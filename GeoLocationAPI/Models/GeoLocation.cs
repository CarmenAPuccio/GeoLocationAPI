using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GeoLocationAPI.Models
{
    public class GeoLocation
    {
        public DateTime Date { get; set; }

        public string IPAddress { get; set; }

        public string City { get; set; }

        public string TimeZone { get; set; }

        public string Continent { get; set; }

        public string Country { get; set; }
    }
}
