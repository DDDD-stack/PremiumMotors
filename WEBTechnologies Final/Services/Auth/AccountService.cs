using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;

namespace WEBTechnologies_Final.Services.Auth
{
    public record AccountResult(User? User, string? Error)
    {
        public bool Succeeded => User is not null;
        public static AccountResult Ok(User user) => new(user, null);
        public static AccountResult Fail(string error) => new(null, error);
    }

    /// <summary>
    /// The single source of truth for creating and authenticating accounts.
    ///
    /// Before this existed the MVC form (via ApiClient) wrote plaintext passwords while the
    /// API controller wrote SHA-256 hashes, so an account made on one surface could never sign
    /// in on the other. Both surfaces now call this, which is what makes an account genuinely
    /// portable between the website and the mobile app.
    /// </summary>
    public class AccountService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<AccountService> _logger;

        public AccountService(AppDbContext db, ILogger<AccountService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<AccountResult> RegisterAsync(
            string username, string email, string phone, string password, CancellationToken ct = default)
        {
            username = (username ?? string.Empty).Trim();
            email = (email ?? string.Empty).Trim();
            phone = (phone ?? string.Empty).Trim();

            if (username.Length < 3 || username.Length > 30)
                return AccountResult.Fail("Username must be 3-30 characters.");
            if (string.IsNullOrWhiteSpace(email))
                return AccountResult.Fail("An email address is required.");
            if ((password ?? string.Empty).Length < 6)
                return AccountResult.Fail("Password must be at least 6 characters.");

            if (await _db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct))
                return AccountResult.Fail("That username is already taken.");

            if (await _db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct))
                return AccountResult.Fail("An account with that email already exists.");

            var user = new User
            {
                Username = username,
                Email = email,
                Phone = phone,
                PasswordHash = PasswordHasher.Hash(password!),
                Role = Roles.User,
                IsActive = true,
                RegisteredUtc = DateTime.UtcNow
            };

            _db.Users.Add(user);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Unique index on Username/Email caught a concurrent signup for the same name.
                return AccountResult.Fail("That username or email was just taken. Please try another.");
            }

            return AccountResult.Ok(user);
        }

        /// <summary>
        /// Creates a dealer account in one step: credentials, contact details AND the business
        /// record, already flagged as a seller.
        ///
        /// A business never passes through the "start selling" opt-in, because everything that
        /// form would ask has been answered here. That is the whole point of splitting signup:
        /// asking "are you a dealer?" later is asking a question the signup route already knew.
        /// </summary>
        public async Task<AccountResult> RegisterBusinessAsync(
            RegisterBusinessViewModel form, CancellationToken ct = default)
        {
            var result = await RegisterAsync(
                form.Username, form.Email, form.Phone, form.Password, ct);

            if (!result.Succeeded) return result;

            var user = result.User!;

            user.IsSeller = true;
            user.SellerSinceUtc = DateTime.UtcNow;
            user.SellerType = SellerType.Dealer;
            user.SellerDisplayName = form.BusinessName.Trim();
            user.SellerLocation = string.Join(", ",
                new[] { form.City?.Trim(), form.Country?.Trim() }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            user.BusinessRegistrationNumber = form.RegistrationNumber.Trim();
            user.VatNumber = Blank(form.VatNumber);
            user.BusinessAddress = form.Address.Trim();
            user.Website = Blank(form.Website);
            user.ContactName = form.ContactName.Trim();

            // SellerVerified stays false: no document check exists yet. See docs/SELLER-PANEL.md.

            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }

        /// <summary>Updates the business record on an existing dealer account.</summary>
        public async Task<AccountResult> UpdateBusinessAsync(
            int userId, BusinessDetailsViewModel form, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return AccountResult.Fail("Account not found.");

            user.SellerDisplayName = form.BusinessName.Trim();
            user.SellerLocation = Blank(form.Location);
            user.BusinessRegistrationNumber = form.RegistrationNumber.Trim();
            user.VatNumber = Blank(form.VatNumber);
            user.BusinessAddress = form.Address.Trim();
            user.Website = Blank(form.Website);
            user.ContactName = form.ContactName.Trim();

            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>
        /// Validates credentials against either the username or the email address. On success
        /// with a legacy (plaintext / bare SHA-256) stored password, the hash is transparently
        /// upgraded to PBKDF2 before returning.
        /// </summary>
        public async Task<AccountResult> ValidateAsync(
            string usernameOrEmail, string password, CancellationToken ct = default)
        {
            var identifier = (usernameOrEmail ?? string.Empty).Trim().ToLower();
            if (identifier.Length == 0 || string.IsNullOrEmpty(password))
                return AccountResult.Fail("Invalid username or password.");

            var user = await _db.Users.FirstOrDefaultAsync(
                u => u.Username.ToLower() == identifier || u.Email.ToLower() == identifier, ct);

            // Always the same message, so the endpoint cannot be used to enumerate accounts.
            const string Invalid = "Invalid username or password.";
            if (user is null) return AccountResult.Fail(Invalid);

            var result = PasswordHasher.Verify(password, user.PasswordHash);
            if (result == PasswordVerificationResult.Failed) return AccountResult.Fail(Invalid);

            if (!user.IsActive)
                return AccountResult.Fail("This account has been disabled.");

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = PasswordHasher.Hash(password);
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation("Upgraded legacy password hash for user {UserId}.", user.Id);
            }

            return AccountResult.Ok(user);
        }

        public async Task<AccountResult> ChangePasswordAsync(
            int userId, string currentPassword, string newPassword, CancellationToken ct = default)
        {
            if ((newPassword ?? string.Empty).Length < 6)
                return AccountResult.Fail("New password must be at least 6 characters.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return AccountResult.Fail("Account not found.");

            if (PasswordHasher.Verify(currentPassword, user.PasswordHash) == PasswordVerificationResult.Failed)
                return AccountResult.Fail("Your current password is incorrect.");

            user.PasswordHash = PasswordHasher.Hash(newPassword!);
            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }

        /// <summary>Sets a password without the current one. Only for a redeemed reset token.</summary>
        public async Task<AccountResult> SetPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
        {
            if ((newPassword ?? string.Empty).Length < 6)
                return AccountResult.Fail("Password must be at least 6 characters.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return AccountResult.Fail("Account not found.");

            user.PasswordHash = PasswordHasher.Hash(newPassword!);
            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }

        public async Task<AccountResult> MarkEmailVerifiedAsync(int userId, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return AccountResult.Fail("Account not found.");

            user.EmailVerifiedUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }

        public async Task<AccountResult> UpdateProfileAsync(
            int userId, string? email, string? phone, CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return AccountResult.Fail("Account not found.");

            if (!string.IsNullOrWhiteSpace(email))
            {
                var normalized = email.Trim();
                var taken = await _db.Users.AnyAsync(
                    u => u.Id != userId && u.Email.ToLower() == normalized.ToLower(), ct);
                if (taken) return AccountResult.Fail("An account with that email already exists.");
                user.Email = normalized;
            }

            if (phone is not null) user.Phone = phone.Trim();

            await _db.SaveChangesAsync(ct);
            return AccountResult.Ok(user);
        }
    }
}
