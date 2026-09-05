using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Offer placement and seller response. Shared by the website and the API so both enforce
    /// exactly the same rules — the website's hidden form is cosmetic, these checks are what
    /// actually hold when a request is crafted by hand.
    ///
    /// Nothing here runs on a timer. The old auction close swept expired listings and picked a
    /// winner by amount; a marketplace seller chooses, and may well take a lower offer from a
    /// buyer who can collect today.
    /// </summary>
    public class OfferService
    {
        private readonly AppDbContext _db;
        private readonly ConversationService _conversations;

        public OfferService(AppDbContext db, ConversationService conversations)
        {
            _db = db;
            _conversations = conversations;
        }

        /// <summary>
        /// Places an offer. An offer does not have to beat the asking price or any other
        /// offer — offers are private, so there is no standing total to beat.
        /// </summary>
        public async Task<MarketplaceResult<Offer>> PlaceAsync(
            int carId, int buyerId, string buyerUsername, decimal amount,
            string? message, CancellationToken ct = default)
        {
            if (amount <= 0)
                return MarketplaceResult<Offer>.Fail(
                    "Your offer must be greater than zero.", MarketplaceCodes.InvalidAmount);

            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null || !car.IsPubliclyVisible)
                return MarketplaceResult<Offer>.Fail("Listing not found.", MarketplaceCodes.NotFound);

            // A seller offering on their own listing could inflate apparent demand and would
            // consume their own listing token.
            if (IsOwner(car, buyerId))
                return MarketplaceResult<Offer>.Fail(
                    "You cannot make an offer on your own listing.", MarketplaceCodes.OwnListing);

            if (!car.AcceptsOffers)
                return MarketplaceResult<Offer>.Fail(
                    car.Status == ListingStatus.Reserved
                        ? "This car is reserved — the seller has accepted another offer."
                        : "This listing is no longer accepting offers.",
                    MarketplaceCodes.NotAcceptingOffers);

            var offer = new Offer
            {
                CarId = carId,
                Amount = amount,
                BuyerId = buyerId,
                BuyerUsername = buyerUsername,
                Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
                Status = OfferStatus.Pending,
                CreatedUtc = DateTime.UtcNow
            };

            _db.Offers.Add(offer);
            await _db.SaveChangesAsync(ct);

            // A note attached to the offer starts the thread, so the seller can reply in place
            // instead of having nowhere to ask a follow-up question.
            if (offer.Message is not null)
            {
                var thread = await _conversations.OpenAsync(carId, buyerId, buyerId, false, ct);
                if (thread.Success && thread.Value is not null)
                {
                    await _conversations.PostAsync(
                        thread.Value.Id, buyerId, buyerUsername, offer.Message, false, ct);
                    offer.ConversationId = thread.Value.Id;
                    await _db.SaveChangesAsync(ct);
                }
            }

            return MarketplaceResult<Offer>.Ok(offer);
        }

        /// <summary>
        /// Seller accepts an offer. The listing becomes Reserved rather than Sold: the money
        /// and the keys still have to change hands off-platform, and the seller confirms that
        /// separately with <see cref="MarkSoldAsync"/>. Every other pending offer is declined
        /// automatically so no buyer is left waiting on a car that is spoken for.
        /// </summary>
        public async Task<MarketplaceResult<Offer>> AcceptAsync(
            int offerId, int actorId, bool actorIsAdmin, string? response, CancellationToken ct = default)
        {
            var offer = await _db.Offers.Include(o => o.Car)
                .FirstOrDefaultAsync(o => o.Id == offerId, ct);

            var guard = GuardSellerAction(offer, actorId, actorIsAdmin);
            if (guard is not null) return guard;

            var car = offer!.Car!;

            offer.Status = OfferStatus.Accepted;
            offer.RespondedUtc = DateTime.UtcNow;
            offer.SellerResponse = string.IsNullOrWhiteSpace(response) ? null : response.Trim();

            car.Status = ListingStatus.Reserved;
            car.SoldTo = offer.BuyerUsername;
            car.SoldToUserId = offer.BuyerId;
            car.SoldPrice = offer.Amount;

            var others = await _db.Offers
                .Where(o => o.CarId == car.Id && o.Id != offer.Id && o.Status == OfferStatus.Pending)
                .ToListAsync(ct);

            foreach (var other in others)
            {
                other.Status = OfferStatus.Declined;
                other.RespondedUtc = DateTime.UtcNow;
                other.SellerResponse = "The seller accepted another offer on this listing.";
            }

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<Offer>.Ok(offer);
        }

        public async Task<MarketplaceResult<Offer>> DeclineAsync(
            int offerId, int actorId, bool actorIsAdmin, string? response, CancellationToken ct = default)
        {
            var offer = await _db.Offers.Include(o => o.Car)
                .FirstOrDefaultAsync(o => o.Id == offerId, ct);

            var guard = GuardSellerAction(offer, actorId, actorIsAdmin);
            if (guard is not null) return guard;

            offer!.Status = OfferStatus.Declined;
            offer.RespondedUtc = DateTime.UtcNow;
            offer.SellerResponse = string.IsNullOrWhiteSpace(response) ? null : response.Trim();

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<Offer>.Ok(offer);
        }

        /// <summary>A buyer may pull a pending offer back until the seller has answered it.</summary>
        public async Task<MarketplaceResult<Offer>> WithdrawAsync(
            int offerId, int buyerId, CancellationToken ct = default)
        {
            var offer = await _db.Offers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
            if (offer is null)
                return MarketplaceResult<Offer>.Fail("Offer not found.", MarketplaceCodes.NotFound);

            if (offer.BuyerId != buyerId)
                return MarketplaceResult<Offer>.Fail("This is not your offer.", MarketplaceCodes.Forbidden);

            if (offer.Status != OfferStatus.Pending)
                return MarketplaceResult<Offer>.Fail(
                    "This offer has already been answered.", MarketplaceCodes.OfferNotPending);

            offer.Status = OfferStatus.Withdrawn;
            offer.RespondedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return MarketplaceResult<Offer>.Ok(offer);
        }

        /// <summary>Confirms the sale actually completed. Only from Reserved.</summary>
        public async Task<MarketplaceResult> MarkSoldAsync(
            int carId, int actorId, bool actorIsAdmin, CancellationToken ct = default)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return MarketplaceResult.Fail("Listing not found.", MarketplaceCodes.NotFound);

            if (!actorIsAdmin && !IsOwner(car, actorId))
                return MarketplaceResult.Fail("This is not your listing.", MarketplaceCodes.Forbidden);

            if (car.Status != ListingStatus.Reserved)
                return MarketplaceResult.Fail(
                    "Accept an offer before marking the car sold.", MarketplaceCodes.NotAcceptingOffers);

            car.Status = ListingStatus.Sold;
            car.SoldUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            // Nothing more to discuss once the car is gone.
            await _db.Conversations.Where(c => c.CarId == carId && !c.IsClosed)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsClosed, true), ct);

            return MarketplaceResult.Ok();
        }

        /// <summary>Undoes an accept, putting the car back on the market.</summary>
        public async Task<MarketplaceResult> ReopenAsync(
            int carId, int actorId, bool actorIsAdmin, CancellationToken ct = default)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return MarketplaceResult.Fail("Listing not found.", MarketplaceCodes.NotFound);

            if (!actorIsAdmin && !IsOwner(car, actorId))
                return MarketplaceResult.Fail("This is not your listing.", MarketplaceCodes.Forbidden);

            car.Status = ListingStatus.Active;
            car.SoldTo = null;
            car.SoldToUserId = null;
            car.SoldPrice = null;
            car.SoldUtc = null;
            await _db.SaveChangesAsync(ct);

            await _db.Offers.Where(o => o.CarId == carId && o.Status == OfferStatus.Accepted)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OfferStatus.Declined), ct);

            return MarketplaceResult.Ok();
        }

        // ---------- helpers ----------

        private MarketplaceResult<Offer>? GuardSellerAction(Offer? offer, int actorId, bool actorIsAdmin)
        {
            if (offer?.Car is null)
                return MarketplaceResult<Offer>.Fail("Offer not found.", MarketplaceCodes.NotFound);

            if (!actorIsAdmin && !IsOwner(offer.Car, actorId))
                return MarketplaceResult<Offer>.Fail(
                    "Only the seller of this listing can answer its offers.", MarketplaceCodes.Forbidden);

            if (offer.Status != OfferStatus.Pending)
                return MarketplaceResult<Offer>.Fail(
                    "This offer has already been answered.", MarketplaceCodes.OfferNotPending);

            return null;
        }

        // Ownership only, deliberately NOT counting admins: an administrator is not the seller
        // of a user's listing, so they must stay able to make an offer on one.
        private static bool IsOwner(Car car, int userId) => car.OwnerId is not null && car.OwnerId == userId;

    }
}
