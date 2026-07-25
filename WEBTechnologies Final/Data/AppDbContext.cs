using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Models;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WEBTechnologies_Final.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Car> Cars => Set<Car>();
        public DbSet<Bid> Bids => Set<Bid>();
        public DbSet<UserFavoriteCar> UserFavoriteCars => Set<UserFavoriteCar>();
        public DbSet<Payment> Payments => Set<Payment>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(e =>
            {
                e.HasIndex(u => u.Username).IsUnique();
                e.HasIndex(u => u.Email).IsUnique();
            });

            modelBuilder.Entity<Car>(e =>
            {
                e.Property(c => c.StartingPrice).HasPrecision(18, 2);
                e.Property(c => c.ImagePaths)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>()
                    );
            });

            modelBuilder.Entity<Bid>(e =>
            {
                e.Property(b => b.Amount).HasPrecision(18, 2);
                e.HasOne(b => b.Car)
                    .WithMany(c => c.Bids)
                    .HasForeignKey(b => b.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserFavoriteCar>(e =>
            {

                e.HasKey(f => new { f.Username, f.CarId });
            });

            modelBuilder.Entity<Payment>(e =>
            {
                e.HasIndex(p => p.ProviderOrderId);
                e.HasIndex(p => p.Username);
                e.HasOne(p => p.Car)
                    .WithMany()
                    .HasForeignKey(p => p.CarId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
