using GeoLocationAPI.V1.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace GeoLocationAPI.Tests;

/// <summary>The API with in-memory exporters, so tests can look at what would be sent to the collector.</summary>
public class InMemoryTelemetryFactory : GeoLocationApiFactory
{
    public List<Activity> Activities { get; } = [];

    public List<Metric> Metrics { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Activities));
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(Metrics));
        });
    }

    public void Flush()
    {
        Services.GetRequiredService<TracerProvider>().ForceFlush();
        Services.GetRequiredService<MeterProvider>().ForceFlush();
    }
}

// Meter listeners are process-wide, so these run alone, like the other telemetry tests.
[Collection(nameof(TelemetryTests))]
public sealed class TelemetryPipelineTests : IDisposable
{
    private readonly InMemoryTelemetryFactory _factory = new();
    private readonly HttpClient _client;

    public TelemetryPipelineTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    private static IEnumerable<string?> Paths(IEnumerable<Activity> activities) =>
        activities.Select(a => a.GetTagItem("url.path")?.ToString());

    private static long Sum(Metric metric)
    {
        long total = 0;
        foreach (var point in metric.GetMetricPoints())
        {
            total += point.GetSumLong();
        }

        return total;
    }

    private static string[] TagKeys(Metric metric)
    {
        var keys = new HashSet<string>();
        foreach (var point in metric.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                keys.Add(tag.Key);
            }
        }

        return [.. keys.Order()];
    }

    [Fact]
    public async Task HealthChecks_AreNotTraced_ButApiRequestsAre()
    {
        await _client.GetAsync("/hc");
        await _client.GetAsync("/api/v1/geolocation/8.8.8.8");
        _factory.Flush();

        var paths = Paths(_factory.Activities).ToList();
        Assert.Contains("/api/v1/geolocation/8.8.8.8", paths);
        Assert.DoesNotContain("/hc", paths);
        // The lookup span under the health check request is dropped with its parent.
        Assert.Single(_factory.Activities, a => a.OperationName == "GetGeoLocation");
    }

    [Fact]
    public async Task HealthChecks_AreNotCountedAsLookups()
    {
        await _client.GetAsync("/hc");
        _factory.Flush();
        Assert.DoesNotContain(_factory.Metrics, m => m.Name == "geolocation.lookups");

        await _client.GetAsync("/api/v1/geolocation/8.8.8.8");
        _factory.Flush();
        var lookups = Assert.Single(_factory.Metrics, m => m.Name == "geolocation.lookups");
        Assert.Equal(1, Sum(lookups));
    }

    [Fact]
    public async Task OnlyTheWantedMetrics_AreExported_WithTrimmedTags()
    {
        await _client.GetAsync("/api/v1/geolocation/8.8.8.8");
        await _client.GetAsync("/api/v1/geolocation/not-an-ip");
        await _client.GetAsync("/hc");
        _factory.Flush();

        var names = _factory.Metrics.Select(m => m.Name).Distinct().ToList();
        Assert.Contains("http.server.request.duration", names);
        Assert.Contains("geolocation.lookups", names);
        Assert.All(names, name => Assert.True(
            name is "http.server.request.duration" or "http.server.active_requests" or "geolocation.lookups" || name.StartsWith("dotnet.", StringComparison.Ordinal),
            $"Unexpected metric {name}"));

        var duration = _factory.Metrics.Last(m => m.Name == "http.server.request.duration");
        Assert.Equal(["http.request.method", "http.response.status_code", "http.route"], TagKeys(duration));

        // The failing endpoints are visible by status code, including the health check.
        var statuses = new List<string>();
        foreach (var point in duration.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                if (tag.Key == "http.response.status_code")
                {
                    statuses.Add(tag.Value?.ToString() ?? string.Empty);
                }
            }
        }

        Assert.Contains("200", statuses);
        Assert.Contains("400", statuses);
    }

    [Fact]
    public void Production_KeepsOnlyWarningsOnTheConsole()
    {
        using var production = new ProductionApiFactory();
        var rules = production.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules;

        Assert.Contains(rules, r => r.ProviderName is not null && r.ProviderName.EndsWith("ConsoleLoggerProvider", StringComparison.Ordinal) && r.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void Development_KeepsTheFullConsoleLog()
    {
        var rules = _factory.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules;

        Assert.DoesNotContain(rules, r => r.ProviderName is not null && r.ProviderName.EndsWith("ConsoleLoggerProvider", StringComparison.Ordinal));
    }
}
