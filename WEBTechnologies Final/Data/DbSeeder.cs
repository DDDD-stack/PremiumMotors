using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Data
{
    /// <summary>
    /// Ensures an administrator account exists as a real database row.
    ///
    /// Admin used to be a hardcoded username/password pair compared inside AccountController,
    /// which meant the admin had no user id and so could not be issued a token or own anything.
    /// </summary>
    public static class DbSeeder
    {
        public static async Task SeedAsync(
            AppDbContext db, IConfiguration config, IWebHostEnvironment env, ILogger logger,
            CancellationToken ct = default)
        {
            if (await db.Users.AnyAsync(u => u.Role == Roles.Admin, ct)) return;

            var username = config["AdminSeed:Username"];
            if (string.IsNullOrWhiteSpace(username)) username = "admin";

            var email = config["AdminSeed:Email"];
            if (string.IsNullOrWhiteSpace(email)) email = "admin@premiummotors.local";

            var password = config["AdminSeed:Password"];

            if (string.IsNullOrWhiteSpace(password))
            {
                if (env.IsDevelopment())
                {
                    // Matches the credentials the site used before admin became a real account.
                    password = "admin123";
                    logger.LogWarning(
                        "Seeded the development admin account {Username} with the default password. " +
                        "Set AdminSeed:Password in user-secrets to change it.", username);
                }
                else
                {
                    // Never silently create a well-known admin password outside development.
                    password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                    logger.LogWarning(
                        "No AdminSeed:Password configured. Seeded admin {Username} with a generated " +
                        "password. Sign in and change it now: {Password}", username, password);
                }
            }

            // An existing non-admin row with this username would collide with the unique index.
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower(), ct);
            if (existing is not null)
            {
                existing.Role = Roles.Admin;
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Promoted existing user {Username} to administrator.", username);
                return;
            }

            db.Users.Add(new User
            {
                Username = username,
                Email = email,
                Phone = string.Empty,
                PasswordHash = PasswordHasher.Hash(password),
                Role = Roles.Admin,
                IsActive = true,
                RegisteredUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded administrator account {Username}.", username);
        }
    }
}
