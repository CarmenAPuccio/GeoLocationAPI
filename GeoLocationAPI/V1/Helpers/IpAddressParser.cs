using System.Net;
using System.Net.Sockets;

namespace GeoLocationAPI.V1.Helpers;

/// <summary>
/// Strict IP address parsing for user input.
/// </summary>
public static class IpAddressParser
{
    // Longest textual IPv6 form (an IPv4-mapped address) is 45 characters.
    private const int MaxLength = 45;

    /// <summary>
    /// <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> accepts shorthand such as "1" or "127.1"
    /// and IPv6 scope ids ("fe80::1%eth0"). Neither is meaningful for a geolocation lookup, so reject them.
    /// </summary>
    public static bool TryParse(string? value, out IPAddress? address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Contains('%'))
        {
            return false;
        }

        if (!IPAddress.TryParse(value, out var parsed))
        {
            return false;
        }

        if (parsed.AddressFamily == AddressFamily.InterNetwork && value.Count(c => c == '.') != 3)
        {
            return false;
        }

        address = parsed;
        return true;
    }
}
