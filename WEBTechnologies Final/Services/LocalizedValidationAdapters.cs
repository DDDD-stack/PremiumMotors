using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.DataAnnotations;
using Microsoft.Extensions.Localization;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Makes "The {0} field is required." translatable, which it is not by default.
    ///
    /// MVC runs a validation attribute's message through the localizer ONLY when the
    /// attribute carries an explicit ErrorMessage. Every other attribute on this site has
    /// one, or - like [Phone] and [EmailAddress] - gets its default text localized anyway;
    /// [Required] is the exception, and it is also the most common message on the site. The
    /// result was a form where every message was translated except the one everybody sees.
    ///
    /// Rather than writing the same ErrorMessage out on forty-odd properties (and still
    /// missing the implicit required that non-nullable reference types generate, which has no
    /// attribute in the source to add it to), this fills in the framework's own wording where
    /// there is none. The text is unchanged, so English renders exactly as it did before -
    /// it is now simply a key the translation files can answer.
    /// </summary>
    public sealed class LocalizedValidationAdapterProvider : IValidationAttributeAdapterProvider
    {
        /// <summary>The framework's own default, kept verbatim so it stays a familiar key.</summary>
        public const string RequiredMessage = "The {0} field is required.";

        private readonly IValidationAttributeAdapterProvider _inner =
            new ValidationAttributeAdapterProvider();

        public IAttributeAdapter? GetAttributeAdapter(
            ValidationAttribute attribute, IStringLocalizer? stringLocalizer)
        {
            FillInRequiredMessage(attribute);
            return _inner.GetAttributeAdapter(attribute, stringLocalizer);
        }

        /// <summary>
        /// Attribute instances are cached per property, so this runs once and is idempotent.
        /// An attribute that names its own message, or points at a resource file, is left
        /// exactly as the author wrote it.
        /// </summary>
        public static void FillInRequiredMessage(ValidationAttribute attribute)
        {
            if (attribute is not RequiredAttribute required) return;

            if (string.IsNullOrEmpty(required.ErrorMessage)
                && string.IsNullOrEmpty(required.ErrorMessageResourceName)
                && required.ErrorMessageResourceType is null)
            {
                required.ErrorMessage = RequiredMessage;
            }
        }
    }
}
