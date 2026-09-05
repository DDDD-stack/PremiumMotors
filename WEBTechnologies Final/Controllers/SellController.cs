using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers
{
    // User-facing "Sell your car" flow. A listing is created and published in one step:
    // listing is free and always will be, and advertising is the only thing anyone pays for.
    // The listing-fee flow that used to sit here, and the PayPal adapter behind it, were
    // removed on 5 September 2026 - PayPal is a poor fit for taking money in Albania, and
    // the fee itself contradicted the product.
    //
    // Gated on being a seller: posting a car is what the seller panel is for, and a buyer
    // who lands here is sent through the one-form opt-in first.
    [SellerOnly]
    public class SellController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IPhotoStorage _photos;

        public SellController(AppDbContext context, IPhotoStorage photos)
        {
            _context = context;
            _photos = photos;
        }

        private int CurrentUserId => HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
        private string CurrentUser => HttpContext.Session.GetString(SessionKeys.Username)!;

        [HttpGet]
        public IActionResult Create() =>
            View(new Car { Year = DateTime.UtcNow.Year });

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.ListingFormBytes)]
        public async Task<IActionResult> Create(Car car, List<IFormFile>? photos)
        {
            if (!ModelState.IsValid) return View(car);

            car.OwnerId = CurrentUserId;
            car.OwnerUsername = CurrentUser;
            // Live immediately. There is no fee to settle, no relist token to look for and no
            // provider to wait on, so a draft nobody can see would be a state with nothing to
            // move it out of. Draft still exists as a status for a seller who unpublishes.
            car.Status = ListingStatus.Active;
            car.PublishedUtc = DateTime.UtcNow;
            car.ImagePaths = (await _photos.SaveAsync(photos)).Paths.ToList();

            _context.Cars.Add(car);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"\"{car.Title}\" is now live.";
            return RedirectToAction("Details", "Cars", new { id = car.Id });
        }
    }
}
