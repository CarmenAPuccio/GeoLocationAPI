using GeoLocationAPI.V1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GeoLocationAPI.Controllers
{
    /// <summary>
    /// API for retrieving GeoLocations
    /// </summary>
    [ApiController]
    [ApiVersion( "1.0" )]
    [Route( "api/v{version:apiVersion}/[controller]" )]
    //[Route("api/[controller]")]
    public class GeoLocationController : ControllerBase
    {        
        
        private readonly ILogger _logger;
        private readonly IGeoLocationService _geoLocationService;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="geoLocationService"></param>
        /// <param name="logger"></param>
        public GeoLocationController(
            IGeoLocationService geoLocationService,
            ILogger<GeoLocationController> logger
            )

        {
            _geoLocationService = geoLocationService;
            _logger = logger;
        }

        /// <summary>
        /// Get the GeoLocation for the user's IPAddress
        /// </summary>
        /// <response code="200"></response>
        [HttpGet]
        public IActionResult GetGeoLocation()
        {
            var items = _geoLocationService.GetGeoLocationByIPAsync(Request.HttpContext.Connection.RemoteIpAddress.ToString());
            return Ok(items.Result);
        }

        /// <summary>
        ///  Get the GeoLocation for the IPAddress the user specified
        /// </summary>
        /// <response code="200"></response>
        [HttpGet("{IPAddress}")]
        public IActionResult GetGeoLocationByIPAsync(string IPAddress)

        {            
            var items = _geoLocationService.GetGeoLocationByIPAsync(IPAddress);
            return Ok(items.Result);
        }
    }
}
