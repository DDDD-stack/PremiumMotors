using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    // User-facing "Sell your car" flow: create a draft listing, then pay the listing fee
    // via the payment provider (PayPal) before it is published.
    [LoggedInOnly]
    public class SellController : Controller
    {
        private readonly AppDbContext _context;
        private readonly PhotoService _photos;
        private readonly IPaymentProvider _pay;
        private readonly ListingOptions _listing;

        public SellController(
            AppDbContext context, PhotoService photos, IPaymentProvider pay,
            IOptions<ListingOptions> listing)
        {
            _context = context;
            _photos = photos;
            _pay = pay;
            _listing = listing.Value;
        }

        private string CurrentUser => HttpContext.Session.GetString(SessionKeys.Username)!;

        [HttpGet]
        public IActionResult Create() =>
            View(new Car { Year = DateTime.UtcNow.Year, AuctionEnd = DateTime.Now.AddDays(7) });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Car car, List<IFormFile>? photos)
        {
            if (car.AuctionEnd.HasValue && car.AuctionEnd.Value <= DateTime.Now)
            {
                ModelState.AddModelError(nameof(Car.AuctionEnd), "The auction end date must be in the future.");
            }
            if (!ModelState.IsValid) return View(car);

            car.OwnerUsername = CurrentUser;
            car.IsPublished = false;
            car.ImagePaths = await _photos.SaveAsync(photos);
            _context.Cars.Add(car);
            await _context.SaveChangesAsync();

            // 1) A free relist is available if the seller has a paid, unconsumed token from a
            //    previous zero-offer auction (capped by MaxFreeRelists). Reuse it, no charge.
            var reusable = await _context.Payments
                .Where(p => p.Username == CurrentUser
                            && p.Status == PaymentStatus.Paid
                            && !p.OfferConsumed
                            && p.CarId == null
                            && p.RelistCount < _listing.MaxFreeRelists)
                .OrderBy(p => p.Id)
                .FirstOrDefaultAsync();

            if (reusable is not null)
            {
                reusable.CarId = car.Id;
                reusable.RelistCount++;
                car.IsPublished = true;
                await _context.SaveChangesAsync();
                TempData["Success"] = $"\"{car.Title}\" is live again — your earlier listing got no offers, so this relist was free.";
                return RedirectToAction("Details", "Cars", new { id = car.Id });
            }

            var payment = new Payment
            {
                Username = CurrentUser,
                CarId = car.Id,
                AmountCents = _listing.ListingFeeCents,
                Currency = _listing.Currency,
                Provider = "paypal"
            };

            // 2) Launch / free-listing mode: no fee configured, publish immediately.
            if (_listing.ListingFeeCents <= 0)
            {
                payment.Status = PaymentStatus.Paid;
                payment.PaidUtc = DateTime.UtcNow;
                car.IsPublished = true;
                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"\"{car.Title}\" is now live.";
                return RedirectToAction("Details", "Cars", new { id = car.Id });
            }

            // 3) Paid listing: create the PayPal order and send the seller to approve it.
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            var returnUrl = Url.Action(nameof(Success), "Sell", new { paymentId = payment.Id }, Request.Scheme)!;
            var cancelUrl = Url.Action(nameof(Cancel), "Sell", new { carId = car.Id }, Request.Scheme)!;

            try
            {
                var checkout = await _pay.CreateListingCheckoutAsync(payment, car, returnUrl, cancelUrl);
                payment.ProviderOrderId = checkout.OrderId;
                await _context.SaveChangesAsync();
                return Redirect(checkout.RedirectUrl);
            }
            catch (Exception)
            {
                TempData["Error"] = "We couldn't start the payment. Your listing was saved as a draft — try publishing it again from your account.";
                return RedirectToAction("Details", "Cars", new { id = car.Id });
            }
        }

        // Buyer returns here from PayPal. We capture the order server-side to confirm payment
        // (the redirect itself is not trusted — the capture API call is the verification).
        [HttpGet]
        public async Task<IActionResult> Success(int paymentId)
        {
            var payment = await _context.Payments.Include(p => p.Car)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.Username == CurrentUser);
            if (payment is null) return NotFound();

            if (payment.Status != PaymentStatus.Paid && payment.ProviderOrderId is not null)
            {
                var captureId = await _pay.CaptureAsync(payment.ProviderOrderId);
                if (captureId is not null)
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidUtc = DateTime.UtcNow;
                    payment.ProviderCaptureId = captureId;
                    if (payment.Car is not null) payment.Car.IsPublished = true;
                    await _context.SaveChangesAsync();
                }
            }

            return View(payment);
        }

        [HttpGet]
        public async Task<IActionResult> Cancel(int carId)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == carId && c.OwnerUsername == CurrentUser);
            return View(car);
        }
    }
}
