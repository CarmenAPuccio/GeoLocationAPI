using GeoLocationAPI.V1.Models;
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

        /// <summary>
        /// GeoLocationService
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="configuration"></param>
        public GeoLocationService(
            ILogger<GeoLocationService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="incomingIP">The IP passed in from the IGeoLocationService Interface</param>
        /// <returns></returns>
        public async Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP)

        {
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
                        response.Message = (response.IPAddress + " found in the GeoDB");
                        Activity.Current?.AddEvent(new ActivityEvent(response.IPAddress + " found in the GeoDB"));
                        Activity.Current?.SetTag("otel.status_code", "OK");
                    }
                    else
                    {
                        response.IPFoundInGeoDB = false;
                        _logger.LogWarning(response.IPAddress + " not found in the GeoDB");
                        response.Message = (response.IPAddress + " not found in the GeoDB");
                        Activity.Current?.AddEvent(new ActivityEvent(response.IPAddress + " not found in the GeoDB"));
                        Activity.Current?.SetTag("otel.status_code", "OK");
                    }
                }
                return await Task.FromResult(response);
            }
            else
            {
                response.IPFoundInGeoDB = false;
                _logger.LogWarning(incomingIP + " Unable to Parse");
                response.Message = (incomingIP + " Unable to Parse");
                Activity.Current?.SetTag("otel.status_code", "Error");
                Activity.Current?.SetTag("otel.status_description", incomingIP + " Unable to Parse");
                return await Task.FromResult(response);
            }

        }
    }
}
