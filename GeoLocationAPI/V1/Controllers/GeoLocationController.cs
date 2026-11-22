using Asp.Versioning;
using GeoLocationAPI.V1.Helpers;
using GeoLocationAPI.V1.Models;
using GeoLocationAPI.V1.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace GeoLocationAPI.V1.Controllers;

/// <summary>
/// API for retrieving GeoLocations
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class GeoLocationController : ControllerBase
{
    private readonly IGeoLocationService _geoLocationService;
    private readonly Instrumentation _instrumentation;

    public GeoLocationController(IGeoLocationService geoLocationService, Instrumentation instrumentation)
    {
        _geoLocationService = geoLocationService;
        _instrumentation = instrumentation;
    }

    /// <summary>
    /// Get the GeoLocation for the caller's IP address
    /// </summary>
    /// <response code="200">The lookup completed. IPFoundInGeoDB says whether the address is in the database.</response>
    /// <response code="400">The caller's IP address could not be determined.</response>
    [HttpGet]
    [ProducesResponseType<GeoLocation>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public ActionResult<GeoLocation> GetGeoLocation()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;

        if (remoteIp is null)
        {
            return InvalidIp("Unable to determine the caller's IP address.", null);
        }

        // Dual-stack listeners report IPv4 callers as ::ffff:a.b.c.d.
        return Lookup(remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp);
    }

    /// <summary>
    /// Get the GeoLocation for the IP address the user specified
    /// </summary>
    /// <param name="ipAddress">An IPv4 or IPv6 address, for example 8.8.8.8</param>
    /// <response code="200">The lookup completed. IPFoundInGeoDB says whether the address is in the database.</response>
    /// <response code="400">The value is not a valid IP address.</response>
    [HttpGet("{ipAddress}")]
    [ProducesResponseType<GeoLocation>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public ActionResult<GeoLocation> GetGeoLocationByIP(string ipAddress)
    {
        if (!IpAddressParser.TryParse(ipAddress, out var parsed))
        {
            return InvalidIp($"'{ipAddress}' is not a valid IPv4 or IPv6 address.", ipAddress);
        }

        return Lookup(parsed!);
    }

    // Counted here, not in the service, so the health check's own lookups are not counted as API traffic.
    private GeoLocation Lookup(IPAddress address)
    {
        var location = _geoLocationService.GetGeoLocation(address);
        _instrumentation.LookupCounter.Add(1, new KeyValuePair<string, object?>("geolocation.found", location.IPFoundInGeoDB));
        return location;
    }

    private ObjectResult InvalidIp(string detail, string? ipAddress)
    {
        var problem = ProblemDetailsFactory.CreateProblemDetails(
            HttpContext,
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid IP address",
            detail: detail);

        if (ipAddress is not null)
        {
            problem.Extensions["ipAddress"] = ipAddress;
        }

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }
}
