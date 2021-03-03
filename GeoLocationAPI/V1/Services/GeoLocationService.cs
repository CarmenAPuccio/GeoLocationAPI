using GeoLocationAPI.V1.Models;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
                if (ipParseResult != null)
                {
                    if (ipParseResult.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        ipParseResult = Dns.GetHostEntry(ipParseResult).AddressList
                            .First(x => x.AddressFamily == AddressFamily.InterNetwork);
                    }
                }

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
                    }
                    else
                    {                        
                        response.IPFoundInGeoDB = false;
                        _logger.LogWarning(response.IPAddress + " not found in the GeoDB");
                        response.Message = (response.IPAddress + " not found in the GeoDB");
                    }
                }
                return await Task.FromResult(response);
            }
            else
            {                
                response.IPFoundInGeoDB = false;
                _logger.LogWarning(incomingIP + " Unable to Parse");
                response.Message = (incomingIP + " Unable to Parse");
                return await Task.FromResult(response);
            }           
            
        }
    }
}
