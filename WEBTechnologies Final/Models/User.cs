namespace WEBTechnologies_Final.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Contact number revealed to a seller only for the winning offer on their listing.
        public string Phone { get; set; } = string.Empty;

        public string PasswordHash { get; set; } = string.Empty;
        public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    }
}
