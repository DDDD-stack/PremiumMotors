using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Seller ratings, for private sellers and dealerships alike.
    ///
    /// One mechanism covers both because a review targets the seller's USER account: a private
    /// seller's reputation is their own reviews, and a dealership's is the reviews of the
    /// account that owns it. No second table, no second set of rules to keep in step.
    ///
    /// ELIGIBILITY IS WHAT MAKES THE RATINGS WORTH ANYTHING. Only the recorded buyer of a
    /// listing that actually sold may review its seller, once per listing. Open reviews would
    /// be worthless inside a week: anyone could rate anyone, and a rival could bury a dealer
    /// for free. The sale is already on Car (SoldToUserId, SoldUtc), so eligibility is
    /// something the database can prove rather than something a moderator has to judge.
    /// </summary>
    public class ReviewService
    {
        private readonly AppDbContext _db;

        public ReviewService(AppDbContext db) => _db = db;

        /// <summary>Listings this user bought and has not yet reviewed.</summary>
        public Task<List<Car>> AwaitingReviewAsync(int buyerId, CancellationToken ct = default) =>
            _db.Cars.AsNoTracking()
                .Where(c => c.SoldToUserId == buyerId
                            && c.Status == ListingStatus.Sold
                            && c.OwnerId != null
                            && !_db.SellerReviews.Any(r => r.CarId == c.Id))
                .OrderByDescending(c => c.SoldUtc)
                .ToListAsync(ct);

        public Task<List<SellerReview>> ForSellerAsync(
            int sellerUserId, int take = 50, CancellationToken ct = default) =>
            _db.SellerReviews.AsNoTracking()
                .Include(r => r.Car)
                .Where(r => r.SellerUserId == sellerUserId)
                .OrderByDescending(r => r.CreatedUtc)
                .Take(take)
                .ToListAsync(ct);

        /// <summary>
        /// The 5/4/3/2/1 breakdown. A bare average hides the shape: 20 fives and 5 ones reads
        /// very differently from 25 fours, and buyers care about the difference.
        /// </summary>
        public async Task<int[]> DistributionAsync(int sellerUserId, CancellationToken ct = default)
        {
            var rows = await _db.SellerReviews.AsNoTracking()
                .Where(r => r.SellerUserId == sellerUserId)
                .GroupBy(r => r.Rating)
                .Select(g => new { Stars = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var buckets = new int[5];
            foreach (var row in rows)
                if (row.Stars is >= 1 and <= 5) buckets[row.Stars - 1] = row.Count;
            return buckets;
        }

        public async Task<MarketplaceResult<SellerReview>> LeaveAsync(
            int carId, int authorUserId, int rating, string? comment,
            CancellationToken ct = default)
        {
            if (rating is < 1 or > 5)
                return MarketplaceResult<SellerReview>.Fail(
                    "Choose a rating from 1 to 5 stars.", MarketplaceCodes.InvalidAmount);

            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null)
                return MarketplaceResult<SellerReview>.Fail(
                    "Listing not found.", MarketplaceCodes.NotFound);

            // Both halves of the eligibility rule, checked against the sale record.
            if (car.Status != ListingStatus.Sold || car.SoldToUserId != authorUserId)
                return MarketplaceResult<SellerReview>.Fail(
                    "You can only review a seller you actually bought a car from.",
                    MarketplaceCodes.Forbidden);

            if (car.OwnerId is null)
                return MarketplaceResult<SellerReview>.Fail(
                    "That listing has no seller to review.", MarketplaceCodes.NotFound);

            if (car.OwnerId == authorUserId)
                return MarketplaceResult<SellerReview>.Fail(
                    "You cannot review yourself.", MarketplaceCodes.OwnListing);

            if (await _db.SellerReviews.AnyAsync(r => r.CarId == carId, ct))
                return MarketplaceResult<SellerReview>.Fail(
                    "You have already reviewed this purchase.", ReviewCodes.AlreadyReviewed);

            var author = await _db.Users.FirstOrDefaultAsync(u => u.Id == authorUserId, ct);

            var review = new SellerReview
            {
                SellerUserId = car.OwnerId.Value,
                AuthorUserId = authorUserId,
                AuthorUsername = author?.Username ?? "Buyer",
                CarId = carId,
                Rating = rating,
                Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
                CreatedUtc = DateTime.UtcNow
            };

            _db.SellerReviews.Add(review);
            await _db.SaveChangesAsync(ct);

            await RecomputeAsync(car.OwnerId.Value, ct);
            return MarketplaceResult<SellerReview>.Ok(review);
        }

        /// <summary>A seller may answer a review once. Answering is not editing what was said.</summary>
        public async Task<MarketplaceResult> ReplyAsync(
            int reviewId, int sellerUserId, string reply, CancellationToken ct = default)
        {
            var review = await _db.SellerReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
            if (review is null)
                return MarketplaceResult.Fail("Review not found.", MarketplaceCodes.NotFound);

            if (review.SellerUserId != sellerUserId)
                return MarketplaceResult.Fail(
                    "That review is not on your account.", MarketplaceCodes.Forbidden);

            if (string.IsNullOrWhiteSpace(reply))
                return MarketplaceResult.Fail("Write a reply first.", MarketplaceCodes.EmptyMessage);

            review.SellerReply = reply.Trim();
            review.SellerRepliedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return MarketplaceResult.Ok();
        }

        /// <summary>
        /// Rewrites User.RatingCount and User.RatingAverage from the review table.
        ///
        /// RECOMPUTED, never incremented. An incremental counter drifts the first time a write
        /// is rolled back, a review is deleted by a cascade, or two reviews land at once - and
        /// a reputation number that quietly disagrees with the reviews under it is worse than
        /// no number at all. This is one cheap aggregate on an indexed column.
        /// </summary>
        public async Task RecomputeAsync(int sellerUserId, CancellationToken ct = default)
        {
            var stats = await _db.SellerReviews
                .Where(r => r.SellerUserId == sellerUserId)
                .GroupBy(r => 1)
                .Select(g => new { Count = g.Count(), Sum = g.Sum(r => r.Rating) })
                .FirstOrDefaultAsync(ct);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == sellerUserId, ct);
            if (user is null) return;

            user.RatingCount = stats?.Count ?? 0;
            user.RatingAverage = user.RatingCount == 0
                ? 0m
                : Math.Round((decimal)stats!.Sum / user.RatingCount, 2, MidpointRounding.AwayFromZero);

            await _db.SaveChangesAsync(ct);
        }
    }

    public static class ReviewCodes
    {
        public const string AlreadyReviewed = "already_reviewed";
    }
}
