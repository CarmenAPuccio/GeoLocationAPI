using System.ComponentModel.DataAnnotations;

namespace GeoLocationAPI.Configuration;

/// <summary>
/// Settings for the <c>/hc</c> health check.
/// </summary>
public sealed class HealthCheckSettings
{
    public const string SectionName = "HealthCheck";

    /// <summary>
    /// The address the health check looks up. It must be in the database for the check to be healthy.
    /// </summary>
    [Required]
    public string IpToTest { get; init; } = string.Empty;
}
