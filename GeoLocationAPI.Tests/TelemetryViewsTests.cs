using GeoLocationAPI.V1.Helpers;
using OpenTelemetry.Metrics;
using System.Diagnostics.Metrics;

namespace GeoLocationAPI.Tests;

public sealed class TelemetryViewsTests : IDisposable
{
    private readonly Meter _meter = new("TelemetryViewsTests");

    public void Dispose() => _meter.Dispose();

    [Fact]
    public void RequestDuration_KeepsOnlyMethodRouteStatusAndErrorType()
    {
        var view = TelemetryViews.Configure(_meter.CreateHistogram<double>("http.server.request.duration"));

        Assert.NotNull(view);
        Assert.Equal(["http.request.method", "http.route", "http.response.status_code", "error.type"], view.TagKeys!);
    }

    [Fact]
    public void RequestDuration_HasACardinalityLimitBelowTheDefault()
    {
        var view = TelemetryViews.Configure(_meter.CreateHistogram<double>("http.server.request.duration"));

        Assert.NotNull(view);
        Assert.Equal(100, view.CardinalityLimit);
    }

    [Fact]
    public void ActiveRequests_DropsAllTags()
    {
        var view = TelemetryViews.Configure(_meter.CreateUpDownCounter<long>("http.server.active_requests"));

        Assert.NotNull(view);
        Assert.Empty(view.TagKeys!);
    }

    [Theory]
    [InlineData("geolocation.lookups")]
    [InlineData("dotnet.gc.collections")]
    [InlineData("dotnet.gc.pause.time")]
    [InlineData("dotnet.gc.heap.total_allocated")]
    [InlineData("dotnet.gc.last_collection.heap.size")]
    [InlineData("dotnet.process.cpu.time")]
    [InlineData("dotnet.process.memory.working_set")]
    [InlineData("dotnet.thread_pool.queue.length")]
    [InlineData("dotnet.thread_pool.thread.count")]
    [InlineData("dotnet.exceptions")]
    public void WantedMetrics_AreKeptAsTheyAre(string name)
    {
        Assert.Null(TelemetryViews.Configure(_meter.CreateCounter<long>(name)));
    }

    [Theory]
    [InlineData("kestrel.active_connections")]
    [InlineData("kestrel.connection.duration")]
    [InlineData("dns.lookup.duration")]
    [InlineData("http.client.request.duration")]
    [InlineData("aspnetcore.memory_pool.allocated")]
    [InlineData("dotnet.jit.compiled_methods")]
    [InlineData("dotnet.timer.count")]
    [InlineData("something.new")]
    public void EverythingElse_IsDropped(string name)
    {
        var view = TelemetryViews.Configure(_meter.CreateCounter<long>(name));

        Assert.Same(MetricStreamConfiguration.Drop, view);
    }

    [Fact]
    public void NullInstrument_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => TelemetryViews.Configure(null!));
    }
}
