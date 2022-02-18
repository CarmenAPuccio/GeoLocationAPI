using GeoLocationAPI.V1.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using GeoLocationAPI.V1.Models;

namespace GeoLocationAPI.V1.Controllers
{
    /// <summary>
    /// API for retrieving GeoLocations
    /// </summary>
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Produces("application/json")]
    public class GeoLocationController : ControllerBase
    {
        //private readonly ILogger<GeoLocationController> _logger;
        private readonly IGeoLocationService _geoLocationService;
        /// <summary>
        /// 
        /// </summary>
        /// <param name="geoLocationService"></param>
        ///// <param name="logger"></param>
        public GeoLocationController(
            IGeoLocationService geoLocationService
            //ILogger<GeoLocationController> logger
            )

        {
            _geoLocationService = geoLocationService;
            //_logger = logger;
        }

        /// <summary>
        /// Get the GeoLocation for the user's IPAddress
        /// </summary>
        /// <response code="200"></response>
        [HttpGet]
        public async Task<ActionResult<GeoLocation>> GetGeoLocation()
        {
            if (Request.HttpContext.Connection.RemoteIpAddress is null)
            {
                return BadRequest();
            }
            var items = await _geoLocationService.GetGeoLocationByIPAsync(Request.HttpContext.Connection.RemoteIpAddress.ToString());
            return Ok(items);
        }

        /// <summary>
        ///  Get the GeoLocation for the IPAddress the user specified
        /// </summary>
        /// <response code="200"></response>
        [HttpGet("{IPAddress}")]
        public async Task<ActionResult<GeoLocation>> GetGeoLocationByIPAsync(string IPAddress)

        {
            var items = await _geoLocationService.GetGeoLocationByIPAsync(IPAddress);
            return Ok(items);
        }
    }
}
