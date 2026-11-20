using GeoLocationAPI.Configuration;
using GeoLocationAPI.V1.Helpers;
using GeoLocationAPI.V1.Models;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;

namespace GeoLocationAPI.V1.Services
{
    /// <summary>
    /// GeoLocation Service. Holds a single memory-mapped database reader for the life of the application.
    /// </summary>
    public sealed partial class GeoLocationService : IGeoLocationService, IDisposable
    {
        private readonly ILogger<GeoLocationService> _logger;
        private readonly Instrumentation _instrumentation;
        private readonly DatabaseReader _reader;

        public GeoLocationService(
            ILogger<GeoLocationService> logger,
            IOptions<DbSettings> dbSettings,
            Instrumentation instrumentation)
        {
            ArgumentNullException.ThrowIfNull(dbSettings);
            ArgumentNullException.ThrowIfNull(instrumentation);

            _logger = logger;
            _instrumentation = instrumentation;
            _reader = new DatabaseReader(dbSettings.Value.ResolvedPath);
        }

        /// <inheritdoc />
        public GeoLocation GetGeoLocation(IPAddress ipAddress)
        {
            ArgumentNullException.ThrowIfNull(ipAddress);

            using var activity = _instrumentation.ActivitySource.StartActivity("GetGeoLocation");

            var address = ipAddress.ToString();
            var response = new GeoLocation(address) { Date = DateTime.UtcNow };

            if (_reader.TryCity(ipAddress, out var city) && city is not null)
            {
                response.City = city.City.Name;
                response.TimeZone = city.Location.TimeZone;
                response.Continent = city.Continent.Name;
                response.Country = city.Country.Name;
                response.IPFoundInGeoDB = true;
                response.Message = $"{address} found in the GeoDB";
            }
            else
            {
                response.IPFoundInGeoDB = false;
                response.Message = $"{address} not found in the GeoDB";
                LogNotFound(address);
            }

            activity?.SetTag("geolocation.found", response.IPFoundInGeoDB);
            _instrumentation.LookupCounter.Add(1, new KeyValuePair<string, object?>("geolocation.found", response.IPFoundInGeoDB));
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "{IpAddress} not found in the GeoDB")]
        private partial void LogNotFound(string ipAddress);
    }
}
