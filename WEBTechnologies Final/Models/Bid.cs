using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    public class Bid
    {
        public int Id { get; set; }

        [Required]
        public int CarId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [Column("BidderName")]
        public string BidderUsername { get; set; } = string.Empty;

        [Required]
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public Car? Car { get; set; }
    }
}
