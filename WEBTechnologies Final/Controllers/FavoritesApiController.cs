using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Controllers
{
    [ApiController]
    [Route("api/favorites")]
    public class FavoritesApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        public FavoritesApiController(AppDbContext db) => _db = db;

        [HttpGet("{username}")]
        public IActionResult GetFavorites(string username)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

            var carIds = _db.UserFavoriteCars
                .Where(f => f.Username.ToLower() == username.Trim().ToLower())
                .Select(f => f.CarId)
                .ToList();

            return Ok(carIds);
        }

        [HttpGet("{username}/{carId}")]
        public IActionResult IsFavorite(string username, int carId)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

            var isFav = _db.UserFavoriteCars.Any(f =>
                f.Username.ToLower() == username.Trim().ToLower() &&
                f.CarId == carId
            );

            return Ok(new { isFavorite = isFav });
        }

        [HttpPost("{username}/{carId}/toggle")]
        public IActionResult Toggle(string username, int carId)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

            var formattedUsername = username.Trim();

            var existing = _db.UserFavoriteCars.FirstOrDefault(f =>
                f.Username.ToLower() == formattedUsername.ToLower() &&
                f.CarId == carId
            );

            if (existing is not null)
            {
                _db.UserFavoriteCars.Remove(existing);
                _db.SaveChanges();
                return Ok(new { isFavorite = false });
            }

            _db.UserFavoriteCars.Add(new UserFavoriteCar
            {
                Username = formattedUsername,
                CarId = carId
            });

            _db.SaveChanges();
            return Ok(new { isFavorite = true });
        }
    }
}
