using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Car> Cars => Set<Car>();
        public DbSet<Offer> Offers => Set<Offer>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<UserFavoriteCar> UserFavoriteCars => Set<UserFavoriteCar>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<UserToken> UserTokens => Set<UserToken>();
        public DbSet<Dealership> Dealerships => Set<Dealership>();
        public DbSet<SellerReview> SellerReviews => Set<SellerReview>();
        public DbSet<CarPriceChange> CarPriceChanges => Set<CarPriceChange>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<ListingViewDaily> ListingViewDaily => Set<ListingViewDaily>();
        public DbSet<SessionCacheEntry> SessionCache => Set<SessionCacheEntry>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        }

        /// <summary>
        /// Writes a CarPriceChange row whenever a listing's price actually changes.
        ///
        /// Done here rather than in the edit actions because a listing can be repriced from
        /// four places - the seller panel, the admin panel, the seller API and the listings
        /// API - and the first attempt at this only covered the admin path, so a seller
        /// dropping their price recorded nothing and the "reduced from" badge never appeared.
        /// The change tracker is the one place all four converge, and it still holds the old
        /// value in OriginalValues, which is the only copy of it that exists.
        ///
        /// Creation deliberately writes NO baseline row: a new listing's Id does not exist
        /// until the insert completes, so a baseline would need a second save. It is not
        /// needed either - the earliest change row carries the original price in
        /// PreviousPrice, so nothing is lost.
        /// </summary>
        public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            RecordPriceChanges();
            return await base.SaveChangesAsync(ct);
        }

        public override int SaveChanges()
        {
            RecordPriceChanges();
            return base.SaveChanges();
        }

        private void RecordPriceChanges()
        {
            var repriced = ChangeTracker.Entries<Car>()
                .Where(e => e.State == EntityState.Modified)
                .Select(e => new
                {
                    Car = e.Entity,
                    Previous = e.Property(c => c.Price).OriginalValue,
                    Current = e.Property(c => c.Price).CurrentValue
                })
                .Where(x => x.Previous != x.Current)
                .ToList();

            foreach (var change in repriced)
            {
                CarPriceChanges.Add(new CarPriceChange
                {
                    CarId = change.Car.Id,
                    Price = change.Current,
                    PreviousPrice = change.Previous,
                    ChangedUtc = DateTime.UtcNow
                });
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(e =>
            {
                e.HasIndex(u => u.Username).IsUnique();
                e.HasIndex(u => u.Email).IsUnique();
                e.Property(u => u.Role).HasMaxLength(32).HasDefaultValue(Roles.User);
                e.Property(u => u.IsActive).HasDefaultValue(true);
                e.Property(u => u.SellerDisplayName).HasMaxLength(80);
                e.Property(u => u.SellerLocation).HasMaxLength(80);
                e.Property(u => u.BusinessRegistrationNumber).HasMaxLength(40);
                e.Property(u => u.VatNumber).HasMaxLength(40);
                e.Property(u => u.BusinessAddress).HasMaxLength(160);
                e.Property(u => u.Website).HasMaxLength(160);
                e.Property(u => u.ContactName).HasMaxLength(80);
                e.HasIndex(u => u.IsSeller);
                e.Property(u => u.RatingAverage).HasPrecision(3, 2);
            });

            modelBuilder.Entity<Dealership>(e =>
            {
                // One shopfront per business account, and one URL per shopfront. Both are
                // unique constraints rather than conventions, because both are things the
                // rest of the app is allowed to assume.
                e.HasIndex(d => d.OwnerUserId).IsUnique();
                e.HasIndex(d => d.Slug).IsUnique();
                e.HasIndex(d => d.City);

                // Deleting the account deletes the shopfront. Unlike a listing, a dealership
                // has no meaning without the business behind it.
                e.HasOne(d => d.Owner)
                    .WithMany()
                    .HasForeignKey(d => d.OwnerUserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SellerReview>(e =>
            {
                e.HasIndex(r => new { r.SellerUserId, r.CreatedUtc });

                // One review per listing: the unique index is what actually enforces "you may
                // review a seller once per car you bought from them". A controller check alone
                // loses to a double-submitted form.
                e.HasIndex(r => r.CarId).IsUnique().HasFilter("\"CarId\" IS NOT NULL");

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(r => r.SellerUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // The author may leave; what they wrote stays, attributed to the stored name.
                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(r => r.AuthorUserId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne(r => r.Car)
                    .WithMany()
                    .HasForeignKey(r => r.CarId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<SessionCacheEntry>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasMaxLength(200);
                // The sweep deletes by expiry, so that is the column it has to seek on.
                e.HasIndex(x => x.ExpiresAtUtc);
            });

            modelBuilder.Entity<ListingViewDaily>(e =>
            {
                // Unique on (car, day) is what makes the counter an UPSERT rather than an
                // append-only event log. Without it a busy listing writes a row per view and
                // the table grows without limit.
                e.HasIndex(v => new { v.CarId, v.Day }).IsUnique();

                e.HasOne(v => v.Car)
                    .WithMany()
                    .HasForeignKey(v => v.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Promotion>(e =>
            {
                // Unique because the reference is what a seller quotes and an admin looks up.
                // Two receipts sharing a code is the one failure this table cannot recover
                // from, so the database enforces it rather than the code that generates it.
                e.HasIndex(p => p.Reference).IsUnique();

                // The admin screen's two questions: what is running on this listing, and what
                // has run recently across the whole site.
                e.HasIndex(p => new { p.CarId, p.StartedUtc });
                e.HasIndex(p => p.StartedUtc);

                e.Property(p => p.PriceEur).HasPrecision(18, 2);

                // Deleting a listing must NOT delete the receipts for it. Listings are hard
                // deleted here, and a seller can still ask what they were charged after
                // deleting the car - "the row is gone" is not an answer to that. The link is
                // dropped and Promotion.CarTitle keeps the receipt readable.
                e.HasOne(p => p.Car)
                    .WithMany()
                    .HasForeignKey(p => p.CarId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<CarPriceChange>(e =>
            {
                e.HasIndex(p => new { p.CarId, p.ChangedUtc });
                e.Property(p => p.Price).HasPrecision(18, 2);
                e.Property(p => p.PreviousPrice).HasPrecision(18, 2);

                e.HasOne(p => p.Car)
                    .WithMany()
                    .HasForeignKey(p => p.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<RefreshToken>(e =>
            {
                e.HasIndex(t => t.TokenHash).IsUnique();
                e.HasIndex(t => new { t.UserId, t.RevokedUtc });
                e.Property(t => t.TokenHash).HasMaxLength(64);
                e.Property(t => t.Device).HasMaxLength(64);

                // Deleting an account signs out every device it owned.
                e.HasOne(t => t.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserToken>(e =>
            {
                e.HasIndex(t => t.TokenHash).IsUnique();
                e.HasIndex(t => new { t.UserId, t.Purpose });
                e.Property(t => t.TokenHash).HasMaxLength(64);

                e.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Car>(e =>
            {
                e.Property(c => c.Price).HasPrecision(18, 2);

                // FirstRegistration is the only DateTime on a listing that comes from user
                // input: an <input type="date"> binds to Kind=Unspecified, and Npgsql refuses
                // to write anything but Kind=Utc to a timestamptz. Normalizing here rather
                // than in each controller means no future write path can miss it — which is
                // exactly how the admin create form started throwing.
                //
                // AsUtc TAGS the value without shifting it. A timezone conversion would be
                // wrong: this is a calendar date, not an instant, and shifting could move a
                // registration back to the previous day.
                e.Property(c => c.FirstRegistration)
                    .HasConversion(v => AppTime.AsUtc(v), v => v);
                e.Property(c => c.SoldPrice).HasPrecision(18, 2);
                // The value comparer is what makes a CONTENT change to the list detectable.
                // Without it EF snapshots by reference, so any code that mutates the list in
                // place — rather than assigning a new one, which is what UpdateCarAsync is
                // careful to do — would silently not save. EF warns about this at startup.
                e.Property(c => c.ImagePaths)
                    .HasConversion(
                        v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                        v => System.Text.Json.JsonSerializer.Deserialize<List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<string>(),
                        new ValueComparer<List<string>>(
                            (a, b) => a != null && b != null ? a.SequenceEqual(b) : a == b,
                            v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
                            v => v.ToList()));

                e.HasIndex(c => c.OwnerId);
                // Every public query filters on Status, so it carries the browse index now.
                e.HasIndex(c => c.Status);
                e.HasIndex(c => new { c.Make, c.Model });

                // A listing outlives its seller's account row; ownership just goes null.
                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(c => c.OwnerId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(c => c.SoldToUserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Offer>(e =>
            {
                e.Property(o => o.Amount).HasPrecision(18, 2);
                e.HasIndex(o => o.BuyerId);
                // The seller's offer inbox reads by listing and status.
                e.HasIndex(o => new { o.CarId, o.Status });

                e.HasOne(o => o.Car)
                    .WithMany(c => c.Offers)
                    .HasForeignKey(o => o.CarId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(o => o.Buyer)
                    .WithMany()
                    .HasForeignKey(o => o.BuyerId)
                    .OnDelete(DeleteBehavior.SetNull);

                // An offer points at its thread, but deleting the thread must not delete
                // the offer -- the offer is the record of what was actually proposed.
                e.HasOne(o => o.Conversation)
                    .WithMany()
                    .HasForeignKey(o => o.ConversationId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Conversation>(e =>
            {
                // One thread per buyer per listing: reopening a chat from a second offer
                // continues the existing conversation rather than starting a parallel one.
                e.HasIndex(c => new { c.CarId, c.BuyerId }).IsUnique();
                e.HasIndex(c => c.SellerId);
                e.HasIndex(c => c.LastMessageUtc);

                e.HasOne(c => c.Car)
                    .WithMany()
                    .HasForeignKey(c => c.CarId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(c => c.BuyerId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(c => c.SellerId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<Message>(e =>
            {
                e.HasIndex(m => new { m.ConversationId, m.SentUtc });

                e.HasOne(m => m.Conversation)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(m => m.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(m => m.SenderId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<UserFavoriteCar>(e =>
            {
                // Keyed by stable user id, so renaming a user keeps their saved listings.
                e.HasKey(f => new { f.UserId, f.CarId });

                e.HasOne(f => f.User)
                    .WithMany()
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                e.HasOne(f => f.Car)
                    .WithMany()
                    .HasForeignKey(f => f.CarId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Payment>(e =>
            {
                e.HasIndex(p => p.ProviderOrderId);
                e.HasIndex(p => p.Username);
                e.HasIndex(p => p.UserId);

                e.HasOne(p => p.Car)
                    .WithMany()
                    .HasForeignKey(p => p.CarId)
                    .OnDelete(DeleteBehavior.SetNull);

                e.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
