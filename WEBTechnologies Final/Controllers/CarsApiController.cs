using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Controllers.Api
{
    [ApiController]
    [Route("api/cars")]
    public class CarsApiController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CarsApiController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Car>>> GetCars(
            [FromQuery] string? search,
            [FromQuery] CarType? type,
            [FromQuery] string? make,
            [FromQuery] string? model,
            [FromQuery] int? year,
            [FromQuery] string sortBy = "newest")
        {
            var query = _context.Cars.Where(c => c.IsPublished).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c => c.Make.Contains(search) || c.Model.Contains(search) || c.Description.Contains(search));
            }
            if (type.HasValue)
            {
                query = query.Where(c => c.Type == type.Value);
            }
            if (!string.IsNullOrEmpty(make))
            {
                query = query.Where(c => c.Make == make);
            }
            if (!string.IsNullOrEmpty(model))
            {
                query = query.Where(c => c.Model == model);
            }
            if (year.HasValue)
            {
                query = query.Where(c => c.Year == year.Value);
            }

            query = sortBy switch
            {
                "price_asc" => query.OrderBy(c => c.StartingPrice),
                "price_desc" => query.OrderByDescending(c => c.StartingPrice),
                "year_asc" => query.OrderBy(c => c.Year),
                "year_desc" => query.OrderByDescending(c => c.Year),
                _ => query.OrderByDescending(c => c.Id)
            };

            return await query.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Car>> GetCar(int id)
        {
            var car = await _context.Cars
                .Include(c => c.Bids)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (car is null) return NotFound();
            return car;
        }

        [HttpGet("makes")]
        public async Task<ActionResult<IEnumerable<string>>> GetMakes()
        {
            return await _context.Cars
                .Select(c => c.Make)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync();
        }

        [HttpGet("models")]
        public async Task<ActionResult<IEnumerable<string>>> GetModels()
        {
            return await _context.Cars
                .Select(c => c.Model)
                .Distinct()
                .OrderBy(m => m)
                .ToListAsync();
        }

        [HttpGet("years")]
        public async Task<ActionResult<IEnumerable<int>>> GetYears()
        {
            return await _context.Cars
                .Select(c => c.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();
        }

        [HttpGet("{id}/is-favorite")]
        public async Task<ActionResult<bool>> IsFavorite(int id, [FromQuery] string username)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

            return await _context.UserFavoriteCars
                .AnyAsync(f => f.Username == username && f.CarId == id);
        }

        [HttpPost("{id}/bid")]
        public async Task<ActionResult<Car>> PlaceBid(int id, [FromQuery] string username, [FromQuery] decimal amount)
        {
            if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

            var car = await _context.Cars.Include(c => c.Bids).FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound("Car listing not found.");

            decimal currentPrice = car.Bids.Any() ? car.Bids.Max(b => b.Amount) : car.StartingPrice;

            if (amount <= currentPrice)
            {
                return BadRequest($"Bid must be higher than current price of {currentPrice:C}");
            }

            var bid = new Bid
            {
                CarId = id,
                Amount = amount,
                BidderUsername = username,
                CreatedUtc = DateTime.UtcNow
            };

            _context.Bids.Add(bid);

            // First offer spends the seller's listing token (no free relist after this).
            var token = await _context.Payments
                .FirstOrDefaultAsync(p => p.CarId == id && p.Status == PaymentStatus.Paid && !p.OfferConsumed);
            if (token is not null) token.OfferConsumed = true;

            await _context.SaveChangesAsync();
            return Ok(car);
        }
    }
}
