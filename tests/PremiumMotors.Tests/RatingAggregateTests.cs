using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// User.RatingCount and User.RatingAverage are denormalized so a page of listing cards does
/// not need an aggregate per card. They are RECOMPUTED from the review table rather than
/// incremented, and these tests hold that: an incremented counter drifts the first time a
/// write is rolled back or a review is removed, and a reputation number that quietly
/// disagrees with the reviews beneath it is worse than no number at all.
/// </summary>
public class RatingAggregateTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(AppDbContext Db, int SellerId)> SellerWithRatingsAsync(params int[] ratings)
    {
        var db = NewContext();
        var seller = new User { Username = "seller", Email = "s@x.com", IsSeller = true };
        db.Users.Add(seller);
        await db.SaveChangesAsync();

        foreach (var rating in ratings)
            db.SellerReviews.Add(new SellerReview
            {
                SellerUserId = seller.Id,
                AuthorUsername = "buyer",
                Rating = rating
            });

        await db.SaveChangesAsync();
        return (db, seller.Id);
    }

    [Fact]
    public async Task The_average_is_the_mean_of_every_review()
    {
        var (db, sellerId) = await SellerWithRatingsAsync(5, 4, 3);

        await new ReviewService(db).RecomputeAsync(sellerId);

        var seller = await db.Users.FirstAsync(u => u.Id == sellerId);
        Assert.Equal(3, seller.RatingCount);
        Assert.Equal(4.00m, seller.RatingAverage);
    }

    [Fact]
    public async Task The_average_is_rounded_to_two_decimals()
    {
        // 5 + 4 + 4 = 13 / 3 = 4.333..., which the numeric(3,2) column cannot hold unrounded.
        var (db, sellerId) = await SellerWithRatingsAsync(5, 4, 4);

        await new ReviewService(db).RecomputeAsync(sellerId);

        Assert.Equal(4.33m, (await db.Users.FirstAsync(u => u.Id == sellerId)).RatingAverage);
    }

    [Fact]
    public async Task A_seller_with_no_reviews_has_a_zero_average_and_not_a_divide_by_zero()
    {
        var (db, sellerId) = await SellerWithRatingsAsync();

        await new ReviewService(db).RecomputeAsync(sellerId);

        var seller = await db.Users.FirstAsync(u => u.Id == sellerId);
        Assert.Equal(0, seller.RatingCount);
        Assert.Equal(0m, seller.RatingAverage);
        Assert.False(seller.HasRating);
    }

    [Fact]
    public async Task Recomputing_corrects_a_stored_value_that_has_drifted()
    {
        var (db, sellerId) = await SellerWithRatingsAsync(5, 5);

        // Whatever put these there — a rolled-back write, an old incremental counter — the
        // recompute must overrule them rather than build on them.
        var seller = await db.Users.FirstAsync(u => u.Id == sellerId);
        seller.RatingCount = 99;
        seller.RatingAverage = 1.00m;
        await db.SaveChangesAsync();

        await new ReviewService(db).RecomputeAsync(sellerId);

        seller = await db.Users.FirstAsync(u => u.Id == sellerId);
        Assert.Equal(2, seller.RatingCount);
        Assert.Equal(5.00m, seller.RatingAverage);
    }

    [Fact]
    public async Task The_distribution_buckets_every_star_level()
    {
        var (db, sellerId) = await SellerWithRatingsAsync(5, 5, 5, 4, 1);

        var buckets = await new ReviewService(db).DistributionAsync(sellerId);

        Assert.Equal(new[] { 1, 0, 0, 1, 3 }, buckets);
    }
}
