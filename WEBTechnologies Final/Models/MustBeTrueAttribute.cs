using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// Validates that a required checkbox is ticked.
    ///
    /// This replaces [Range(typeof(bool), "true", "true")], a widely copied idiom that fails on
    /// the CLIENT in a way that looks like the checkbox is being ignored. The mechanism, from
    /// jquery.validate.js:
    ///
    ///   range: value >= param[0] && value <= param[1]
    ///
    /// RangeAttribute writes its bounds with Boolean.ToString(), which yields "True", while the
    /// checkbox tag helper renders value="true". For a ticked box jQuery compares the strings
    /// "true" >= "True" (true, lowercase sorts after uppercase) and "true" &lt;= "True" (false),
    /// so the rule FAILS. For an unticked box elementValue() returns undefined, optional()
    /// short-circuits, and the rule PASSES.
    ///
    /// The result is exactly backwards: ticking the box blocked submission, leaving it alone
    /// did not. Server-side RangeAttribute is fine, which is why this was invisible in tests
    /// that only exercised the model.
    ///
    /// "required" is the rule jQuery already implements correctly for a checkbox, so that is
    /// what this emits.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public sealed class MustBeTrueAttribute : ValidationAttribute, IClientModelValidator
    {
        public MustBeTrueAttribute()
            : base("You must accept this to continue.") { }

        public override bool IsValid(object? value) => value is bool b && b;

        public void AddValidation(ClientModelValidationContext context)
        {
            var message = FormatErrorMessage(context.ModelMetadata.GetDisplayName());

            context.Attributes.TryAdd("data-val", "true");
            context.Attributes.TryAdd("data-val-required", message);
        }
    }
}
