using System.Text.RegularExpressions;

namespace CdkGeoLocationApi.Tests;

[Collection(nameof(StackCollectionDefinition))]
public sealed class WafStackTests
{
    private readonly StackFixture _stack;

    public WafStackTests(StackFixture stack) => _stack = stack;

    private System.Text.Json.JsonElement WebAcl => Assert.Single(StackFixture.ResourcesIn(_stack.Waf, "AWS::WAFv2::WebACL"));

    [Fact]
    public void WebAcl_BlocksByDefault_AndIsRegional()
    {
        Assert.Equal("REGIONAL", WebAcl.GetProperty("Scope").GetString());
        Assert.True(WebAcl.GetProperty("DefaultAction").TryGetProperty("Block", out _));
    }

    [Fact]
    public void WebAcl_AppliesTheAwsManagedRuleGroupsInOrder()
    {
        var rules = WebAcl.GetProperty("Rules").EnumerateArray().OrderBy(r => r.GetProperty("Priority").GetInt32()).ToList();

        var managed = rules.Where(r => r.GetProperty("Statement").TryGetProperty("ManagedRuleGroupStatement", out _)).ToList();
        Assert.Equal(
            new[]
            {
                "AWSManagedRulesCommonRuleSet",
                "AWSManagedRulesKnownBadInputsRuleSet",
                "AWSManagedRulesLinuxRuleSet",
                "AWSManagedRulesUnixRuleSet",
                "AWSManagedRulesAmazonIpReputationList",
                "AWSManagedRulesAnonymousIpList",
            },
            managed.Select(r => r.GetProperty("Statement").GetProperty("ManagedRuleGroupStatement").GetProperty("Name").GetString()));
        Assert.All(managed, r =>
        {
            Assert.Equal("AWS", r.GetProperty("Statement").GetProperty("ManagedRuleGroupStatement").GetProperty("VendorName").GetString());
            Assert.True(r.GetProperty("OverrideAction").TryGetProperty("None", out _));
        });

        // After the managed groups: the rate limit, then the allow rule. Allow ends evaluation, so it must be last.
        Assert.Equal(
            new[] { "GeoLocationAPI-RateLimit", "GeoLocationAPI-Regex-Rule" },
            rules.Skip(managed.Count).Select(r => r.GetProperty("Name").GetString()));
        Assert.Equal(
            Enumerable.Range(0, rules.Count),
            rules.Select(r => r.GetProperty("Priority").GetInt32()));
    }

    [Fact]
    public void WebAcl_LimitsEachIpTo300RequestsPer300Seconds_AndAnswers429()
    {
        var rule = WebAcl.GetProperty("Rules").EnumerateArray().Single(r => r.GetProperty("Name").GetString() == "GeoLocationAPI-RateLimit");

        var rate = rule.GetProperty("Statement").GetProperty("RateBasedStatement");
        Assert.Equal(300, rate.GetProperty("Limit").GetInt32());
        Assert.Equal(300, rate.GetProperty("EvaluationWindowSec").GetInt32());
        Assert.Equal("IP", rate.GetProperty("AggregateKeyType").GetString());

        Assert.Equal(429, rule.GetProperty("Action").GetProperty("Block").GetProperty("CustomResponse").GetProperty("ResponseCode").GetInt32());
        Assert.False(rule.TryGetProperty("OverrideAction", out _));
    }

    [Fact]
    public void WebAcl_AllowsOnlyTheApiAndHealthCheckPaths()
    {
        var allow = WebAcl.GetProperty("Rules").EnumerateArray().Single(r => r.GetProperty("Name").GetString() == "GeoLocationAPI-Regex-Rule");
        Assert.True(allow.GetProperty("Action").TryGetProperty("Allow", out _));

        var patterns = allow.GetProperty("Statement").GetProperty("OrStatement").GetProperty("Statements").EnumerateArray()
            .Select(s => new Regex(s.GetProperty("RegexMatchStatement").GetProperty("RegexString").GetString()!, RegexOptions.None))
            .ToList();
        Assert.Equal(2, patterns.Count);

        bool Allowed(string path) => patterns.Any(p => p.IsMatch(path));

        // The API and the health check, with any casing, version number and an optional trailing slash.
        foreach (var path in new[]
        {
            "/hc", "/HC", "/hc/",
            "/api/v1/geolocation", "/api/v1/geolocation/", "/api/v1/geolocation/8.8.8.8",
            "/api/V12/GeoLocation/2001:4860:4860::8888",
        })
        {
            Assert.True(Allowed(path), $"{path} should be allowed");
        }

        // Everything else, including paths that merely contain an allowed path.
        foreach (var path in new[]
        {
            "/", "/admin", "/scalar/v1", "/openapi/v1.json", "/api/v1/other",
            "/x/hc", "/hcx", "/hc/extra", "/hc\n",
            "/api/v1/geolocation/8.8.8.8/extra", "/api/v1/geolocationx", "/xapi/v1/geolocation", "/api/v/geolocation",
            "/admin/api/v1/geolocation",
        })
        {
            Assert.False(Allowed(path), $"{path} should be blocked");
        }
    }

    [Fact]
    public void WebAcl_RulesEmitMetricsAndSampleRequests()
    {
        Assert.True(WebAcl.GetProperty("VisibilityConfig").GetProperty("CloudWatchMetricsEnabled").GetBoolean());
        Assert.All(WebAcl.GetProperty("Rules").EnumerateArray(), r =>
        {
            Assert.True(r.GetProperty("VisibilityConfig").GetProperty("CloudWatchMetricsEnabled").GetBoolean());
            Assert.True(r.GetProperty("VisibilityConfig").GetProperty("SampledRequestsEnabled").GetBoolean());
        });
    }

    [Fact]
    public void WebAcl_LogsToACloudWatchGroupWithTheRequiredPrefix()
    {
        var logGroup = Assert.Single(StackFixture.ResourcesIn(_stack.Waf, "AWS::Logs::LogGroup"));
        Assert.StartsWith("aws-waf-logs-", logGroup.GetProperty("LogGroupName").GetString(), StringComparison.Ordinal);
        Assert.Equal(180, logGroup.GetProperty("RetentionInDays").GetInt32());

        var logging = Assert.Single(StackFixture.ResourcesIn(_stack.Waf, "AWS::WAFv2::LoggingConfiguration"));
        var destination = Assert.Single(logging.GetProperty("LogDestinationConfigs").EnumerateArray()).ToString();
        Assert.Contains(":log-group:", destination, StringComparison.Ordinal);
        // The ARN is built from the log group name. WAF rejects a log group ARN that ends in ":*",
        // which is what the log group's own ARN attribute returns.
        Assert.DoesNotContain("Fn::GetAtt", destination, StringComparison.Ordinal);
        Assert.DoesNotContain(":*", destination, StringComparison.Ordinal);
    }

    [Fact]
    public void WebAcl_IsAssociatedWithTheLoadBalancer()
    {
        var association = Assert.Single(StackFixture.ResourcesIn(_stack.Waf, "AWS::WAFv2::WebACLAssociation"));

        Assert.True(association.TryGetProperty("ResourceArn", out _));
        Assert.True(association.TryGetProperty("WebACLArn", out _));
    }
}
