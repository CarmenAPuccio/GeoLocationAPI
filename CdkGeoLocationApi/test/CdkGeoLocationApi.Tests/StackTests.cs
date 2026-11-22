using System.Text.Json;

namespace CdkGeoLocationApi.Tests;

[Collection(nameof(StackCollectionDefinition))]
public sealed class StackTests
{
    private readonly StackFixture _stack;

    public StackTests(StackFixture stack) => _stack = stack;

    private JsonElement TaskDefinition => Assert.Single(_stack.Resources("AWS::ECS::TaskDefinition"));

    private JsonElement Container(string name) =>
        TaskDefinition.GetProperty("ContainerDefinitions").EnumerateArray().Single(c => c.GetProperty("Name").GetString() == name);

    [Fact]
    public void Vpc_UsesTheNetworkTheApiTrusts()
    {
        var vpc = Assert.Single(_stack.Resources("AWS::EC2::VPC"));
        Assert.Equal("172.25.0.0/16", vpc.GetProperty("CidrBlock").GetString());

        // The API only trusts X-Forwarded-For from this network, so the two must agree.
        var appSettings = JsonDocument.Parse(File.ReadAllText(
            Path.Join(_stack.RepositoryRoot, "GeoLocationAPI", "appsettings.Production.json"))).RootElement.GetProperty("ProxyInformation");
        var cidr = $"{appSettings.GetProperty("ProxyKnownNetwork").GetString()}/{appSettings.GetProperty("ProxyKnownNetworkCIDR").GetInt32()}";
        Assert.Equal(cidr, vpc.GetProperty("CidrBlock").GetString());
    }

    [Fact]
    public void Cluster_HasContainerInsights()
    {
        var cluster = Assert.Single(_stack.Resources("AWS::ECS::Cluster"));
        var setting = Assert.Single(cluster.GetProperty("ClusterSettings").EnumerateArray());
        Assert.Equal("containerInsights", setting.GetProperty("Name").GetString());
        Assert.Equal("enabled", setting.GetProperty("Value").GetString());
    }

