using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using GeoLocationAPI.Models;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;

namespace GeoLocationAPI.Services
{
    public class GeoLocationService : IGeoLocationService
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IOptions<AppSettings> _appSettings;

        public GeoLocationService(
            ILogger<GeoLocationService> logger,
            IConfiguration configuration,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _configuration = configuration;
            _appSettings = appSettings;
        }

        public async Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP)

        {
            var geoDB = _appSettings.Value.GeoLite2CityDB;
            var response = new GeoLocation();
            response.Date = DateTime.UtcNow.ToUniversalTime();
            response.IPAddress = incomingIP;

            using (var reader = new DatabaseReader(geoDB))
            {
                if (reader.TryCity(response.IPAddress, out var trycityResponse))
                {
                    var cityResponse = reader.City(response.IPAddress);
                    response.City = cityResponse.City.ToString();
                    response.TimeZone = cityResponse.Location.TimeZone.ToString();
                    response.Continent = cityResponse.Continent.ToString();
                    response.Country = cityResponse.Country.ToString();
                }
                else
                {
                    _logger.LogWarning(response.IPAddress + " not found in the GeoDB");
                }
            }

            return await Task.FromResult(response);
            
        }
    }
}
