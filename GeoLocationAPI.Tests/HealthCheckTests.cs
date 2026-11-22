using GeoLocationAPI.Configuration;
using GeoLocationAPI.V1.HealthChecks;
using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace GeoLocationAPI.Tests;

public class HealthCheckTests
{
    private sealed class StubService(Func<IPAddress, GeoLocation> lookup) : IGeoLocationService
    {
        public GeoLocation GetGeoLocation(IPAddress ipAddress) => lookup(ipAddress);
    }

    private sealed class ListLogger : ILogger<GeoLocationHealthCheck>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static IOptions<HealthCheckSettings> Settings(string ip) => Options.Create(new HealthCheckSettings { IpToTest = ip });

    private static HealthCheckContext Context() => new()
    {
        Registration = new HealthCheckRegistration("test", _ => null!, HealthStatus.Unhealthy, null),
    };

    [Fact]
    public async Task Healthy_WhenAddressIsFound()
    {
        var logger = new ListLogger();
        var check = new GeoLocationHealthCheck(
            new StubService(ip => new GeoLocation(ip.ToString()) { IPFoundInGeoDB = true, Message = "found" }),
            logger,
            Settings("8.8.8.8"));

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("8.8.8.8", result.Data["ipAddress"]);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Unhealthy_WhenAddressIsNotFound()
    {
        var logger = new ListLogger();
        var check = new GeoLocationHealthCheck(
            new StubService(ip => new GeoLocation(ip.ToString()) { IPFoundInGeoDB = false, Message = "missing" }),
            logger,
            Settings("10.0.0.1"));

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("missing", result.Description, StringComparison.Ordinal);
        Assert.Equal("10.0.0.1", result.Data["ipAddress"]);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("10.0.0.1", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhealthy_WhenLookupThrows()
    {
        var failure = new InvalidOperationException("database unavailable");
        var logger = new ListLogger();
        var check = new GeoLocationHealthCheck(new StubService(_ => throw failure), logger, Settings("8.8.8.8"));

        var result = await check.CheckHealthAsync(Context());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Same(failure, result.Exception);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Exception);
    }

    [Fact]
    public void Constructor_RejectsInvalidAddress()
    {
        Assert.Throws<FormatException>(() => new GeoLocationHealthCheck(new StubService(_ => null!), new ListLogger(), Settings("nope")));
    }
}
