using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Periodically resolves auctions whose end time has passed. User listings have no admin
    /// watching them, so this runs the close: pick the winning offer (revealing the seller's
    /// right to that buyer's contact) or, if no offers came in, release the listing token so
    /// the seller gets a free relist. Each auction is processed exactly once.
    /// </summary>
    public class AuctionCloseService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly IServiceProvider _services;
        private readonly ILogger<AuctionCloseService> _logger;

        public AuctionCloseService(IServiceProvider services, ILogger<AuctionCloseService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await CloseDueAuctionsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auction close sweep failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task CloseDueAuctionsAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // AuctionEnd is stored as local wall-clock (Unspecified), matching Car.IsClosed.
            var now = DateTime.Now;

            var due = await db.Cars
                .Include(c => c.Bids)
                .Where(c => !c.ClosureProcessed && c.AuctionEnd != null && c.AuctionEnd <= now)
                .ToListAsync(ct);

            if (due.Count == 0) return;

            foreach (var car in due)
            {
                var token = await db.Payments
                    .FirstOrDefaultAsync(p => p.CarId == car.Id && p.Status == PaymentStatus.Paid, ct);

                if (car.Bids.Count > 0)
                {
                    // Winner = highest offer. The token is spent; seller may now contact the buyer.
                    var winner = car.Bids.OrderByDescending(b => b.Amount).First();
                    car.IsSold = true;
                    car.SoldTo = winner.BidderUsername;
                    if (token is not null) token.OfferConsumed = true;
                }
                else
                {
                    // No offers: the listing did nothing for the seller. Free the token for a
                    // free relist and retire the dead listing from public view.
                    if (token is not null) token.CarId = null;
                    car.IsPublished = false;
                }

                car.ClosureProcessed = true;
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Closed {Count} auction(s).", due.Count);
        }
    }
}
