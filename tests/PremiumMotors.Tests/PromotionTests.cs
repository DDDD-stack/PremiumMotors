using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Paid placement is the only thing anyone buys on this site, so the two ways of asking
/// "is this promoted right now" have to agree: Car.IsPromoted, used when rendering, and
/// CarQueries.WherePromoted, used when querying. EF cannot translate the property, so the
/// conditions are written out twice and nothing but a test stops them drifting.
///
/// The failure they guard against is quiet: an expired promotion that still shows in the
/// marketplace strip but not on the front page, or a sold car advertised at the top of a
/// page a buyer cannot act on.
/// </summary>
public class PromotionTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Car Listing(
        PromotionTier tier, DateTime? until, ListingStatus status = ListingStatus.Active) =>
        new()
        {
            Make = "BMW", Model = "320d", Year = 2020, Country = "Albania",
            Price = 20000m, OwnerId = 1,
            Status = status,
            PromotionTier = tier,
            PromotedUntilUtc = until
        };

    private static readonly DateTime Future = DateTime.UtcNow.AddDays(7);
    private static readonly DateTime Past = DateTime.UtcNow.AddDays(-1);

    // ---------- The rendered property ----------

    [Fact]
    public void An_ordinary_listing_is_not_promoted()
    {
        Assert.False(Listing(PromotionTier.None, null).IsPromoted);
    }

    [Fact]
    public void A_live_promotion_is_promoted()
    {
        Assert.True(Listing(PromotionTier.Promoted, Future).IsPromoted);
    }

    [Fact]
    public void A_promotion_that_has_run_out_is_not_promoted()
    {
        Assert.False(Listing(PromotionTier.Promoted, Past).IsPromoted);
    }

    [Fact]
    public void A_tier_with_no_end_date_is_not_promoted()
    {
        // Guards the case where a tier is set without a date - the placement would
        // otherwise run for ever and could only be sold once.
        Assert.False(Listing(PromotionTier.Promoted, null).IsPromoted);
    }

    [Theory]
    [InlineData(ListingStatus.Reserved)]
    [InlineData(ListingStatus.Sold)]
    [InlineData(ListingStatus.Draft)]
    [InlineData(ListingStatus.Archived)]
    public void Only_an_active_listing_is_ever_promoted(ListingStatus status)
    {
        // Reserved and sold cars stay browsable, but advertising one sends the visitor to
        // a car they cannot buy - worse than advertising nothing.
        Assert.False(Listing(PromotionTier.FrontPage, Future, status).IsPromoted);
    }

    [Fact]
    public void Front_page_placement_includes_the_lower_tier()
    {
        var car = Listing(PromotionTier.FrontPage, Future);
        Assert.True(car.IsPromoted);
        Assert.True(car.IsFrontPagePromoted);
    }

    [Fact]
    public void The_lower_tier_does_not_reach_the_front_page()
    {
        var car = Listing(PromotionTier.Promoted, Future);
        Assert.True(car.IsPromoted);
        Assert.False(car.IsFrontPagePromoted);
    }

    // ---------- The query, which must agree with the property ----------

    [Fact]
    public async Task WherePromoted_matches_exactly_what_IsPromoted_says()
    {
        var db = NewContext();
        db.Cars.AddRange(
            Listing(PromotionTier.None, null),
            Listing(PromotionTier.Promoted, Future),
            Listing(PromotionTier.Promoted, Past),
            Listing(PromotionTier.Promoted, null),
            Listing(PromotionTier.FrontPage, Future),
            Listing(PromotionTier.FrontPage, Future, ListingStatus.Sold));
        await db.SaveChangesAsync();

        var fromQuery = await db.Cars
            .WherePromoted(PromotionTier.Promoted, DateTime.UtcNow)
            .ToListAsync();

        var fromProperty = (await db.Cars.ToListAsync()).Where(c => c.IsPromoted).ToList();

        Assert.Equal(2, fromQuery.Count);
        Assert.Equal(
            fromProperty.Select(c => c.Id).OrderBy(id => id),
            fromQuery.Select(c => c.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Asking_for_the_front_page_excludes_the_cheaper_tier()
    {
        var db = NewContext();
        db.Cars.AddRange(
            Listing(PromotionTier.Promoted, Future),
            Listing(PromotionTier.FrontPage, Future));
        await db.SaveChangesAsync();

        var front = await db.Cars
            .WherePromoted(PromotionTier.FrontPage, DateTime.UtcNow)
            .ToListAsync();

        Assert.Single(front);
        Assert.Equal(PromotionTier.FrontPage, front[0].PromotionTier);
    }

    [Fact]
    public async Task The_most_expensive_placement_is_ordered_first()
    {
        var db = NewContext();
        db.Cars.AddRange(
            Listing(PromotionTier.Promoted, Future),
            Listing(PromotionTier.FrontPage, Future),
            Listing(PromotionTier.Promoted, Future));
        await db.SaveChangesAsync();

        var ordered = await db.Cars
            .WherePromoted(PromotionTier.Promoted, DateTime.UtcNow)
            .OrderByPromotion()
            .ToListAsync();

        Assert.Equal(PromotionTier.FrontPage, ordered[0].PromotionTier);
    }
}
