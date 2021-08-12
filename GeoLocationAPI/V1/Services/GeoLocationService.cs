using GeoLocationAPI.V1.Models;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GeoLocationAPI.V1.Services
{
    /// <summary>
    /// GeoLocation Service
    /// </summary>
    public class GeoLocationService : IGeoLocationService
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IOptions<AppSettings> _appSettings;

        /// <summary>
        /// GeoLocationService
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="configuration"></param>
        /// <param name="appSettings"></param>
        public GeoLocationService(
            ILogger<GeoLocationService> logger,
            IConfiguration configuration,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _configuration = configuration;
            _appSettings = appSettings;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="incomingIP">The IP passed in from the IGeoLocationService Interface</param>
        /// <returns></returns>
        public async Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP)

        {
            var response = new GeoLocation();
            if (System.Net.IPAddress.TryParse(incomingIP, out var ipParseResult))
            {
                var geoDB = _appSettings.Value.GeoLite2CityDB;
                response.Date = DateTime.UtcNow.ToUniversalTime();
                response.IPAddress = ipParseResult.ToString();

                using (var reader = new DatabaseReader(geoDB))
                {
                    if (reader.TryCity(response.IPAddress, out var trycityResponse))
                    {
                        var cityResponse = reader.City(response.IPAddress);
                        response.City = cityResponse.City.ToString();
                        response.TimeZone = cityResponse.Location.TimeZone.ToString();
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
