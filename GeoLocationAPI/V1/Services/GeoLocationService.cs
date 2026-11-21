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
    /// The service does not log looked-up addresses or put them on its own span, because an address can be the
    /// caller's. The outcome is recorded as the <c>geolocation.found</c> span attribute and the
    /// <c>geolocation.lookups</c> counter. (The request span still has ASP.NET Core's standard URL and client attributes.)
    /// </summary>
    public sealed class GeoLocationService : IGeoLocationService, IDisposable
    {
        private readonly Instrumentation _instrumentation;
        private readonly DatabaseReader _reader;

        public GeoLocationService(
            IOptions<DbSettings> dbSettings,
            Instrumentation instrumentation)
        {
            ArgumentNullException.ThrowIfNull(dbSettings);
            ArgumentNullException.ThrowIfNull(instrumentation);

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
            }

            // The span is not recorded when the request itself is not (for example the health check).
            // Status stays Unset, which means "no error". Ok is only for when success must be stated explicitly.
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag("geolocation.found", response.IPFoundInGeoDB);
            }

            return response;
        }

        public void Dispose()
        {
            _reader.Dispose();
        }
    }
}
