using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Data;
using Microsoft.EntityFrameworkCore;

namespace WEBTechnologies_Final.Controllers
{
    public class CarsController : Controller
    {
        private readonly ApiClient _api;
        private readonly AppDbContext _context;

        public CarsController(ApiClient api, AppDbContext context)
        {
            _api = api;
            _context = context;
        }

        public async Task<IActionResult> Index(
            string? search, CarType? type, string? make,
            string? model, int? year, string sortBy = "newest")
        {
            // Only published (listing-fee-paid or admin) cars appear publicly; drafts stay hidden.
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

            var cars = await query.ToListAsync();

            var makes = await _context.Cars.Select(c => c.Make).Distinct().OrderBy(m => m).ToListAsync();
            var models = await _context.Cars.Select(c => c.Model).Distinct().OrderBy(m => m).ToListAsync();
            var years = await _context.Cars.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToListAsync();

            var vm = new CarListViewModel
            {
                Cars = cars,
                Search = search,
                Type = type,
                Make = make,
                Model = model,
                Year = year,
                SortBy = sortBy,
                TypeOptions = BuildTypeOptions(type),
                MakeOptions = new SelectList(makes, make),
                ModelOptions = new SelectList(models, model),
                YearOptions = new SelectList(years, year),
                SortOptions = BuildSortOptions(sortBy)
            };

            return View(vm);
        }

        public async Task<IActionResult> Details(int id)
        {
            var car = await _context.Cars
                .Include(c => c.Bids)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (car is null) return NotFound();

            var username = HttpContext.Session.GetString(SessionKeys.Username);
            var isAdmin = HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";
            var isSeller = isAdmin || (username is not null
                && string.Equals(username, car.OwnerUsername, StringComparison.OrdinalIgnoreCase));

            // A draft (unpaid) listing is visible only to its owner/admin, never the public.
            if (!car.IsPublished && !isSeller) return NotFound();

            if (username is not null)
            {

                ViewData[$"IsFav_{car.Id}"] = await _context.UserFavoriteCars
                    .AnyAsync(f => f.Username == username && f.CarId == id);
            }

            // Reveal the winning bidder's contact details to the seller once the auction has closed.
            if (isSeller && car.IsSold && car.SoldTo is not null)
            {
                var winner = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == car.SoldTo.ToLower());
                if (winner is not null)
                {
                    ViewData["WinnerEmail"] = winner.Email;
                    ViewData["WinnerPhone"] = winner.Phone;
                }
            }

            return View(car);
        }

        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Bid(int id, decimal amount)
        {
            var bidderName = HttpContext.Session.GetString(SessionKeys.Username)!;

            var car = await _context.Cars.Include(c => c.Bids).FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound();

            if (car.IsClosed)
            {
                TempData["Error"] = "This auction is closed and no longer accepting offers.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Sealed offers don't need to out-bid each other (and revealing the standing
            // total would defeat their privacy) — they only need to be a positive amount.
            if (amount <= 0)
            {
                TempData["Error"] = "Your offer must be greater than zero.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var newBid = new Bid
            {
                CarId = id,
                Amount = amount,
                BidderUsername = bidderName,
                CreatedUtc = DateTime.UtcNow
            };

            _context.Bids.Add(newBid);
            await ConsumeListingTokenAsync(id);

            // Offers are private — the bidder only learns their own offer landed, not the standing total.
            TempData["Success"] = $"Your offer of {amount:C} was sent privately to the seller.";

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id });
        }

        // Once a listing receives an offer, its paid token is spent and can no longer be
        // reclaimed for a free relist — this is what blocks the "never declare a winner" exploit.
        private async Task ConsumeListingTokenAsync(int carId)
        {
            var token = await _context.Payments
                .FirstOrDefaultAsync(p => p.CarId == carId && p.Status == PaymentStatus.Paid && !p.OfferConsumed);
            if (token is not null) token.OfferConsumed = true;
        }

        public async Task<IActionResult> Share(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound();
            ViewData["ListingUrl"] = Url.Action(nameof(Details), "Cars", new { id }, Request.Scheme);
            return View(car);
        }

        private static SelectList BuildTypeOptions(CarType? selected)
        {
            var items = Enum.GetValues<CarType>()
                .Select(t => new { Value = t.ToString(), Text = t.ToString() });
            return new SelectList(items, "Value", "Text", selected?.ToString());
        }

        private static SelectList BuildSortOptions(string? selected)
        {
            var items = new[]
            {
                new { Value = "newest",     Text = "Newest first" },
                new { Value = "price_asc",  Text = "Price: low to high" },
                new { Value = "price_desc", Text = "Price: high to low" },
                new { Value = "year_desc",  Text = "Year: newest" },
                new { Value = "year_asc",   Text = "Year: oldest" },
            };
            return new SelectList(items, "Value", "Text", selected);
        }
    }
}
