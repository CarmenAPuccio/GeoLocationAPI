using System.ComponentModel.DataAnnotations;

namespace GeoLocationAPI.Configuration;

/// <summary>
/// Location of the MaxMind GeoLite2 City database.
/// </summary>
public sealed class DbSettings
{
    public const string SectionName = "DBSettings";

    /// <summary>
    /// Path to the .mmdb file. A relative path is resolved against the application directory.
    /// </summary>
    [Required]
    public string GeoLite2CityDB { get; init; } = string.Empty;

    public string ResolvedPath => Path.GetFullPath(GeoLite2CityDB, AppContext.BaseDirectory);
}
