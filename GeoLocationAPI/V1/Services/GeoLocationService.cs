using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Helpers;
using MaxMind.GeoIP2;
using System.Diagnostics;

namespace GeoLocationAPI.V1.Services
{
    /// <summary>
    /// GeoLocation Service
    /// </summary>
    public class GeoLocationService : IGeoLocationService
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly ActivitySource _activitySource;

        /// <summary>
        /// GeoLocationService
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="configuration"></param>
        /// <param name="instrumentation"></param>
        public GeoLocationService(
            ILogger<GeoLocationService> logger,
            IConfiguration configuration,
            Instrumentation instrumentation)
        {
            _logger = logger;
            ArgumentNullException.ThrowIfNull(instrumentation);
            _activitySource = instrumentation.ActivitySource;
            _configuration = configuration;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="incomingIP">The IP passed in from the IGeoLocationService Interface</param>
        /// <returns></returns>       

        public async Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP)
        {
            using var activity = _activitySource.StartActivity("GetGeoLocationByIPAsync");

            var response = new GeoLocation(incomingIP);
            if (System.Net.IPAddress.TryParse(incomingIP, out var ipParseResult))
            {
                var geoDB = _configuration["DBSettings:GeoLite2CityDB"];
                response.Date = DateTime.UtcNow.ToUniversalTime();
                response.IPAddress = ipParseResult.ToString();

                using (var reader = new DatabaseReader(geoDB))
                {
                    if (reader.TryCity(response.IPAddress, out var trycityResponse))
                    {
                        var cityResponse = reader.City(response.IPAddress);
                        response.City = cityResponse.City.ToString();
                        response.TimeZone = cityResponse.Location.TimeZone?.ToString();
                        response.Continent = cityResponse.Continent.ToString();
                        response.Country = cityResponse.Country.ToString();
                        response.IPFoundInGeoDB = true;
                        response.Message = response.IPAddress + " found in the GeoDB";
                        activity?.SetTag("message", response.IPAddress + " found in the GeoDB");
                        Activity.Current?.SetStatus(ActivityStatusCode.Ok);
                    }
                    else
                    {
                        response.IPFoundInGeoDB = false;
                        _logger.LogWarning(response.IPAddress + " not found in the GeoDB");
                        response.Message = response.IPAddress + " not found in the GeoDB";
                        activity?.SetTag("message", response.IPAddress + " not found in the GeoDB");
                        Activity.Current?.SetStatus(ActivityStatusCode.Ok);
                    }
                }
                return await Task.FromResult(response);
            }
            else
            {
                response.IPFoundInGeoDB = false;
                _logger.LogWarning(incomingIP + " Unable to Parse");
                response.Message = incomingIP + " Unable to Parse";
                activity?.SetTag("message", incomingIP + " Unable to Parse");
                Activity.Current?.SetStatus(ActivityStatusCode.Error);
                return await Task.FromResult(response);
            }
        }
    }
}
