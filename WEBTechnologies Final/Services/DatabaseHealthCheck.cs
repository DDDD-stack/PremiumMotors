using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WEBTechnologies_Final.Data;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Confirms the app can actually reach Supabase. Written by hand rather than pulling in
    /// AspNetCore.HealthChecks.NpgSql, since one query is all that is needed.
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _db;

        public DatabaseHealthCheck(AppDbContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var ok = await _db.Database.CanConnectAsync(cancellationToken);
                return ok
                    ? HealthCheckResult.Healthy("Database reachable.")
                    : HealthCheckResult.Unhealthy("Database not reachable.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database check threw.", ex);
            }
        }
    }
}
