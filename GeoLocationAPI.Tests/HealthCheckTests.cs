using GeoLocationAPI.V1.HealthChecks;
using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;

namespace GeoLocationAPI.Tests;

public class HealthCheckTests
{
    private sealed class StubService(Func<IPAddress, GeoLocation> lookup) : IGeoLocationService
    {
        public GeoLocation GetGeoLocation(IPAddress ipAddress) => lookup(ipAddress);
    }

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, HealthStatus.Unhealthy, null),
    };

    [Fact]
    public async Task Healthy_WhenAddressIsFound()
    {
        var check = new GeoLocationHealthCheck(
            new StubService(ip => new GeoLocation(ip.ToString()) { IPFoundInGeoDB = true, Message = "found" }),
            "8.8.8.8");

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("8.8.8.8", result.Data["ipAddress"]);
    }

    [Fact]
    public async Task Unhealthy_WhenAddressIsNotFound()
    {
        var check = new GeoLocationHealthCheck(
            new StubService(ip => new GeoLocation(ip.ToString()) { IPFoundInGeoDB = false, Message = "missing" }),
            "10.0.0.1");

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("missing", result.Description, StringComparison.Ordinal);
        Assert.Equal("10.0.0.1", result.Data["ipAddress"]);
    }

    [Fact]
    public async Task Unhealthy_WhenLookupThrows()
    {
        var failure = new InvalidOperationException("database unavailable");
        var check = new GeoLocationHealthCheck(new StubService(_ => throw failure), "8.8.8.8");

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Same(failure, result.Exception);
    }

    [Fact]
    public void Constructor_RejectsInvalidAddress()
    {
        Assert.Throws<FormatException>(() => new GeoLocationHealthCheck(new StubService(_ => null!), "nope"));
    }
}
