using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// The rating system is only worth anything if the eligibility rule holds, so that rule is
/// what these tests are about: a review must come from the recorded buyer of a listing that
/// actually sold, and only once.
/// </summary>
public class ReviewServiceTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(AppDbContext Db, int SellerId, int BuyerId, int CarId)> SoldCarAsync()
    {
        var db = NewContext();

        var seller = new User { Username = "seller", Email = "s@x.com", IsSeller = true };
        var buyer = new User { Username = "buyer", Email = "b@x.com" };
        db.Users.AddRange(seller, buyer);
        await db.SaveChangesAsync();

        var car = new Car
        {
            Make = "Audi", Model = "A4", Year = 2019, Country = "Albania",
            Price = 10000m, OwnerId = seller.Id, OwnerUsername = seller.Username,
            Status = ListingStatus.Sold, SoldToUserId = buyer.Id, SoldPrice = 9500m,
            SoldUtc = DateTime.UtcNow
        };
        db.Cars.Add(car);
        await db.SaveChangesAsync();

        return (db, seller.Id, buyer.Id, car.Id);
    }

    [Fact]
    public async Task The_recorded_buyer_can_review_the_seller()
    {
        var (db, sellerId, buyerId, carId) = await SoldCarAsync();
        var reviews = new ReviewService(db);

        var result = await reviews.LeaveAsync(carId, buyerId, 5, "Great car.");

        Assert.True(result.Success);
        Assert.Equal(sellerId, result.Value!.SellerUserId);
    }

    [Fact]
    public async Task Someone_who_did_not_buy_the_car_cannot_review_the_seller()
    {
        var (db, _, _, carId) = await SoldCarAsync();
        var stranger = new User { Username = "stranger", Email = "x@x.com" };
        db.Users.Add(stranger);
        await db.SaveChangesAsync();

        var result = await new ReviewService(db).LeaveAsync(carId, stranger.Id, 1, "Never bought it.");

        Assert.False(result.Success);
        Assert.Equal(MarketplaceCodes.Forbidden, result.Code);
    }

    [Fact]
    public async Task A_listing_that_has_not_sold_cannot_be_reviewed()
    {
        var (db, _, buyerId, carId) = await SoldCarAsync();

        // Still reserved: the money has not changed hands, so there is nothing to judge yet.
        var car = await db.Cars.FirstAsync(c => c.Id == carId);
        car.Status = ListingStatus.Reserved;
        await db.SaveChangesAsync();

        var result = await new ReviewService(db).LeaveAsync(carId, buyerId, 5, null);

        Assert.False(result.Success);
        Assert.Equal(MarketplaceCodes.Forbidden, result.Code);
    }

    [Fact]
    public async Task The_same_purchase_cannot_be_reviewed_twice()
    {
        var (db, _, buyerId, carId) = await SoldCarAsync();
        var reviews = new ReviewService(db);

        Assert.True((await reviews.LeaveAsync(carId, buyerId, 5, "First.")).Success);

        var second = await reviews.LeaveAsync(carId, buyerId, 1, "Changed my mind.");

        Assert.False(second.Success);
        Assert.Equal(ReviewCodes.AlreadyReviewed, second.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task A_rating_outside_one_to_five_is_rejected(int rating)
    {
        var (db, _, buyerId, carId) = await SoldCarAsync();

        var result = await new ReviewService(db).LeaveAsync(carId, buyerId, rating, null);

        Assert.False(result.Success);
    }
}
