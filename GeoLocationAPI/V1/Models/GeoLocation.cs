using System.ComponentModel.DataAnnotations;

namespace GeoLocationAPI.V1.Models
{
    /// <summary>
    /// Represents a GeoLocation
    /// </summary>
    public class GeoLocation
    {
        public GeoLocation(string ipAddress)
        {
            IPAddress = ipAddress;
        }
        /// <summary>
        /// Gets or sets the Date UtcNow
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Gets or sets the IPAddress
        /// </summary>
        [Required(ErrorMessage = "The IPAddress is required.")]
        public string IPAddress { get; set; }

        /// <summary>
        /// Gets or sets the City for the IPAddress
        /// </summary>
        public string? City { get; set; }

        /// <summary>
        /// Gets or sets the TimeZone for the IPAddress
        /// </summary>
        public string? TimeZone { get; set; }

        /// <summary>
        /// Gets or sets the Continent for the IPAddress
        /// </summary>
        public string? Continent { get; set; }

        /// <summary>
        /// Gets or sets the Country for the IPAddress
        /// </summary>
        public string? Country { get; set; }

        /// <summary>
        /// Bool result if the IPAddress is found in the GeoDB
        /// </summary>
        public bool IPFoundInGeoDB { get; set; }

        /// <summary>
        /// Message to send back to the user
        /// </summary>
        public string? Message { get; set; }
    }
}
