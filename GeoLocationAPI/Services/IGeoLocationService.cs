using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeoLocationAPI.Models;

namespace GeoLocationAPI.Services
{
    public interface IGeoLocationService
    {
        Task<GeoLocation> GetGeoLocationByIPAsync(string incomingIP);
        
    }
}
