using GeoLocationAPI.V1.Models;
using System.Net;

namespace GeoLocationAPI.V1.Services;

public interface IGeoLocationService
{
    /// <summary>
    /// Looks up the location of an IP address in the GeoLite2 City database.
    /// </summary>
    GeoLocation GetGeoLocation(IPAddress ipAddress);
}
