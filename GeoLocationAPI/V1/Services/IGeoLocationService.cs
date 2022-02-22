using GeoLocationAPI.V1.Models;

namespace GeoLocationAPI.V1.Services
{
    /// <summary>
    /// IGeoLocationService Interface
    /// </summary>
    public interface IGeoLocationService
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="incomingIP">The IP passed in from the GeoLocationController</param>
        /// <returns></returns>
        Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP);

    }
}
