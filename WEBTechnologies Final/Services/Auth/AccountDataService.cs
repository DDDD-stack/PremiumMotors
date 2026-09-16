using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Services.Auth
{
    /// <summary>
    /// GDPR obligations: the right to erasure and the right to data portability.
    ///
    /// Erasure ANONYMIZES rather than hard-deletes. A completed auction is a transaction record
    /// that the counterparty has a legitimate interest in keeping, and hard-deleting the user
    /// row would only null the id columns while leaving the username copies on listings, bids
    /// and payments - which are themselves personal data. Anonymizing scrubs every copy.
    /// </summary>
    public class AccountDataService
    {
        private readonly AppDbContext _db;
        private readonly IPhotoStorage _photos;
        private readonly ILogger<AccountDataService> _logger;

        public AccountDataService(
            AppDbContext db, IPhotoStorage photos, ILogger<AccountDataService> logger)
        {
            _db = db;
            _photos = photos;
            _logger = logger;
        }

        /// <summary>
        /// Everything held about one account, as a single JSON document - the right of access
        /// and the right to portability.
        ///
        /// COMPLETENESS IS THE POINT. This used to return account, listings, offers, favourites
        /// and sessions, and silently left out messages, reviews, the seller and business
        /// profile, the dealership page and the terms-acceptance record. An export that omits
        /// categories is not a partial answer to an access request, it is a wrong one, and the
        /// Privacy Policy lists every category below. If a new table holds personal data, it
        /// belongs here in the same commit.
        ///
        /// Other people's personal data is deliberately NOT included beyond what this user can
        /// already see in the product: the other party's username on an offer or a message is
        /// shown to them on the site; the other party's email and phone are not repeated here.
        /// </summary>
        public async Task<object?> ExportAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return null;

            var listings = await _db.Cars.AsNoTracking()
                .Where(c => c.OwnerId == userId)
                .Select(c => new
                {
                    c.Id, c.Make, c.Model, c.Year, c.Type, c.Condition, c.Description, c.Price,
                    c.Country, c.City, c.Mileage, c.FuelType, c.Transmission, c.Drivetrain,
                    c.EngineSizeCc, c.PowerHp, c.Doors, c.Seats, c.PreviousOwners,
                    c.ServiceHistory, c.ServiceHistoryNotes, c.FirstRegistration, c.Vin,
                    c.HasAccidentHistory, c.ExteriorColour, c.Status, c.CreatedUtc,
                    c.PublishedUtc, c.SoldPrice, c.SoldUtc, c.SoldTo, c.ImagePaths
                })
                .ToListAsync(ct);

            var listingIds = listings.Select(l => l.Id).ToList();

            var priceHistory = await _db.CarPriceChanges.AsNoTracking()
                .Where(p => listingIds.Contains(p.CarId))
                .OrderBy(p => p.ChangedUtc)
                .Select(p => new { p.CarId, p.PreviousPrice, p.Price, p.ChangedUtc })
                .ToListAsync(ct);

            var offersMade = await _db.Offers.AsNoTracking()
                .Where(o => o.BuyerId == userId)
                .Select(o => new
                {
                    o.Id, o.CarId, o.Amount, o.Message, o.Status, o.SellerResponse,
                    o.CreatedUtc, o.RespondedUtc
                })
                .ToListAsync(ct);

            // Offers other people made on this user's listings. The seller already sees every
            // one of these, username and amount included, in their offer inbox.
            var offersReceived = await _db.Offers.AsNoTracking()
                .Where(o => listingIds.Contains(o.CarId))
                .Select(o => new
                {
                    o.Id, o.CarId, o.BuyerUsername, o.Amount, o.Message, o.Status,
                    o.SellerResponse, o.CreatedUtc, o.RespondedUtc
                })
                .ToListAsync(ct);

            var conversations = await _db.Conversations.AsNoTracking()
                .Where(c => c.BuyerId == userId || c.SellerId == userId)
                .Select(c => new
                {
                    c.Id, c.CarId, c.CreatedUtc, c.LastMessageUtc, c.IsClosed,
                    Role = c.BuyerId == userId ? "buyer" : "seller",
                    Messages = c.Messages
                        .OrderBy(m => m.SentUtc)
                        .Select(m => new
                        {
                            From = m.SenderId == userId ? "you" : m.SenderUsername,
                            m.Body, m.SentUtc, m.ReadUtc
                        })
                        .ToList()
                })
                .ToListAsync(ct);

            var reviewsWritten = await _db.SellerReviews.AsNoTracking()
                .Where(r => r.AuthorUserId == userId)
                .Select(r => new
                {
                    r.Id, r.CarId, r.Rating, r.Comment, r.CreatedUtc, r.SellerReply, r.SellerRepliedUtc
                })
                .ToListAsync(ct);

            var reviewsReceived = await _db.SellerReviews.AsNoTracking()
                .Where(r => r.SellerUserId == userId)
                .Select(r => new
                {
                    r.Id, r.CarId, r.AuthorUsername, r.Rating, r.Comment, r.CreatedUtc,
                    r.SellerReply, r.SellerRepliedUtc
                })
                .ToListAsync(ct);

            var favourites = await _db.UserFavoriteCars.AsNoTracking()
                .Where(f => f.UserId == userId)
                .Select(f => new { f.CarId, f.CreatedUtc })
                .ToListAsync(ct);

            var dealership = await _db.Dealerships.AsNoTracking()
                .Where(d => d.OwnerUserId == userId)
                .Select(d => new
                {
                    d.Slug, d.Name, d.About, d.City, d.Country, d.Address, d.Phone,
                    d.Website, d.OpeningHours, d.LogoPath, d.BannerPath, d.CreatedUtc
                })
                .FirstOrDefaultAsync(ct);

            var promotions = await _db.Promotions.AsNoTracking()
                .Where(p => p.SellerUserId == userId)
                .Select(p => new
                {
                    p.Reference, p.CarId, p.CarTitle, p.Tier, p.StartedUtc, p.EndsUtc,
                    p.EndedEarlyUtc, p.EndedReason, p.PriceEur
                })
                .ToListAsync(ct);

            var payments = await _db.Payments.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new
                {
                    p.Id, p.AmountCents, p.Currency, p.Status, p.Provider, p.CreatedUtc, p.PaidUtc
                })
                .ToListAsync(ct);

            var sessions = await _db.RefreshTokens.AsNoTracking()
                .Where(t => t.UserId == userId)
                .Select(t => new { t.Device, t.CreatedUtc, t.ExpiresUtc, t.RevokedUtc })
                .ToListAsync(ct);

            return new
            {
                exportedUtc = DateTime.UtcNow,
                account = new
                {
                    user.Id, user.Username, user.Email, user.Phone, user.Role, user.IsActive,
                    user.RegisteredUtc, user.LastLoginUtc, user.EmailVerifiedUtc, user.AvatarPath
                },
                legal = new { user.TermsAcceptedUtc, user.TermsVersion },
                sellerProfile = user.IsSeller
                    ? new
                    {
                        user.SellerSinceUtc, user.SellerType, user.SellerDisplayName,
                        user.SellerLocation, user.PublicPhone, user.RatingAverage, user.RatingCount
                    }
                    : null,
                business = user.IsBusiness
                    ? new
                    {
                        user.BusinessRegistrationNumber, user.VatNumber, user.BusinessAddress,
                        user.Website, user.ContactName
                    }
                    : null,
                dealership,
                listings,
                priceHistory,
                offersMade,
                offersReceived,
                conversations,
                reviewsWritten,
                reviewsReceived,
                favourites,
                promotions,
                payments,
                sessions
            };
        }

        /// <summary>
        /// Erases personal data while leaving auction history intact and attributable to an
        /// anonymous handle. Irreversible.
        /// </summary>
        public async Task<bool> AnonymizeAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return false;

            var oldUsername = user.Username;
            var handle = $"deleted_user_{user.Id}";

            user.Username = handle;
            user.Email = $"{handle}@deleted.invalid";
            user.Phone = string.Empty;
            // The opt-in number shown on the seller's own listings to signed-out visitors.
            // Missing this meant an erased account kept publishing its owner's phone number
            // on every listing they had ever posted - the one piece of personal data on this
            // site that is deliberately public, and therefore the one that matters most.
            user.PublicPhone = null;
            user.AnonymizedUtc = DateTime.UtcNow;
            user.EmailVerifiedUtc = null;
            user.IsActive = false;
            // Unusable random hash: no password can ever match, and no reset can be requested
            // because the address no longer belongs to anyone.
            user.PasswordHash = PasswordHasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

            // Scrub the denormalized username copies, which are personal data in their own right.
            await _db.Cars.Where(c => c.OwnerId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerUsername, handle), ct);
            await _db.Cars.Where(c => c.SoldToUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SoldTo, handle), ct);
            await _db.Offers.Where(o => o.BuyerId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BuyerUsername, handle), ct);
            await _db.Payments.Where(p => p.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Username, handle), ct);

            // Messages and reviews carry their own username copy too, and both are shown on the
            // site: a review is public, and a message is read by the other party. These two were
            // missed, which left an erased account's old username published on every review it
            // had ever written - while the Privacy Policy said personal details were cleared.
            await _db.Messages.Where(m => m.SenderId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.SenderUsername, handle), ct);
            await _db.SellerReviews.Where(r => r.AuthorUserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.AuthorUsername, handle), ct);

            // Any row still keyed on the old username (created before ids were recorded).
            await _db.Cars.Where(c => c.OwnerId == null && c.OwnerUsername == oldUsername)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerUsername, handle), ct);
            await _db.Offers.Where(o => o.BuyerId == null && o.BuyerUsername == oldUsername)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BuyerUsername, handle), ct);

            // Every image this account ever uploaded, collected BEFORE the paths are cleared -
            // once they are gone nothing records where the files were, and they would sit in
            // storage forever with no way to find them again.
            //
            // Photographs of a person's car, taken outside their house, are personal data as
            // surely as their phone number is. The listings themselves survive: a sale that
            // happened is still a real sale and still belongs in the sold history. It just no
            // longer carries their pictures.
            var files = new List<string>();

            var shopfront = await _db.Dealerships
                .Where(d => d.OwnerUserId == userId)
                .Select(d => new { d.LogoPath, d.BannerPath })
                .ToListAsync(ct);

            files.AddRange(shopfront.SelectMany(d => new[] { d.LogoPath, d.BannerPath })
                .Where(path => !string.IsNullOrWhiteSpace(path))!);

            var listingPhotos = await _db.Cars
                .Where(c => c.OwnerId == userId)
                .Select(c => c.ImagePaths)
                .ToListAsync(ct);

            files.AddRange(listingPhotos.SelectMany(p => p).Where(p => !string.IsNullOrWhiteSpace(p)));

            await _db.Cars.Where(c => c.OwnerId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ImagePaths, new List<string>()), ct);

            // A dealership is a public shopfront carrying a name, an address, a phone number
            // and a website. For a sole trader all four are personal data, and none of them
            // survive an erasure request. The row itself is kept rather than deleted so that
            // listings pointing at it keep rendering; what made it identifiable does not.
            await _db.Dealerships.Where(d => d.OwnerUserId == userId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.Name, handle)
                    .SetProperty(d => d.About, (string?)null)
                    .SetProperty(d => d.Address, (string?)null)
                    .SetProperty(d => d.Phone, (string?)null)
                    .SetProperty(d => d.Website, (string?)null)
                    .SetProperty(d => d.OpeningHours, (string?)null)
                    .SetProperty(d => d.LogoPath, (string?)null)
                    .SetProperty(d => d.BannerPath, (string?)null), ct);

            // Preferences and credentials carry no retention justification at all.
            await _db.UserFavoriteCars.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
            await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            await _db.UserTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

            await _db.SaveChangesAsync(ct);

            // Files last, and only after the database commits. The other order would delete
            // somebody's photos and then fail to save, leaving listings pointing at images
            // that no longer exist. Deletion is best-effort by design - an orphaned blob is a
            // cleanup problem, a half-erased account is not.
            await _photos.DeleteAllAsync(files, ct);

            _logger.LogInformation(
                "Anonymized account {UserId} on user request; {FileCount} files removed.",
                userId, files.Count);
            return true;
        }
    }
}
