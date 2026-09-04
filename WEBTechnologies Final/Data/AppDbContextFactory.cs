using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace WEBTechnologies_Final.Data
{
    /// <summary>
    /// Lets "dotnet ef migrations add/script" work without booting the web host.
    ///
    /// Program.cs deliberately throws when the Supabase connection string or the JWT signing key
    /// is missing, which is right for running the app but would block scaffolding a migration.
    /// Building a migration only needs the Npgsql provider, not a reachable database, so this
    /// falls back to a placeholder when nothing is configured.
    /// </summary>
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        private const string DesignTimePlaceholder =
            "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        public AppDbContext CreateDbContext(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddUserSecrets<AppDbContextFactory>(optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = config.GetConnectionString("DefaultConnection");
            connectionString = string.IsNullOrWhiteSpace(connectionString)
                ? DesignTimePlaceholder
                // Same normalization the app uses, so "dotnet ef database update" accepts the
                // postgres:// URI form and gets the pooler-safe settings too.
                : SupabaseConnection.Build(connectionString, "ConnectionStrings:DefaultConnection");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new AppDbContext(options);
        }
    }
}
