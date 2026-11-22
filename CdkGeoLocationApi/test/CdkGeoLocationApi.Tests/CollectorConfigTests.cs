using System.Text.RegularExpressions;

namespace CdkGeoLocationApi.Tests;

/// <summary>
/// Checks on the collector configuration the task runs. The configuration itself is validated by starting the real
/// collector with it (see the README), these tests keep the intent from drifting.
/// </summary>
public sealed class CollectorConfigTests
{
    private static readonly string[] Pipelines = ["traces", "metrics", "logs"];

    private static string ReadConfig(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Join(directory.FullName, "aws-otel-collector", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException(name);
    }

    [Theory]
    [InlineData("ecs-resource-detection.yaml")]
    [InlineData("local.yaml")]
    public void EveryPipeline_StartsWithTheMemoryLimiter(string file)
    {
        var config = ReadConfig(file);

        foreach (var pipeline in Pipelines)
        {
            var match = Regex.Match(config, @$"^    {pipeline}:\s*\n(?:.*\n)*?\s+processors: \[(?<processors>[^\]]*)\]", RegexOptions.Multiline);
            Assert.True(match.Success, $"{file} has no {pipeline} pipeline");
            Assert.StartsWith("memory_limiter", match.Groups["processors"].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EcsConfig_ReceivesOnlyOtlp_BecauseNothingSendsXRaySegmentsOrStatsd()
    {
        var config = ReadConfig("ecs-resource-detection.yaml");

        Assert.DoesNotContain("awsxray:\n    endpoint", config, StringComparison.Ordinal);
        Assert.DoesNotContain("statsd", config, StringComparison.Ordinal);
        Assert.DoesNotContain("receivers: [otlp, ", config, StringComparison.Ordinal);
    }

    [Fact]
    public void EcsConfig_UsesTheCurrentNameOfTheResourceDetectionProcessor()
    {
        var config = ReadConfig("ecs-resource-detection.yaml");

        Assert.Contains("resource_detection/ecs", config, StringComparison.Ordinal);
        Assert.DoesNotContain("resourcedetection", config, StringComparison.Ordinal);
    }
}