    [Fact]
    public void TaskDefinition_RunsOnArm64Fargate_WithRightSizedResources()
    {
        Assert.Equal("GeoLocationAPI", TaskDefinition.GetProperty("Family").GetString());
        Assert.Equal("512", TaskDefinition.GetProperty("Cpu").GetString());
        Assert.Equal("1024", TaskDefinition.GetProperty("Memory").GetString());
        Assert.Equal("ARM64", TaskDefinition.GetProperty("RuntimePlatform").GetProperty("CpuArchitecture").GetString());
        Assert.Equal("LINUX", TaskDefinition.GetProperty("RuntimePlatform").GetProperty("OperatingSystemFamily").GetString());
        Assert.Contains("FARGATE", TaskDefinition.GetProperty("RequiresCompatibilities").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void TaskDefinition_HasTheApiAndCollectorContainers()
    {
        Assert.Equal(2, TaskDefinition.GetProperty("ContainerDefinitions").GetArrayLength());

        var api = Container("GeoLocationAPI");
        Assert.True(api.GetProperty("Essential").GetBoolean());
        Assert.Equal(512, api.GetProperty("Memory").GetInt32());
        Assert.Equal(5254, Assert.Single(api.GetProperty("PortMappings").EnumerateArray()).GetProperty("ContainerPort").GetInt32());
        var environment = Assert.Single(api.GetProperty("Environment").EnumerateArray());
        Assert.Equal("ASPNETCORE_ENVIRONMENT", environment.GetProperty("Name").GetString());
        Assert.Equal("Production", environment.GetProperty("Value").GetString());

        var collector = Container("aws-otel-collector");
        Assert.True(collector.GetProperty("Essential").GetBoolean());
        Assert.Equal("--config=/etc/ecs/ecs-resource-detection.yaml", Assert.Single(collector.GetProperty("Command").EnumerateArray()).GetString());
    }

    [Fact]
    public void Api_StartsAfterTheCollector_AndIsHealthChecked()
    {
        var api = Container("GeoLocationAPI");

        var dependency = Assert.Single(api.GetProperty("DependsOn").EnumerateArray());
        Assert.Equal("aws-otel-collector", dependency.GetProperty("ContainerName").GetString());
        Assert.Equal("START", dependency.GetProperty("Condition").GetString());

        var command = api.GetProperty("HealthCheck").GetProperty("Command").EnumerateArray().Select(c => c.GetString()).ToArray();
        Assert.Equal("CMD-SHELL", command[0]);
        Assert.Contains("http://localhost:5254/hc", command[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Containers_LogToCloudWatch()
    {
        foreach (var name in new[] { "GeoLocationAPI", "aws-otel-collector" })
        {
            Assert.Equal("awslogs", Container(name).GetProperty("LogConfiguration").GetProperty("LogDriver").GetString());
        }
    }

    [Fact]
    public void TaskRole_HasOnlyWhatTheCollectorNeeds()
    {
        var policy = Assert.Single(_stack.Resources("AWS::IAM::Policy"), p => p.GetProperty("PolicyName").GetString()!.Contains("GeoLocationAPITaskRole", StringComparison.Ordinal));
        var statements = policy.GetProperty("PolicyDocument").GetProperty("Statement").EnumerateArray().ToList();

        static IEnumerable<string> Actions(JsonElement statement) => statement.GetProperty("Action").EnumerateArray().Select(a => a.GetString()!);

        var allActions = statements.SelectMany(Actions).ToList();
        Assert.All(allActions, action => Assert.True(
            action.StartsWith("xray:", StringComparison.Ordinal) || action.StartsWith("logs:", StringComparison.Ordinal),
            $"Unexpected action {action}"));

        // X-Ray has no resource-level permissions. Logs must be limited to the collector's log groups.
        var xray = statements.Single(s => Actions(s).All(a => a.StartsWith("xray:", StringComparison.Ordinal)));
        Assert.Equal("*", xray.GetProperty("Resource").GetString());
        Assert.Contains("xray:PutTraceSegments", Actions(xray));

        var logs = statements.Single(s => Actions(s).All(a => a.StartsWith("logs:", StringComparison.Ordinal)));
        Assert.Contains(":log-group:/geolocationapi/otel/*", logs.GetProperty("Resource").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LogGroups_HaveRetention_AndCollectorGroupsMatchTheCollectorConfig()
    {
        var groups = _stack.Resources("AWS::Logs::LogGroup");
        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.Equal(180, g.GetProperty("RetentionInDays").GetInt32()));

        var collectorConfig = File.ReadAllText(Path.Join(_stack.RepositoryRoot, "aws-otel-collector", "ecs-resource-detection.yaml"));
        foreach (var name in new[] { "/geolocationapi/otel/metrics", "/geolocationapi/otel/logs" })
        {
            Assert.Contains(groups, g => g.TryGetProperty("LogGroupName", out var n) && n.GetString() == name);
            Assert.Contains(name, collectorConfig, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Service_RunsTwoTasksPrivately_AndRollsBackFailedDeployments()
    {
        var service = Assert.Single(_stack.Resources("AWS::ECS::Service"));

        Assert.Equal(2, service.GetProperty("DesiredCount").GetInt32());
        Assert.Equal("FARGATE", service.GetProperty("LaunchType").GetString());
        Assert.Equal("DISABLED", service.GetProperty("NetworkConfiguration").GetProperty("AwsvpcConfiguration").GetProperty("AssignPublicIp").GetString());
        Assert.Equal(200, service.GetProperty("DeploymentConfiguration").GetProperty("MaximumPercent").GetInt32());
        Assert.Equal(100, service.GetProperty("DeploymentConfiguration").GetProperty("MinimumHealthyPercent").GetInt32());
        Assert.True(service.GetProperty("DeploymentConfiguration").GetProperty("DeploymentCircuitBreaker").GetProperty("Rollback").GetBoolean());
    }

    [Fact]
    public void LoadBalancer_IsInternetFacing_AndChecksTheHealthEndpoint()
    {
        var lb = Assert.Single(_stack.Resources("AWS::ElasticLoadBalancingV2::LoadBalancer"));
        Assert.Equal("internet-facing", lb.GetProperty("Scheme").GetString());

        var listener = Assert.Single(_stack.Resources("AWS::ElasticLoadBalancingV2::Listener"));
        Assert.Equal(80, listener.GetProperty("Port").GetInt32());
        Assert.Equal("HTTP", listener.GetProperty("Protocol").GetString());

        var target = Assert.Single(_stack.Resources("AWS::ElasticLoadBalancingV2::TargetGroup"));
        Assert.Equal("/hc", target.GetProperty("HealthCheckPath").GetString());
        Assert.Equal(2, target.GetProperty("HealthyThresholdCount").GetInt32());
        Assert.Contains(target.GetProperty("TargetGroupAttributes").EnumerateArray(),
            a => a.GetProperty("Key").GetString() == "deregistration_delay.timeout_seconds" && a.GetProperty("Value").GetString() == "10");
    }

    [Fact]
    public void Output_PointsAtTheApi()
    {
        var output = _stack.Main.GetProperty("Outputs").GetProperty("GeoLocationAPILoadBalancerURL").GetProperty("Value").ToString();

        Assert.Contains("/api/v1/geolocation", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_AreBuiltForArm64_FromTheRepositoryRoot()
    {
        var images = _stack.AssetManifest.GetProperty("dockerImages").EnumerateObject().Select(i => i.Value.GetProperty("source")).ToList();

        Assert.Equal(2, images.Count);
        Assert.All(images, i => Assert.Equal("linux/arm64", i.GetProperty("platform").GetString()));

        var api = Assert.Single(images, i => i.TryGetProperty("dockerFile", out _));
        Assert.Equal("GeoLocationAPI/Dockerfile", api.GetProperty("dockerFile").GetString());
        Assert.Equal(_stack.RepositoryRoot, api.GetProperty("directory").GetString());
        Assert.True(File.Exists(Path.Join(_stack.RepositoryRoot, api.GetProperty("dockerFile").GetString()!)));

        var collector = Assert.Single(images, i => !i.TryGetProperty("dockerFile", out _));
        Assert.Equal(Path.Join(_stack.RepositoryRoot, "aws-otel-collector"), collector.GetProperty("directory").GetString());
    }

    private static JsonElement ApiImageSource(string sourceRevision, string root, string outputDirectory)
    {
        var app = new Amazon.CDK.App(new Amazon.CDK.AppProps
        {
            Outdir = outputDirectory,
            Context = new Dictionary<string, object> { ["aws:cdk:disable-asset-staging"] = true },
        });
        new CdkGeoLocationApiStack(app, "RevisionStack", new Amazon.CDK.StackProps(), root, sourceRevision);
        var assets = JsonDocument.Parse(File.ReadAllText(Path.Join(app.Synth().Directory, "RevisionStack.assets.json"))).RootElement;

        return assets.GetProperty("dockerImages").EnumerateObject().Select(i => i.Value.GetProperty("source")).Single(s => s.TryGetProperty("dockerFile", out _));
    }

    [Fact]
    public void ApiImage_ReceivesTheSourceRevision_SoServiceVersionNamesTheCommit()
    {
        var output = Path.Join(Path.GetTempPath(), "cdk-geolocationapi-revision-" + Guid.NewGuid().ToString("N"));
        try
        {
            var api = ApiImageSource("3f9a2c1", _stack.RepositoryRoot, output);

            Assert.Equal("3f9a2c1", api.GetProperty("dockerBuildArgs").GetProperty("SOURCE_REVISION_ID").GetString());
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void ApiImage_HasNoBuildArgsWithoutARevision()
    {
        var output = Path.Join(Path.GetTempPath(), "cdk-geolocationapi-norevision-" + Guid.NewGuid().ToString("N"));
        try
        {
            var api = ApiImageSource(string.Empty, _stack.RepositoryRoot, output);

            var hasRevisionArg = api.TryGetProperty("dockerBuildArgs", out var args) && args.TryGetProperty("SOURCE_REVISION_ID", out _);

            Assert.False(hasRevisionArg);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void Stack_DefaultsRepositoryRootToTheParentDirectory()
    {
        var original = Environment.CurrentDirectory;
        var scratch = Path.Join(Path.GetTempPath(), "cdk-geolocationapi-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Join(scratch, "work"));
        Directory.CreateDirectory(Path.Join(scratch, "aws-otel-collector"));
        Directory.CreateDirectory(Path.Join(scratch, "GeoLocationAPI"));
        File.WriteAllText(Path.Join(scratch, "GeoLocationAPI", "Dockerfile"), "FROM scratch");
        File.WriteAllText(Path.Join(scratch, "aws-otel-collector", "Dockerfile"), "FROM scratch");

        try
        {
            // cdk runs from CdkGeoLocationApi, so ".." is the repository root.
            Environment.CurrentDirectory = Path.Join(scratch, "work");
            var app = Program.BuildApp(new Amazon.CDK.AppProps
            {
                Outdir = Path.Join(scratch, "out"),
                Context = new Dictionary<string, object> { ["aws:cdk:disable-asset-staging"] = true },
            });
            var assets = JsonDocument.Parse(File.ReadAllText(Path.Join(app.Synth().Directory, "CdkGeoLocationApiStack.assets.json"))).RootElement;

            var api = assets.GetProperty("dockerImages").EnumerateObject().Select(i => i.Value.GetProperty("source")).Single(s => s.TryGetProperty("dockerFile", out _));
            // Compare names, because the temp directory is a symlink on macOS (/var is /private/var).
            Assert.Equal(Path.GetFileName(scratch), Path.GetFileName(api.GetProperty("directory").GetString()!));
        }
        finally
        {
            Environment.CurrentDirectory = original;
            Directory.Delete(scratch, recursive: true);
        }
    }
}
