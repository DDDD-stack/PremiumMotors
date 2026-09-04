using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// Saved listings for the signed-in account, so favourites follow the user between the
    /// website and the mobile app.
    ///
    /// The previous version took the username from the URL, which let anyone read or modify
    /// anyone elses favourites. Identity now comes from the bearer token only.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/favorites")]
    [Produces("application/json")]
    public class FavoritesApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _current;
        private readonly IMediaUrlResolver _urls;

        public FavoritesApiController(AppDbContext db, ICurrentUser current, IMediaUrlResolver urls)
        {
            _db = db;
            _current = current;
            _urls = urls;
        }

        private int UserId => _current.UserId!.Value;

        /// <summary>The full favourite listings, newest saved first.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<CarSummaryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFavorites(CancellationToken ct)
        {
            var cars = await _db.UserFavoriteCars
                .Where(f => f.UserId == UserId)
                .OrderByDescending(f => f.CreatedUtc)
                .Join(_db.Cars, f => f.CarId, c => c.Id, (f, c) => c)
                .ToListAsync(ct);

            return Ok(cars.Select(c => CarSummaryDto.From(c, _urls)).ToList());
        }

        /// <summary>Just the ids, for cheaply painting heart icons over a cached list.</summary>
        [HttpGet("ids")]
        [ProducesResponseType(typeof(IEnumerable<int>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetFavoriteIds(CancellationToken ct) =>
            Ok(await _db.UserFavoriteCars
                .Where(f => f.UserId == UserId)
                .Select(f => f.CarId)
                .ToListAsync(ct));

        [HttpGet("{carId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> IsFavorite(int carId, CancellationToken ct)
        {
            var isFav = await _db.UserFavoriteCars.AnyAsync(f => f.UserId == UserId && f.CarId == carId, ct);
            return Ok(new { carId, isFavorite = isFav });
        }

        [HttpPut("{carId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Add(int carId, CancellationToken ct)
        {
            if (!await _db.Cars.AnyAsync(c => c.Id == carId, ct))
                return NotFound(new ApiError("Listing not found.", "not_found"));

            var exists = await _db.UserFavoriteCars.AnyAsync(f => f.UserId == UserId && f.CarId == carId, ct);
            if (!exists)
            {
                _db.UserFavoriteCars.Add(new UserFavoriteCar
                {
                    UserId = UserId,
                    CarId = carId,
                    CreatedUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }

            // Idempotent, so a retry after a dropped mobile connection is harmless.
            return Ok(new { carId, isFavorite = true });
        }

        [HttpDelete("{carId:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Remove(int carId, CancellationToken ct)
        {
            var existing = await _db.UserFavoriteCars
                .FirstOrDefaultAsync(f => f.UserId == UserId && f.CarId == carId, ct);

            if (existing is not null)
            {
                _db.UserFavoriteCars.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }

            return Ok(new { carId, isFavorite = false });
        }

        [HttpPost("{carId:int}/toggle")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Toggle(int carId, CancellationToken ct)
        {
            if (!await _db.Cars.AnyAsync(c => c.Id == carId, ct))
                return NotFound(new ApiError("Listing not found.", "not_found"));

            var existing = await _db.UserFavoriteCars
                .FirstOrDefaultAsync(f => f.UserId == UserId && f.CarId == carId, ct);

            if (existing is not null)
            {
                _db.UserFavoriteCars.Remove(existing);
                await _db.SaveChangesAsync(ct);
                return Ok(new { carId, isFavorite = false });
            }

            _db.UserFavoriteCars.Add(new UserFavoriteCar
            {
                UserId = UserId,
                CarId = carId,
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
            return Ok(new { carId, isFavorite = true });
        }
    }
}
