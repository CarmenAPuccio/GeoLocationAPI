using GeoLocationAPI.Configuration;
using GeoLocationAPI.V1.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net;

namespace GeoLocationAPI.V1.HealthChecks;

/// <summary>
/// Looks up a known IP address in the database to prove the service and data file are usable.
/// </summary>
public sealed partial class GeoLocationHealthCheck : IHealthCheck
{
    private readonly IGeoLocationService _geoLocationService;
    private readonly ILogger<GeoLocationHealthCheck> _logger;
    private readonly IPAddress _ipToTest;

    public GeoLocationHealthCheck(IGeoLocationService geoLocationService, ILogger<GeoLocationHealthCheck> logger, IOptions<HealthCheckSettings> settings)
    {
        _geoLocationService = geoLocationService;
        _logger = logger;
        _ipToTest = IPAddress.Parse(settings.Value.IpToTest);
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = _geoLocationService.GetGeoLocation(_ipToTest);
            var data = new Dictionary<string, object> { ["ipAddress"] = result.IPAddress };

            if (result.IPFoundInGeoDB)
            {
                return Task.FromResult(HealthCheckResult.Healthy("The healthcheck is healthy - " + result.Message, data));
            }

            LogAddressNotFound(_ipToTest.ToString());
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus,
                "The healthcheck indicates an unhealthy result. " + result.Message, data: data));
        }
        catch (Exception ex)
        {
            LogLookupFailed(ex, _ipToTest.ToString());
            return Task.FromResult(new HealthCheckResult(
                context.Registration.FailureStatus,
                "The healthcheck indicates an unhealthy result.",
                ex));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Health check failed: {IpAddress} was not found in the GeoDB")]
    private partial void LogAddressNotFound(string ipAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "Health check failed: the lookup of {IpAddress} threw an exception")]
    private partial void LogLookupFailed(Exception exception, string ipAddress);
}
