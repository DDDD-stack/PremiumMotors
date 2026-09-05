using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Paid listings are mixed into the free ones rather than fenced off above them, and the
/// mixing has to be provably lossless: a bug here either shows the same car twice or drops
/// somebody's listing off the page entirely, and neither would be reported - the seller
/// just quietly gets nothing for their money, or nothing for being on the site at all.
///
/// The ratio matters commercially in both directions. Too sparse and promotion is not worth
/// buying; too dense and free sellers conclude the site is pay-to-be-seen and leave, which
/// takes the stock with them and leaves nothing worth advertising against.
/// </summary>
public class PromotedMixTests
{
    private static Car Car(int id) => new() { Id = id, Make = "BMW", Model = "320d", Year = 2020 };

    private static List<Car> Cars(params int[] ids) => ids.Select(Car).ToList();

    [Fact]
    public void No_adverts_leaves_the_page_exactly_as_it_was()
    {
        var free = Cars(1, 2, 3);
        var mixed = CarQueries.MixPromoted(free, new List<Car>(), 2);

        Assert.Equal(new[] { 1, 2, 3 }, mixed.Select(c => c.Id));
    }

    [Fact]
    public void Every_second_slot_is_an_advert_while_adverts_remain()
    {
        var mixed = CarQueries.MixPromoted(Cars(1, 2, 3, 4), Cars(90, 91), 2);

        // advert, free, advert, free, then the rest of the free listings
        Assert.Equal(new[] { 90, 1, 91, 2, 3, 4 }, mixed.Select(c => c.Id));
    }

    [Fact]
    public void A_free_listing_always_sits_between_two_adverts()
    {
        var mixed = CarQueries.MixPromoted(Cars(1, 2, 3), Cars(90, 91, 92), 2);
        var ids = mixed.Select(c => c.Id).ToList();

        for (var i = 1; i < ids.Count; i++)
        {
            var bothAdverts = ids[i] >= 90 && ids[i - 1] >= 90;
            Assert.False(bothAdverts, $"Adverts adjacent at position {i}: {string.Join(",", ids)}");
        }
    }

    [Fact]
    public void Nothing_is_dropped_and_nothing_is_repeated()
    {
        var free = Cars(1, 2, 3, 4, 5);
        var promoted = Cars(90, 91);

        var mixed = CarQueries.MixPromoted(free, promoted, 2);

        Assert.Equal(free.Count + promoted.Count, mixed.Count);
        Assert.Equal(mixed.Count, mixed.Select(c => c.Id).Distinct().Count());
        foreach (var expected in free.Concat(promoted))
            Assert.Contains(mixed, c => c.Id == expected.Id);
    }

    [Fact]
    public void Left_over_adverts_are_appended_rather_than_lost()
    {
        // More adverts than free listings: the cadence cannot place them all, and the
        // remainder must still appear rather than being silently dropped.
        var mixed = CarQueries.MixPromoted(Cars(1), Cars(90, 91, 92), 2);

        Assert.Equal(4, mixed.Count);
        foreach (var id in new[] { 1, 90, 91, 92 })
            Assert.Contains(mixed, c => c.Id == id);
    }

    [Fact]
    public void Adverts_never_take_a_majority_while_free_listings_remain()
    {
        // The commercial guardrail: with plenty of both, adverts get half the page at most.
        var mixed = CarQueries.MixPromoted(Cars(1, 2, 3, 4, 5, 6), Cars(90, 91, 92), 2);
        var adverts = mixed.Count(c => c.Id >= 90);

        Assert.True(adverts * 2 <= mixed.Count,
            $"Adverts took {adverts} of {mixed.Count} slots.");
    }

    [Fact]
    public void An_empty_page_with_adverts_still_shows_the_adverts()
    {
        var mixed = CarQueries.MixPromoted(new List<Car>(), Cars(90, 91), 2);

        Assert.Equal(new[] { 90, 91 }, mixed.Select(c => c.Id));
    }
}
