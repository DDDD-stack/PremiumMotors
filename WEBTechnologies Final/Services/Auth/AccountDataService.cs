using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

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
        private readonly ILogger<AccountDataService> _logger;

        public AccountDataService(AppDbContext db, ILogger<AccountDataService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<object?> ExportAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return null;

            var listings = await _db.Cars.AsNoTracking()
                .Where(c => c.OwnerId == userId)
                .Select(c => new
                {
                    c.Id, c.Make, c.Model, c.Year, c.Description, c.Price, c.Country, c.City,
                    c.Mileage, c.ServiceHistory, c.FuelType, c.Transmission, c.Status,
                    c.SoldPrice, c.SoldUtc, c.CreatedUtc, c.ImagePaths
                })
                .ToListAsync(ct);

            var offers = await _db.Offers.AsNoTracking()
                .Where(o => o.BuyerId == userId)
                .Select(o => new { o.Id, o.CarId, o.Amount, o.Message, o.Status, o.CreatedUtc, o.RespondedUtc })
                .ToListAsync(ct);

            var favourites = await _db.UserFavoriteCars.AsNoTracking()
                .Where(f => f.UserId == userId)
                .Select(f => new { f.CarId, f.CreatedUtc })
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
                    user.Id, user.Username, user.Email, user.Phone, user.Role,
                    user.RegisteredUtc, user.LastLoginUtc, user.EmailVerifiedUtc
                },
                listings,
                offers,
                favourites,
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

            // Any row still keyed on the old username (created before ids were recorded).
            await _db.Cars.Where(c => c.OwnerId == null && c.OwnerUsername == oldUsername)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerUsername, handle), ct);
            await _db.Offers.Where(o => o.BuyerId == null && o.BuyerUsername == oldUsername)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BuyerUsername, handle), ct);

            // Preferences and credentials carry no retention justification at all.
            await _db.UserFavoriteCars.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
            await _db.RefreshTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
            await _db.UserTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Anonymized account {UserId} on user request.", userId);
            return true;
        }
    }
}
