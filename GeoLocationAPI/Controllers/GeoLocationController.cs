using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GeoLocationAPI.Models;
using MaxMind.GeoIP2;
using GeoLocationAPI.Services;

namespace GeoLocationAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GeoLocationController : ControllerBase
    {        
        
        private readonly ILogger _logger;
        private readonly IGeoLocationService _geoLocationService;

        public GeoLocationController(
            IGeoLocationService geoLocationService,
            ILogger<GeoLocationController> logger
            )

        {
            _geoLocationService = geoLocationService;
            _logger = logger;
        }

        // GET: api/GeoLocation
        [HttpGet]
        public IActionResult GetGeoLocation()
        {
            IPAddress remoteIP = Request.HttpContext.Connection.RemoteIpAddress;
            if (remoteIP != null)
            {
                if (remoteIP.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    remoteIP = Dns.GetHostEntry(remoteIP).AddressList
                        .First(x => x.AddressFamily == AddressFamily.InterNetwork);
                }
            }

            var items = _geoLocationService.GetGeoLocationByIPAsync(remoteIP.ToString());
            return Ok(items.Result);
        }

        // GET: api/GeoLocation/8.8.8.8
        [HttpGet("{IPAddress}")]
        public IActionResult GetGeoLocationByIPAsync(string IPAddress)

        {            
            var items = _geoLocationService.GetGeoLocationByIPAsync(IPAddress);

            return Ok(items.Result);
        }
    }
}
