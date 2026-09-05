using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services.Email;
using WEBTechnologies_Final.Services.Marketplace;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// The reference is the only thing a seller can reliably quote about a placement they paid
/// for, and the only handle an admin has for finding it. Two failures matter: a code that
/// cannot be typed back in, and a code that collides with somebody else's.
/// </summary>
public class PromotionReferenceTests
{
    [Fact]
    public void A_generated_reference_is_in_the_documented_shape()
    {
        var reference = PromotionReference.Next();

        Assert.Matches(@"^PM-[0-9A-Z]{4}-[0-9A-Z]{4}$", reference);
    }

    [Fact]
    public void Generated_references_never_contain_the_characters_people_mistype()
    {
        // O/0 and I/1 are the pairs that get read wrong down a phone; U is excluded so that
        // eight random letters are much less likely to spell something.
        for (var i = 0; i < 500; i++)
        {
            var body = PromotionReference.Next().Replace("PM-", "").Replace("-", "");
            Assert.DoesNotContain(body, c => c is 'O' or '0' or 'I' or '1' or 'L' or 'U');
        }
    }

    [Fact]
    public void References_do_not_repeat_in_any_realistic_run()
    {
        // Not proof of uniqueness - PromotionService checks the database for that - but it
        // would catch the classic failure of a per-call seeded RNG handing out the same code
        // to everybody who bought a placement in the same second.
        var seen = new HashSet<string>();
        for (var i = 0; i < 5000; i++)
            Assert.True(seen.Add(PromotionReference.Next()), "Duplicate reference generated.");
    }

    [Theory]
    [InlineData("PM-A234-B567")]
    [InlineData("pm-a234-b567")]
    [InlineData("A234B567")]
    [InlineData("a234 b567")]
    [InlineData("  PM A234 B567  ")]
    [InlineData("PMA234B567")]
    public void Normalise_accepts_what_a_human_actually_types(string typed)
    {
        // An admin retyping a code the seller read out correctly and getting "not found"
        // looks like the receipt was lost. It is worth being generous here.
        Assert.Equal("PM-A234-B567", PromotionReference.Normalise(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PM-A234")]
    [InlineData("A234B5678")]
    [InlineData("PM-O000-I111")]
    public void Normalise_rejects_what_cannot_be_a_reference(string? typed)
    {
        Assert.Null(PromotionReference.Normalise(typed));
    }

    [Fact]
    public void A_generated_reference_survives_a_round_trip_through_Normalise()
    {
        for (var i = 0; i < 200; i++)
        {
            var reference = PromotionReference.Next();
            Assert.Equal(reference, PromotionReference.Normalise(reference.ToLowerInvariant()));
        }
    }
}

/// <summary>
/// The receipt is not sent yet - nothing is charged for anything - but the reference it
/// carries is the whole reason the Promotion table exists, so a body that omits it would
/// make every "quote your reference" instruction on the admin side useless.
/// </summary>
public class PromotionReceiptEmailTests
{
    private static Promotion Sample() => new()
    {
        Reference = "PM-A234-B567",
        CarTitle = "BMW 320d Touring",
        Tier = PromotionTier.FrontPage,
        StartedUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
        EndsUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void The_reference_appears_in_the_subject_and_the_body()
    {
        var promotion = Sample();

        Assert.Contains(promotion.Reference, PromotionReceiptEmail.Subject(promotion));
        Assert.Contains(promotion.Reference, PromotionReceiptEmail.HtmlBody(promotion));
    }

    [Fact]
    public void An_unpriced_placement_does_not_claim_a_payment_was_taken()
    {
        // Placements are arranged off-site today. A receipt showing "EUR 0.00" would be the
        // site's first outright false statement to a customer.
        var body = PromotionReceiptEmail.HtmlBody(Sample());

        Assert.DoesNotContain("€0", body);
        Assert.Contains("Arranged directly with us", body);
    }

    [Fact]
    public void A_priced_placement_states_what_was_paid()
    {
        var promotion = Sample();
        promotion.PriceEur = 25m;

        Assert.Contains("€25.00", PromotionReceiptEmail.HtmlBody(promotion));
    }

    [Fact]
    public void A_listing_title_containing_markup_is_encoded_rather_than_rendered()
    {
        // Titles are seller-supplied and this body is HTML that lands in a mail client.
        var promotion = Sample();
        promotion.CarTitle = "<script>alert(1)</script>";

        var body = PromotionReceiptEmail.HtmlBody(promotion);

        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
    }
}
