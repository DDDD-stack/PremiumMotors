using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;

namespace WEBTechnologies_Final.Services.Caching
{
    /// <summary>
    /// Deletes expired session rows.
    ///
    /// Nothing else does. Reads clear the individual row they happen to land on, but a session
    /// that is never read again is never touched again, so without this the table grows for
    /// the life of the deployment - slowly, invisibly, and forever.
    ///
    /// Hourly is deliberate. The rows are already ignored once expired, so sweeping is
    /// housekeeping rather than correctness, and a tighter loop would be pure database load.
    /// </summary>
    public class ExpiredSessionSweeper : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<ExpiredSessionSweeper> _logger;

        public ExpiredSessionSweeper(
            IServiceScopeFactory scopes, ILogger<ExpiredSessionSweeper> logger)
        {
            _scopes = scopes;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // A short delay first: startup is already doing migrations and seeding, and this
            // is the least urgent thing the application will ever do.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var removed = await db.SessionCache
                        .Where(e => e.ExpiresAtUtc <= DateTime.UtcNow)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (removed > 0)
                        _logger.LogInformation("Swept {Count} expired session(s).", removed);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never let housekeeping stop the host. A failed sweep costs some rows.
                    _logger.LogError(ex, "Session sweep failed; will retry next interval.");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
