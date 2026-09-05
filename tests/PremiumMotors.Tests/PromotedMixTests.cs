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
/// The live ratio is two adverts to one free listing. These tests pin the shape of the
/// interleave and the losslessness; they deliberately do not assert a maximum advert
/// share, because that is a commercial choice that lives in CarsController and is expected
/// to be tuned.
/// </summary>
public class PromotedMixTests
{
    private const int PromotedPerBlock = 2;
    private const int FreePerBlock = 1;

    private static Car Car(int id) => new() { Id = id, Make = "BMW", Model = "320d", Year = 2020 };

    private static List<Car> Cars(params int[] ids) => ids.Select(Car).ToList();

    private static List<Car> Mix(IReadOnlyList<Car> free, IReadOnlyList<Car> promoted) =>
        CarQueries.MixPromoted(free, promoted, PromotedPerBlock, FreePerBlock);

    [Fact]
    public void No_adverts_leaves_the_page_exactly_as_it_was()
    {
        var mixed = Mix(Cars(1, 2, 3), new List<Car>());

        Assert.Equal(new[] { 1, 2, 3 }, mixed.Select(c => c.Id));
    }

    [Fact]
    public void Two_adverts_then_one_free_listing_repeating()
    {
        var mixed = Mix(Cars(1, 2, 3, 4), Cars(90, 91, 92, 93));

        Assert.Equal(new[] { 90, 91, 1, 92, 93, 2, 3, 4 }, mixed.Select(c => c.Id));
    }

    [Fact]
    public void Once_the_adverts_run_out_the_rest_of_the_page_is_free_listings_in_order()
    {
        var mixed = Mix(Cars(1, 2, 3, 4, 5), Cars(90, 91));
        var ids = mixed.Select(c => c.Id).ToList();

        Assert.Equal(new[] { 90, 91, 1, 2, 3, 4, 5 }, ids);
        // Nothing paid appears after the block that exhausted them.
        Assert.DoesNotContain(ids.Skip(3), id => id >= 90);
    }

    [Fact]
    public void Nothing_is_dropped_and_nothing_is_repeated()
    {
        var free = Cars(1, 2, 3, 4, 5);
        var promoted = Cars(90, 91, 92);

        var mixed = Mix(free, promoted);

        Assert.Equal(free.Count + promoted.Count, mixed.Count);
        Assert.Equal(mixed.Count, mixed.Select(c => c.Id).Distinct().Count());
        foreach (var expected in free.Concat(promoted))
            Assert.Contains(mixed, c => c.Id == expected.Id);
    }

    [Fact]
    public void Both_sides_keep_the_order_they_arrived_in()
    {
        // Free listings arrive already sorted by whatever the buyer asked for, and paid ones
        // by tier. Interleaving must not quietly reorder either.
        var mixed = Mix(Cars(1, 2, 3, 4), Cars(90, 91, 92));
        var ids = mixed.Select(c => c.Id).ToList();

        Assert.Equal(new[] { 1, 2, 3, 4 }, ids.Where(id => id < 90));
        Assert.Equal(new[] { 90, 91, 92 }, ids.Where(id => id >= 90));
    }

    [Fact]
    public void Left_over_adverts_are_appended_rather_than_lost()
    {
        // Far more adverts than free listings: the cadence cannot place them all inside the
        // blocks, and the remainder must still appear rather than being silently dropped.
        var mixed = Mix(Cars(1), Cars(90, 91, 92, 93, 94));

        Assert.Equal(6, mixed.Count);
        foreach (var id in new[] { 1, 90, 91, 92, 93, 94 })
            Assert.Contains(mixed, c => c.Id == id);
    }

    [Fact]
    public void An_empty_page_with_adverts_still_shows_the_adverts()
    {
        var mixed = Mix(new List<Car>(), Cars(90, 91));

        Assert.Equal(new[] { 90, 91 }, mixed.Select(c => c.Id));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, -1)]
    public void A_block_size_below_one_is_rejected_rather_than_looping_forever(
        int promotedPerBlock, int freePerBlock)
    {
        // A zero-length block adds nothing per pass, so the loop would never terminate and
        // the request would hang instead of failing. Better to be loud about it.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CarQueries.MixPromoted(Cars(1, 2), Cars(90), promotedPerBlock, freePerBlock));
    }
}
