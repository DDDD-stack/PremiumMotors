using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    public class UserFavoriteCar
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public int CarId { get; set; }
    }
}
