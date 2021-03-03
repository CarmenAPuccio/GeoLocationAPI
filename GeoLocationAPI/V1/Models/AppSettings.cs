using System;

namespace GeoLocationAPI.V1.Models
{
    /// <summary>
    /// Represents the appsettings.json for Configuration
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// DBSettings in appsettings.json
        /// </summary>
        public const string DBSettings = "DBSettings";

        /// <summary>
        /// Path to GeoLite2-City.mmdb file
        /// </summary>
        public string GeoLite2CityDB { get; set; }
    }
}
