using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GeoLocationAPI.Tests;

// Listeners are process-wide, so these tests must not overlap with other tests that perform lookups.
[Collection(nameof(TelemetryTests))]
public sealed class TelemetryTests : IClassFixture<GeoLocationApiFactory>
{
    private const string SourceName = "GeoLocationAPI";

    private readonly HttpClient _client;

    public TelemetryTests(GeoLocationApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Lookup_RecordsActivityWithFoundTag()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await _client.GetAsync("/api/v1/geolocation/8.8.8.8");

        var activity = Assert.Single(activities, a => a.OperationName == "GetGeoLocation");
        Assert.Equal(true, activity.GetTagItem("geolocation.found"));
        // Unset means "no error". Ok is reserved for explicitly marking a span as successful.
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
    }

    private async Task<Activity> LookupSpan(Action<HttpRequestMessage>? configure = null)
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/geolocation/8.8.8.8");
        configure?.Invoke(request);
        await _client.SendAsync(request);

        return Assert.Single(activities, a => a.OperationName == "GetGeoLocation");
    }

    [Fact]
    public async Task IncomingXRayTraceHeader_ContinuesTheTrace()
    {
        // The ALB adds this header to every request it forwards.
        var span = await LookupSpan(request => request.Headers.Add(
            "X-Amzn-Trace-Id", "Root=1-5759e988-bd862e3fe1be46a994272793;Parent=53995c3f42cd8ad8;Sampled=1"));

        Assert.Equal("5759e988bd862e3fe1be46a994272793", span.TraceId.ToHexString());
        Assert.Equal(ActivityTraceFlags.Recorded, span.ActivityTraceFlags);
    }

    [Fact]
    public async Task IncomingTraceparentHeader_ContinuesTheTrace()
    {
        var span = await LookupSpan(request => request.Headers.Add(
            "traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"));

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", span.TraceId.ToHexString());
    }

    [Fact]
    public async Task WhenBothHeadersArrive_TheCallersTraceparentWins()
    {
        // The load balancer adds X-Amzn-Trace-Id to every request, including ones that already carry traceparent.
        var span = await LookupSpan(request =>
        {
            request.Headers.Add("traceparent", "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
            request.Headers.Add("X-Amzn-Trace-Id", "Root=1-5759e988-bd862e3fe1be46a994272793;Parent=53995c3f42cd8ad8;Sampled=1");
        });

        Assert.Equal("0af7651916cd43dd8448eb211c80319c", span.TraceId.ToHexString());
    }

    [Fact]
    public async Task NewTraces_UseXRayCompatibleIds()
    {
        // X-Ray rejects trace IDs whose first 8 hex digits (the epoch time in seconds) are not recent.
        var span = await LookupSpan();

        var epochSeconds = Convert.ToInt64(span.TraceId.ToHexString()[..8], 16);
        Assert.InRange(epochSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("10.0.0.1", false)]
    public async Task Lookup_IncrementsCounterTaggedWithResult(string ip, bool expectedFound)
    {
        var measurements = new ConcurrentBag<(long Value, object? Found)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == SourceName && instrument.Name == "geolocation.lookups")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags.ToArray().Where(t => t.Key == "geolocation.found"))
            {
                measurements.Add((value, tag.Value));
            }
        });
        listener.Start();

        await _client.GetAsync($"/api/v1/geolocation/{ip}");

        var measurement = Assert.Single(measurements);
        Assert.Equal(1, measurement.Value);
        Assert.Equal(expectedFound, measurement.Found);
    }
}

[CollectionDefinition(nameof(TelemetryTests), DisableParallelization = true)]
public sealed class TelemetryTestsDefinition;
