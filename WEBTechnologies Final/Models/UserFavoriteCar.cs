using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    // Keyed by the stable user id rather than the username, so a user can be renamed
    // without silently losing their saved listings.
    public class UserFavoriteCar
    {
        public int UserId { get; set; }
        public int CarId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public User? User { get; set; }

        [JsonIgnore]
        public Car? Car { get; set; }
    }
}
