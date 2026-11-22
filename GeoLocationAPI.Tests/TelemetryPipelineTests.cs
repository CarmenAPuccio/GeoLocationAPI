using GeoLocationAPI.V1.Helpers;
using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Net;

namespace GeoLocationAPI.Tests;

/// <summary>The API with in-memory exporters, so tests can look at what would be sent to the collector.</summary>
public class InMemoryTelemetryFactory : GeoLocationApiFactory
{
    public List<Activity> Activities { get; } = [];

    public List<Metric> Metrics { get; } = [];

    public List<LogRecord> Logs { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(Activities));
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(Metrics));
            services.ConfigureOpenTelemetryLoggerProvider(logging => logging.AddInMemoryExporter(Logs));
        });
    }

    public void Flush()
    {
        Services.GetRequiredService<TracerProvider>().ForceFlush();
        Services.GetRequiredService<MeterProvider>().ForceFlush();
        Services.GetRequiredService<LoggerProvider>().ForceFlush();
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

    private sealed class ThrowingService : IGeoLocationService
    {
        public GeoLocation GetGeoLocation(IPAddress ipAddress) => throw new InvalidOperationException("database unavailable");
    }

    private sealed class UnhealthyFactory : InMemoryTelemetryFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // A private address is valid but not in the database, so the health check fails.
            builder.UseSetting("HealthCheck:IpToTest", "10.0.0.1");
        }
    }

    private sealed class NamedServiceFactory : InMemoryTelemetryFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("OTEL_SERVICE_NAME", "name-from-environment");
        }
    }

    private sealed class FailingLookupFactory : InMemoryTelemetryFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IGeoLocationService>(new ThrowingService()));
        }
    }

    [Fact]
    public async Task UnhandledException_IsRecordedOnTheRequestSpan()
    {
        using var failing = new FailingLookupFactory();
        var client = failing.CreateClient();

        var response = await client.GetAsync("/api/v1/geolocation/8.8.8.8");
        failing.Flush();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var span = Assert.Single(failing.Activities, a => a.GetTagItem("url.path")?.ToString() == "/api/v1/geolocation/8.8.8.8");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var exception = Assert.Single(span.Events, e => e.Name == "exception");
        Assert.Equal("System.InvalidOperationException", exception.Tags.Single(t => t.Key == "exception.type").Value);
        Assert.Equal("database unavailable", exception.Tags.Single(t => t.Key == "exception.message").Value);
    }

    [Fact]
    public async Task UnhandledException_AddsErrorTypeToTheRequestMetric()
    {
        using var failing = new FailingLookupFactory();
        var client = failing.CreateClient();

        await client.GetAsync("/api/v1/geolocation/8.8.8.8");
        failing.Flush();

        var duration = failing.Metrics.Last(m => m.Name == "http.server.request.duration");
        var errorTypes = new List<string>();
        foreach (var point in duration.GetMetricPoints())
        {
            foreach (var tag in point.Tags)
            {
                if (tag.Key == "error.type")
                {
                    errorTypes.Add(tag.Value?.ToString() ?? string.Empty);
                }
            }
        }

        Assert.Contains("System.InvalidOperationException", errorTypes);
    }

    [Fact]
    public void RuntimeMetrics_ComeFromTheBuiltInMeter()
    {
        _factory.Flush();

        var names = _factory.Metrics.Select(m => m.Name).Where(n => n.StartsWith("dotnet.", StringComparison.Ordinal)).ToList();

        Assert.Contains("dotnet.gc.collections", names);
        Assert.Contains("dotnet.process.memory.working_set", names);
    }

    [Fact]
    public async Task LogRecords_IncludeTheFormattedMessage()
    {
        using var unhealthy = new UnhealthyFactory();
        var client = unhealthy.CreateClient();

        var response = await client.GetAsync("/hc");
        unhealthy.Flush();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var record = Assert.Single(unhealthy.Logs, l => l.CategoryName == "GeoLocationAPI.V1.HealthChecks.GeoLocationHealthCheck");
        Assert.Equal("Health check failed: 10.0.0.1 was not found in the GeoDB", record.FormattedMessage);
        Assert.Equal(LogLevel.Warning, record.LogLevel);
    }

    [Fact]
    public void ServiceName_ComesFromTheEnvironmentWhenSet()
    {
        using var named = new NamedServiceFactory();

        var attributes = named.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("name-from-environment", attributes["service.name"]);
    }

    [Fact]
    public void Resource_DescribesTheServiceAndItsEnvironment()
    {
        var attributes = _factory.Services.GetRequiredService<TracerProvider>().GetResource().Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("GeoLocationAPI", attributes["service.name"]);
        Assert.Equal("GeoLocation", attributes["service.namespace"]);
        Assert.Equal("Development", attributes["deployment.environment.name"]);
        // The informational version ("1.0.0", plus "+<commit>" when built from git), not the four-part assembly version.
        Assert.Matches(@"^\d+\.\d+\.\d+(\+[0-9a-f]+)?$", attributes["service.version"].ToString());
        // One per process, so telemetry from the two tasks can be told apart.
        Assert.True(Guid.TryParse(attributes["service.instance.id"].ToString(), out _));
    }

    [Fact]
    public void Metrics_AreSentAsDeltas()
    {
        // The collector's CloudWatch (EMF) exporter drops the first interval of every cumulative series,
        // because it has nothing to subtract. Deltas need no baseline, so no counts are lost after a task starts.
        var reader = _factory.Services.GetRequiredService<IOptionsMonitor<MetricReaderOptions>>().Get(name: null);

        Assert.Equal(MetricReaderTemporalityPreference.Delta, reader.TemporalityPreference);
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
