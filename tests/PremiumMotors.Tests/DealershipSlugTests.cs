using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// A dealership's slug is its permanent public URL — the one a dealer prints on a business
/// card — so it has to be predictable and it has to survive whatever a business name contains.
/// </summary>
public class DealershipSlugTests
{
    [Theory]
    [InlineData("Adriatik Motors", "adriatik-motors")]
    [InlineData("E2E Motors", "e2e-motors")]
    [InlineData("Adriatik Motors sh.p.k.", "adriatik-motors-sh-p-k")]
    [InlineData("  Spaces   Everywhere  ", "spaces-everywhere")]
    [InlineData("A&B Cars!!!", "a-b-cars")]
    [InlineData("---Leading and trailing---", "leading-and-trailing")]
    public void Names_become_readable_url_slugs(string name, string expected) =>
        Assert.Equal(expected, DealershipService.Slugify(name));

    [Fact]
    public void A_name_with_nothing_usable_in_it_produces_an_empty_slug()
    {
        // The caller substitutes "dealer" — asserting the empty result here keeps that
        // fallback deliberate rather than accidental.
        Assert.Equal("", DealershipService.Slugify("!!! ??? ***"));
    }

    [Fact]
    public void Runs_of_punctuation_collapse_into_a_single_dash() =>
        Assert.Equal("a-b", DealershipService.Slugify("A  ---  B"));
}
