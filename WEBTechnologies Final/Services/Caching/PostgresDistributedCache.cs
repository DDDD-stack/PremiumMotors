using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Caching
{
    /// <summary>
    /// An IDistributedCache backed by the application's own Postgres database, used to hold
    /// website sessions.
    ///
    /// It is registered as a SINGLETON because that is what IDistributedCache must be, while
    /// AppDbContext is scoped - so every operation opens its own short scope rather than
    /// capturing a context. Capturing one would give the whole application a single shared
    /// DbContext, which is not thread-safe and would fail under any real concurrency.
    ///
    /// Failures are swallowed on read and logged, never thrown. A cache that throws takes the
    /// site down; a cache that misses signs one person out. Writes are not swallowed: silently
    /// failing to persist a session would log people out with no trace of why.
    /// </summary>
    public class PostgresDistributedCache : IDistributedCache
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PostgresDistributedCache> _logger;

        public PostgresDistributedCache(
            IServiceScopeFactory scopes, ILogger<PostgresDistributedCache> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        public byte[]? Get(string key) => GetAsync(key).GetAwaiter().GetResult();

        public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var entry = await db.SessionCache.FirstOrDefaultAsync(e => e.Id == key, ct);
                if (entry is null) return null;

                var now = DateTime.UtcNow;
                if (entry.ExpiresAtUtc <= now)
                {
                    db.SessionCache.Remove(entry);
                    await db.SaveChangesAsync(ct);
                    return null;
                }

                // Sliding renewal on read is what makes "you were logged out while typing"
                // not happen. The absolute expiry still caps it.
                if (entry.SlidingSeconds is double sliding)
                {
                    var renewed = now.AddSeconds(sliding);
                    if (entry.AbsoluteExpirationUtc is DateTime hard && renewed > hard)
                        renewed = hard;

                    if (renewed > entry.ExpiresAtUtc)
                    {
                        entry.ExpiresAtUtc = renewed;
                        await db.SaveChangesAsync(ct);
                    }
                }

                return entry.Value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session cache read failed for key {Key}.", key);
                return null;
            }
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            SetAsync(key, value, options).GetAwaiter().GetResult();

        public async Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;

            DateTime? absolute = options.AbsoluteExpiration?.UtcDateTime
                ?? (options.AbsoluteExpirationRelativeToNow is TimeSpan rel
                    ? now.Add(rel)
                    : null);

            double? sliding = options.SlidingExpiration?.TotalSeconds;

            var expires = sliding is double s ? now.AddSeconds(s) : absolute ?? now.AddHours(1);
            if (absolute is DateTime cap && expires > cap) expires = cap;

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var entry = await db.SessionCache.FirstOrDefaultAsync(e => e.Id == key, ct);
            if (entry is null)
            {
                db.SessionCache.Add(new SessionCacheEntry
                {
                    Id = key,
                    Value = value,
                    ExpiresAtUtc = expires,
                    SlidingSeconds = sliding,
                    AbsoluteExpirationUtc = absolute
                });
            }
            else
            {
                entry.Value = value;
                entry.ExpiresAtUtc = expires;
                entry.SlidingSeconds = sliding;
                entry.AbsoluteExpirationUtc = absolute;
            }

            await db.SaveChangesAsync(ct);
        }

        public void Refresh(string key) => RefreshAsync(key).GetAwaiter().GetResult();

        /// <summary>Reading already renews the sliding window, so this is the same operation.</summary>
        public Task RefreshAsync(string key, CancellationToken ct = default) => GetAsync(key, ct);

        public void Remove(string key) => RemoveAsync(key).GetAwaiter().GetResult();

        public async Task RemoveAsync(string key, CancellationToken ct = default)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.SessionCache.Where(e => e.Id == key).ExecuteDeleteAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Session cache delete failed for key {Key}.", key);
            }
        }
    }
}
