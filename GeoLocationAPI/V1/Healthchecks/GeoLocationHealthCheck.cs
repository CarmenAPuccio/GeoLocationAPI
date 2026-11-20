using GeoLocationAPI.V1.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Net;

namespace GeoLocationAPI.V1.HealthChecks
{
    /// <summary>
    /// Looks up a known IP address in the database to prove the service and data file are usable.
    /// </summary>
    public sealed class GeoLocationHealthCheck : IHealthCheck
    {
        private readonly IGeoLocationService _geoLocationService;
        private readonly IPAddress _ipToTest;

        public GeoLocationHealthCheck(IGeoLocationService geoLocationService, string ipToTest)
        {
            _geoLocationService = geoLocationService;
            _ipToTest = IPAddress.Parse(ipToTest);
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var result = _geoLocationService.GetGeoLocation(_ipToTest);
                var data = new Dictionary<string, object> { ["ipAddress"] = result.IPAddress };

                return Task.FromResult(result.IPFoundInGeoDB
                    ? HealthCheckResult.Healthy("The healthcheck is healthy - " + result.Message, data)
                    : new HealthCheckResult(context.Registration.FailureStatus,
                        "The healthcheck indicates an unhealthy result. " + result.Message, data: data));
            }
            catch (Exception ex)
            {
                return Task.FromResult(new HealthCheckResult(
                    context.Registration.FailureStatus,
                    "The healthcheck indicates an unhealthy result.",
                    ex));
            }
        }
    }
}
