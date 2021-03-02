using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GeoLocationAPI.Models
{
    public class AppSettings
    {
        public const string DBSettings = "DBSettings";

        public string GeoLite2CityDB { get; set; }
    }
}
