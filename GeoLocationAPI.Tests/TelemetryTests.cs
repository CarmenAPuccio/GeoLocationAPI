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
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
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
            foreach (var tag in tags)
            {
                if (tag.Key == "geolocation.found")
                {
                    measurements.Add((value, tag.Value));
                }
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
