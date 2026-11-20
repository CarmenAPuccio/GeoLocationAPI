using System.ComponentModel.DataAnnotations;

namespace GeoLocationAPI.Configuration;

/// <summary>
/// The network of the load balancer or proxy whose X-Forwarded-* headers are trusted.
/// </summary>
public sealed class ProxyInformation
{
    public const string SectionName = "ProxyInformation";

    [Required]
    public string ProxyKnownNetwork { get; init; } = string.Empty;

    [Range(0, 128)]
    public int ProxyKnownNetworkCIDR { get; init; }
}
