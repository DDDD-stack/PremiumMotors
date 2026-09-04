using System.ComponentModel.DataAnnotations;
using WEBTechnologies_Final.Models;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Regression cover for the seller opt-in that rejected its own terms checkbox no matter how
/// many times it was ticked. The cause was [Range(typeof(bool), "true", "true")], a widely
/// copied idiom that never validates.
/// </summary>
public class MustBeTrueTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Ticked_checkbox_passes()
    {
        Assert.True(new MustBeTrueAttribute().IsValid(true));
    }

    [Fact]
    public void Unticked_checkbox_fails()
    {
        Assert.False(new MustBeTrueAttribute().IsValid(false));
    }

    [Fact]
    public void Missing_value_fails()
    {
        // A checkbox that is never posted binds to null, not false.
        Assert.False(new MustBeTrueAttribute().IsValid(null));
    }

    [Fact]
    public void The_old_Range_idiom_fails_on_the_client_not_the_server()
    {
        // RangeAttribute is fine server-side — which is why this bug survived: a model-only
        // test would pass while the form was unusable in a browser.
        var range = new RangeAttribute(typeof(bool), "true", "true");
        Assert.True(range.IsValid(true));

        // The client-side failure is a case mismatch. RangeAttribute stringifies its bounds
        // with Boolean.ToString() -> "True", while the checkbox tag helper renders
        // value="true". jQuery validate then evaluates "true" <= "True", which is false
        // because lowercase sorts after uppercase, so a TICKED box fails the rule.
        Assert.Equal("True", range.Minimum.ToString());
        Assert.Equal(1, string.CompareOrdinal("true", "True") > 0 ? 1 : 0);
    }

    [Fact]
    public void Seller_optin_accepts_a_ticked_form()
    {
        var form = new BecomeSellerViewModel { DisplayName = "Ana", AcceptTerms = true };
        Assert.Empty(Validate(form));
    }

    [Fact]
    public void Seller_optin_rejects_an_unticked_form()
    {
        var form = new BecomeSellerViewModel { DisplayName = "Ana", AcceptTerms = false };

        var errors = Validate(form);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(BecomeSellerViewModel.AcceptTerms)));
    }

    [Fact]
    public void Business_signup_requires_the_terms()
    {
        var form = new RegisterBusinessViewModel
        {
            BusinessName = "Adriatik Motors",
            RegistrationNumber = "L91234567A",
            Address = "Rruga e Kavajes 120",
            City = "Tirana",
            Country = "Albania",
            ContactName = "Arben Hoxha",
            Email = "dealer@example.com",
            Phone = "+355691234567",
            Username = "adriatik",
            Password = "dealerpass1",
            ConfirmPassword = "dealerpass1",
            AcceptTerms = false
        };

        Assert.Contains(Validate(form),
            e => e.MemberNames.Contains(nameof(RegisterBusinessViewModel.AcceptTerms)));

        form.AcceptTerms = true;
        Assert.Empty(Validate(form));
    }
}
