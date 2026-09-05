using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers
{
    public class CarsController : Controller
    {
        private readonly AppDbContext _context;
        private readonly OfferService _offers;
        private readonly ConversationService _conversations;
        private readonly ListingExtrasService _extras;
        private readonly ListingViewService _views;

        public CarsController(
            AppDbContext context, OfferService offers, ConversationService conversations,
            ListingExtrasService extras, ListingViewService views)
        {
            _context = context;
            _offers = offers;
            _conversations = conversations;
            _extras = extras;
            _views = views;
        }

        /// <summary>
        /// How many paid listings can be pinned to the first page of results.
        ///
        /// A ceiling exists because the value of promotion is relative: if every car on the
        /// page is an advert, being an advert buys nothing, and the page stops being a
        /// marketplace. Twelve is half a default page.
        /// </summary>
        private const int MaxPromotedPerPage = 12;

        /// <summary>
        /// The mix on page one: two paid listings, then one free, repeating until the paid
        /// ones run out - after which the rest of the page is free listings in their ordinary
        /// order. Adverts are therefore two thirds of the top of the page and none of the
        /// rest of it.
        ///
        /// Mixing rather than fencing adverts off in a block above the results is the part
        /// that matters. A separate advert block leaves free sellers looking like the
        /// consolation prize, and a free seller who concludes the site is pay-to-be-seen
        /// simply lists elsewhere - taking their stock, and the buyers who came for that
        /// stock, with them.
        ///
        /// These two numbers are the site's most sensitive commercial dial. Raising the paid
        /// share raises what promotion is worth right up until free sellers stop bothering,
        /// at which point there is nothing left to advertise against. Change them here; the
        /// interleaving itself is in CarQueries.MixPromoted and is covered by tests.
        /// </summary>
        private const int PromotedPerBlock = 2;
        private const int FreePerBlock = 1;

        public async Task<IActionResult> Index(
            string? search, CarType? type, string? make, string? model, int? year,
            decimal? minPrice, decimal? maxPrice, int? maxMileage,
            FuelType? fuel, TransmissionType? gearbox, bool includeSold = false,
            string sortBy = "newest", int page = 1, int pageSize = 24)
        {
            // Drafts and archived listings exist only for their seller. Sold cars are kept out
            // of the browse grid too: nobody is shopping for a car they cannot buy, so a sold
            // card is clutter between the ones a buyer can actually act on.
            //
            // Reserved stays. A reservation falls through often enough that hiding it would
            // lose real sales, and the card says so loudly.
            //
            // This hides sold cars from browsing only. Their listing page still resolves, so
            // saved links, favourites and a seller's own pages keep working.
            var query = _context.Cars.Where(c => c.Status != ListingStatus.Draft
                                                 && c.Status != ListingStatus.Archived);

            // Off by default, because the default question a buyer is asking is "what can I
            // buy". Available on demand, because the answer to "what did one like this go
            // for" is worth more than any asking price and this is the only place a buyer
            // can get it. /Cars/Sold is the same data with the sale prices attached.
            if (!includeSold)
                query = query.Where(c => c.Status != ListingStatus.Sold);

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(c =>
                    c.Make.Contains(search) || c.Model.Contains(search) || c.Description.Contains(search));
            }
            if (type.HasValue) query = query.Where(c => c.Type == type.Value);
            if (!string.IsNullOrEmpty(make)) query = query.Where(c => c.Make == make);
            if (!string.IsNullOrEmpty(model)) query = query.Where(c => c.Model == model);
            if (year.HasValue) query = query.Where(c => c.Year == year.Value);
            if (minPrice.HasValue) query = query.Where(c => c.Price >= minPrice.Value);
            if (maxPrice.HasValue) query = query.Where(c => c.Price <= maxPrice.Value);
            if (maxMileage.HasValue) query = query.Where(c => c.Mileage <= maxMileage.Value);
            if (fuel.HasValue) query = query.Where(c => c.FuelType == fuel.Value);
            if (gearbox.HasValue) query = query.Where(c => c.Transmission == gearbox.Value);

            // Captured before sorting and paging, so the promoted strip is drawn from exactly
            // the same filtered set the buyer is looking at. A promoted diesel estate shown to
            // someone filtering for petrol hatchbacks is worth nothing to either of them.
            var filtered = query;

            query = sortBy switch
            {
                "price_asc" => query.OrderBy(c => c.Price),
                "price_desc" => query.OrderByDescending(c => c.Price),
                "year_asc" => query.OrderBy(c => c.Year),
                "year_desc" => query.OrderByDescending(c => c.Year),
                "mileage_asc" => query.OrderBy(c => c.Mileage),
                _ => query.OrderByDescending(c => c.Id)
            };

            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 96 ? 24 : pageSize;

            // Paid placement matching the current filters. Pinned to page one and mixed into
            // the results there rather than paginated with them, so the same car can never
            // show up twice across two pages.
            var promoted = page == 1
                ? await filtered
                    .WherePromoted(PromotionTier.Promoted, DateTime.UtcNow)
                    .OrderByPromotion()
                    .Take(MaxPromotedPerPage)
                    .ToListAsync()
                : new List<Car>();

            // Everything that is not pinned. Excluded by id rather than by re-running the
            // promotion conditions, so the two can never disagree about what is promoted.
            var pinnedIds = promoted.Select(c => c.Id).ToList();
            var freeQuery = query.Where(c => !pinnedIds.Contains(c.Id));

            var freeCount = await freeQuery.CountAsync();
            var free = await freeQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            var cars = CarQueries.MixPromoted(free, promoted, PromotedPerBlock, FreePerBlock);
            var totalCount = freeCount + await filtered
                .WherePromoted(PromotionTier.Promoted, DateTime.UtcNow)
                .CountAsync();

            // Same visibility rule as the grid, so the filter dropdowns cannot offer a make
            // whose only cars are sold and then return "no cars match your search".
            var listed = _context.Cars.Where(c => c.Status != ListingStatus.Draft
                                                  && c.Status != ListingStatus.Archived);
            if (!includeSold)
                listed = listed.Where(c => c.Status != ListingStatus.Sold);
            var makes = await listed.Select(c => c.Make).Distinct().OrderBy(m => m).ToListAsync();
            var models = await listed.Select(c => c.Model).Distinct().OrderBy(m => m).ToListAsync();
            var years = await listed.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToListAsync();

            // Both strips are made of the same card, so they need the same lookups. Built from
            // everything the page will actually render, in one pass rather than two.
            var onPage = cars.Concat(promoted).DistinctBy(c => c.Id).ToList();

            // One query for every favourite on this page, rather than one per card.
            await LoadFavouritesAsync(onPage.Select(c => c.Id));

            // Seller bylines and price history for the whole page in two queries. See
            // ListingExtrasService for why this is not done per card.
            await LoadExtrasAsync(onPage);

            var vm = new CarListViewModel
            {
                Cars = cars,
                FreeCount = freeCount,
                Search = search,
                Type = type,
                Make = make,
                Model = model,
                Year = year,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                MaxMileage = maxMileage,
                Fuel = fuel,
                Gearbox = gearbox,
                IncludeSold = includeSold,
                SortBy = sortBy,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TypeOptions = EnumOptions<CarType>(type?.ToString()),
                MakeOptions = new SelectList(makes, make),
                ModelOptions = new SelectList(models, model),
                YearOptions = new SelectList(years, year),
                FuelOptions = EnumOptions<FuelType>(fuel?.ToString()),
                GearboxOptions = EnumOptions<TransmissionType>(gearbox?.ToString()),
                SortOptions = BuildSortOptions(sortBy)
            };

            return View(vm);
        }

        public async Task<IActionResult> Details(int id)
        {
            var car = await _context.Cars
                .Include(c => c.Offers)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (car is null) return NotFound();

            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId);
            var isAdmin = HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";
            var isSeller = IsSellerOf(car, userId, isAdmin);

            // A draft or archived listing simply does not exist for anyone but its seller.
            if (!car.IsPubliclyVisible && !isSeller) return NotFound();

            ViewData["IsSeller"] = isSeller;

            // Count the view once per browser session, and never the seller's own. Sellers
            // reload their listing constantly to see how it looks; counting that would turn
            // the trend line into a graph of the seller's own anxiety.
            var seenKey = "seen:" + car.Id;
            if (!isSeller && HttpContext.Session.GetString(seenKey) is null)
            {
                HttpContext.Session.SetString(seenKey, "1");
                await _views.RecordAsync(car.Id);
            }

            RememberRecentlyViewed(car.Id);

            var badges = await _extras.ForCarsAsync(new[] { car });
            if (car.OwnerId is int ownerId && badges.Sellers.TryGetValue(ownerId, out var badge))
                ViewData["SellerBadge"] = badge;
            if (badges.PreviousPrices.TryGetValue(car.Id, out var wasPrice))
                ViewData["PreviousPrice"] = wasPrice;

            // The one way a signed-out visitor can reach the seller. Deliberately not
            // User.Phone: that is the account's private number and is released only to the
            // other party once an offer is accepted. This is a number the seller chose to
            // publish - either on their seller profile, or as their dealership's trading
            // line, which has been public on the shopfront all along.
            if (car.OwnerId is int contactOwnerId)
            {
                var contact = await (
                    from u in _context.Users.AsNoTracking()
                    where u.Id == contactOwnerId
                    join d in _context.Dealerships.AsNoTracking() on u.Id equals d.OwnerUserId into ds
                    from d in ds.DefaultIfEmpty()
                    select new { u.PublicPhone, DealerPhone = d != null ? d.Phone : null })
                    .FirstOrDefaultAsync();

                var publicPhone = !string.IsNullOrWhiteSpace(contact?.PublicPhone)
                    ? contact!.PublicPhone
                    : contact?.DealerPhone;

                if (!string.IsNullOrWhiteSpace(publicPhone))
                    ViewData["ListingPhone"] = publicPhone.Trim();
            }

            // Three comparable cars, so a listing that is not right is not a dead end. Same
            // body type, within a quarter of the price either way, cheapest deviation first.
            var lower = car.Price * 0.75m;
            var upper = car.Price * 1.25m;
            ViewData["Similar"] = await _context.Cars
                .Where(c => c.Id != car.Id
                            && c.Status == ListingStatus.Active
                            && c.Type == car.Type
                            && c.Price >= lower && c.Price <= upper)
                .OrderBy(c => c.Price > car.Price ? c.Price - car.Price : car.Price - c.Price)
                .Take(3)
                .ToListAsync();

            ViewData["PriceHistory"] = await _context.CarPriceChanges
                .Where(h => h.CarId == car.Id)
                .OrderByDescending(h => h.ChangedUtc)
                .Take(6)
                .ToListAsync();

            if (userId is not null)
            {
                ViewData[$"IsFav_{car.Id}"] = await _context.UserFavoriteCars
                    .AnyAsync(f => f.UserId == userId.Value && f.CarId == id);

                // The buyer's own offer, so the page can show its status instead of
                // inviting them to make the same offer again.
                var mine = await _context.Offers
                    .Where(o => o.CarId == id && o.BuyerId == userId.Value)
                    .OrderByDescending(o => o.CreatedUtc)
                    .FirstOrDefaultAsync();
                ViewData["MyOffer"] = mine;

                var thread = await _context.Conversations
                    .FirstOrDefaultAsync(c => c.CarId == id && c.BuyerId == userId.Value);
                ViewData["MyConversationId"] = thread?.Id;
            }

            // Contact details are released once an offer is accepted — in both directions.
            // The sale completes off-platform, so each side needs to be able to reach the
            // other; releasing only the buyer would leave them unable to arrange collection.
            if (car.SoldToUserId is not null)
            {
                if (isSeller)
                {
                    var buyer = await _context.Users.FirstOrDefaultAsync(u => u.Id == car.SoldToUserId);
                    if (buyer is not null)
                    {
                        ViewData["BuyerEmail"] = buyer.Email;
                        ViewData["BuyerPhone"] = buyer.Phone;
                    }
                }
                else if (userId is not null && car.SoldToUserId == userId && car.OwnerId is not null)
                {
                    var seller = await _context.Users.FirstOrDefaultAsync(u => u.Id == car.OwnerId);
                    if (seller is not null)
                    {
                        ViewData["SellerName"] = seller.SellerName;
                        ViewData["SellerEmail"] = seller.Email;
                        ViewData["SellerPhone"] = seller.Phone;
                    }
                }
            }

            // Seller view: which offers have a thread already, so each row links to the chat.
            if (isSeller)
            {
                var threads = await _context.Conversations
                    .Where(c => c.CarId == id)
                    .ToDictionaryAsync(c => c.BuyerId, c => c.Id);
                ViewData["Threads"] = threads;
            }

            ViewData["RecentlyViewed"] = await RecentlyViewedAsync(car.Id);

            // The similar and recently-viewed strips are made of the same card, so they need
            // the same lookups. Build them from everything the page will actually render.
            var strip = new List<Car>();
            if (ViewData["Similar"] is List<Car> similar) strip.AddRange(similar);
            if (ViewData["RecentlyViewed"] is List<Car> recent) strip.AddRange(recent);
            if (strip.Count > 0)
            {
                await LoadFavouritesAsync(strip.Select(c => c.Id));
                await LoadExtrasAsync(strip);
            }

            return View(car);
        }

        /// <summary>Places a private offer. Replaces the old auction Bid action.</summary>
        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Offer(int id, decimal amount, string? message)
        {
            var buyerId = HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
            var buyerName = HttpContext.Session.GetString(SessionKeys.Username)!;

            var result = await _offers.PlaceAsync(id, buyerId, buyerName, amount, message);

            if (!result.Success)
            {
                if (result.Code == MarketplaceCodes.NotFound) return NotFound();
                TempData["Error"] = result.Error;
                return RedirectToAction(nameof(Details), new { id });
            }

            TempData["Success"] =
                $"Your offer of {amount:C} was sent privately to the seller. You'll see their answer here.";
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>Buyer pulls back an offer the seller has not answered yet.</summary>
        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawOffer(int offerId, int carId)
        {
            var buyerId = HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
            var result = await _offers.WithdrawAsync(offerId, buyerId);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Your offer was withdrawn." : result.Error;

            return RedirectToAction(nameof(Details), new { id = carId });
        }

        /// <summary>
        /// Opens (or continues) the buyer/seller thread for this listing and jumps to it.
        /// Either side can start it: a buyer asking a question before offering, or a seller
        /// wanting detail before answering an offer.
        /// </summary>
        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartChat(int carId, int? buyerId)
        {
            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
            var isAdmin = HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";

            var result = await _conversations.OpenAsync(carId, buyerId ?? userId, userId, isAdmin);

            if (!result.Success || result.Value is null)
            {
                TempData["Error"] = result.Error;
                return RedirectToAction(nameof(Details), new { id = carId });
            }

            return RedirectToAction("Thread", "Messages", new { id = result.Value.Id });
        }

        /// <summary>
        /// The promoted listings on a page of their own.
        ///
        /// A page whose subject is "these sellers paid us" has no reason to exist for a buyer,
        /// and calling it that would make it less appealing rather than more. So the page is
        /// sold on the two things that are actually TRUE of every listing on it, both of them
        /// enforced rather than claimed:
        ///
        ///   1. It is Active. WherePromoted excludes sold and reserved cars, so nothing here
        ///      has already gone - which is the single most annoying thing about browsing a
        ///      used-car site.
        ///   2. The seller paid money to be seen this week, which is the closest thing to
        ///      evidence that they actually want to sell and will answer the phone.
        ///
        /// Neither is a quality claim about the car, and the page must never imply one.
        /// </summary>
        public async Task<IActionResult> Featured()
        {
            var cars = await _context.Cars.AsNoTracking()
                .WherePromoted(PromotionTier.Promoted, DateTime.UtcNow)
                .OrderByPromotion()
                .Take(48)
                .ToListAsync();

            await LoadFavouritesAsync(cars.Select(c => c.Id));
            await LoadExtrasAsync(cars);

            return View(cars);
        }

        /// <summary>
        /// Cars that actually sold here, newest first.
        ///
        /// The strongest trust signal a young marketplace has: proof that transactions
        /// complete. Sale prices are shown because Car.SoldPrice is already public on the
        /// listing itself - hiding them here would be inconsistent rather than private.
        /// </summary>
        public async Task<IActionResult> Sold(int page = 1, int pageSize = 24)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 96 ? 24 : pageSize;

            var query = _context.Cars
                .Where(c => c.Status == ListingStatus.Sold)
                .OrderByDescending(c => c.SoldUtc);

            var total = await query.CountAsync();
            var cars = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            await LoadFavouritesAsync(cars.Select(c => c.Id));
            await LoadExtrasAsync(cars);

            ViewData["Page"] = page;
            ViewData["PageSize"] = pageSize;
            ViewData["Total"] = total;
            return View(cars);
        }

        public async Task<IActionResult> Share(int id)
        {
            var car = await _context.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound();
            ViewData["ListingUrl"] = Url.Action(nameof(Details), "Cars", new { id }, Request.Scheme);
            return View(car);
        }

        // ---------- helpers ----------

        /// <summary>
        /// Ownership check used for draft visibility and the private offer list. Admins count
        /// here (they administer every listing) but deliberately do not count as the owner when
        /// deciding who may place an offer — see OfferService.IsOwner.
        /// </summary>
        private static bool IsSellerOf(Car car, int? userId, bool isAdmin) =>
            isAdmin || (userId is not null && car.OwnerId is not null && car.OwnerId == userId);

        private async Task LoadExtrasAsync(IReadOnlyList<Car> cars)
        {
            if (cars.Count == 0) return;
            var extras = await _extras.ForCarsAsync(cars);
            ViewData["SellerBadges"] = extras.Sellers;
            ViewData["PriceDrops"] = extras.PreviousPrices;
        }

        /// <summary>
        /// Last five listings this visitor opened, kept in the session.
        ///
        /// Session rather than a table on purpose: it needs no schema, works for signed-out
        /// visitors, and disappears on its own. Browsing history is also the kind of personal
        /// data that is much easier not to store than to store and then have to justify.
        /// </summary>
        private void RememberRecentlyViewed(int carId)
        {
            const string key = "recent";
            var ids = (HttpContext.Session.GetString(key) ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var n) ? n : 0)
                .Where(n => n > 0 && n != carId)
                .ToList();

            ids.Insert(0, carId);
            HttpContext.Session.SetString(key, string.Join(',', ids.Take(6)));
        }

        private async Task<List<Car>> RecentlyViewedAsync(int excludeId)
        {
            var ids = (HttpContext.Session.GetString("recent") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var n) ? n : 0)
                .Where(n => n > 0 && n != excludeId)
                .Take(4)
                .ToList();

            if (ids.Count == 0) return new List<Car>();

            var cars = await _context.Cars
                .Where(c => ids.Contains(c.Id) && c.Status != ListingStatus.Draft
                            && c.Status != ListingStatus.Archived)
                .ToListAsync();

            // Restore the session's order: the database returns them by id, which would show
            // the oldest first and make the row look stale.
            return ids.Select(id => cars.FirstOrDefault(c => c.Id == id))
                      .Where(c => c is not null)
                      .Select(c => c!)
                      .ToList();
        }

        private async Task LoadFavouritesAsync(IEnumerable<int> carIds)
        {
            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId);
            if (userId is null) return;

            var ids = carIds.ToList();
            if (ids.Count == 0) return;

            var favourites = await _context.UserFavoriteCars
                .Where(f => f.UserId == userId.Value && ids.Contains(f.CarId))
                .Select(f => f.CarId)
                .ToListAsync();

            foreach (var carId in favourites) ViewData[$"IsFav_{carId}"] = true;
        }

        private static SelectList EnumOptions<TEnum>(string? selected) where TEnum : struct, Enum
        {
            var items = Enum.GetValues<TEnum>()
                .Select(v => new { Value = v.ToString(), Text = DisplayName(v) });
            return new SelectList(items, "Value", "Text", selected);
        }

        /// <summary>Reads the [Display(Name)] off an enum member so "PluginHybrid" reads as "Plug-in hybrid".</summary>
        private static string DisplayName<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            var member = typeof(TEnum).GetMember(value.ToString()!).FirstOrDefault();
            var display = member?.GetCustomAttributes(
                typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                .FirstOrDefault();
            return display?.Name ?? value.ToString()!;
        }

        private static SelectList BuildSortOptions(string? selected)
        {
            var items = new[]
            {
                new { Value = "newest",      Text = "Newest first" },
                new { Value = "price_asc",   Text = "Price: low to high" },
                new { Value = "price_desc",  Text = "Price: high to low" },
                new { Value = "mileage_asc", Text = "Mileage: lowest" },
                new { Value = "year_desc",   Text = "Year: newest" },
                new { Value = "year_asc",    Text = "Year: oldest" },
            };
            return new SelectList(items, "Value", "Text", selected);
        }
    }
}
