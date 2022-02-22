using GeoLocationAPI.V1.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GeoLocationAPI.V1.HealthChecks
{
    public class GeoLocationHealthCheck : IHealthCheck
    {

        private readonly string _healthcheckBaseURL;
        private readonly string _healtcheckIPToTest;

        public GeoLocationHealthCheck(

            string healthcheckBaseURL,
            string healtcheckIPToTest)
        {

            _healthcheckBaseURL = healthcheckBaseURL;
            _healtcheckIPToTest = healtcheckIPToTest;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
         HealthCheckContext context,
         CancellationToken cancellationToken = default(CancellationToken))
        {
            // No validation done on the url below.
            // Ensure that the HealtcheckBaseURL value from appsettings.json has a trailing slash.
            // ex: "HealtcheckBaseURL": "http://localhost:5000/api/v1/geolocation/"
            string url = _healthcheckBaseURL + _healtcheckIPToTest;
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("From", "GeoLocationHealthCheck@example.com");
            var client = new HttpClient();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            try
            {
                var json = await response.Content.ReadFromJsonAsync<GeoLocation>();
                if (response.IsSuccessStatusCode && json?.Message == _healtcheckIPToTest + " found in the GeoDB")
                {
                    return HealthCheckResult.Healthy("The healthcheck is healthy - " + json?.Message);

                }
                return
                    new HealthCheckResult(
                        context.Registration.FailureStatus, "The healthcheck indicates an unhealthy result." + json?.Message);
            }
            catch (Exception ex)
            {
                return
                    new HealthCheckResult(
                        context.Registration.FailureStatus, "The healthcheck indicates an unhealthy result." + ex.ToString());
            }
        }
    }
}
