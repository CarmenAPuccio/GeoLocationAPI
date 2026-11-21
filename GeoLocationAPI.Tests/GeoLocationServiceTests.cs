using GeoLocationAPI.Configuration;
using GeoLocationAPI.V1.Helpers;
using GeoLocationAPI.V1.Services;
using Microsoft.Extensions.Options;
using System.Net;

namespace GeoLocationAPI.Tests;

public sealed class GeoLocationServiceTests : IDisposable
{
    private readonly Instrumentation _instrumentation = new();
    private readonly GeoLocationService _service;

    public GeoLocationServiceTests()
    {
        var settings = Options.Create(new DbSettings { GeoLite2CityDB = Path.Join(AppContext.BaseDirectory, "GeoLite2-City.mmdb") });
        _service = new GeoLocationService(settings, _instrumentation);
    }

    public void Dispose()
    {
        _service.Dispose();
        _instrumentation.Dispose();
    }

    [Fact]
    public void Lookup_WorksWhenNothingListensForTelemetry()
    {
        // No tracer provider is registered, so StartActivity returns null.
        var found = _service.GetGeoLocation(IPAddress.Parse("8.8.8.8"));
        var missing = _service.GetGeoLocation(IPAddress.Parse("10.0.0.1"));

        Assert.True(found.IPFoundInGeoDB);
        Assert.False(missing.IPFoundInGeoDB);
    }

    [Fact]
    public void NullAddress_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => _service.GetGeoLocation(null!));
    }

    [Fact]
    public void NullOptions_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new GeoLocationService(null!, _instrumentation));
        Assert.Throws<ArgumentNullException>(() => new GeoLocationService(Options.Create(new DbSettings()), null!));
    }
}
