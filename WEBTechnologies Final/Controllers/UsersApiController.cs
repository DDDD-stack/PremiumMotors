using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    [ApiController]
    [Route("api/users")]
    public class UsersApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        public UsersApiController(AppDbContext db) => _db = db;

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest req)
        {
            var username = req.Username.Trim();
            var email = req.Email.Trim();

            if (_db.Users.Any(u => u.Username.ToLower() == username.ToLower()))
                return Conflict(new { error = "That username is already taken." });

            if (_db.Users.Any(u => u.Email.ToLower() == email.ToLower()))
                return Conflict(new { error = "An account with that email already exists." });

            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = PasswordHasher.Hash(req.Password),
                RegisteredUtc = DateTime.UtcNow
            };
            _db.Users.Add(user);
            _db.SaveChanges();
            return Ok(new { user.Id, user.Username, user.Email });
        }

        [HttpPost("validate")]
        public IActionResult Validate([FromBody] ValidateRequest req)
        {
            var user = _db.Users.FirstOrDefault(u => u.Username.ToLower() == req.Username.Trim().ToLower());
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Unauthorized(new { error = "Invalid username or password." });

            return Ok(new { user.Id, user.Username, user.Email });
        }
    }

    public record RegisterRequest(string Username, string Email, string Password);
    public record ValidateRequest(string Username, string Password);
}
