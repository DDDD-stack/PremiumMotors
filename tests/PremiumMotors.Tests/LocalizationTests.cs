using System.Collections;
using System.Globalization;
using System.Resources;
using WEBTechnologies_Final;
using WEBTechnologies_Final.Services;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Translation has one failure mode and it is silent: the lookup misses, the English key is
/// returned, the page renders perfectly, and nobody notices until an Albanian visitor asks
/// why half the site is in English. Nothing throws and nothing is logged.
///
/// This actually happened while building it. The assembly is named "WEBTechnologies Final",
/// with a space, so it cannot be the namespace; the localizer builds its lookup name from the
/// assembly name unless told otherwise, and every single string missed. These tests are here
/// so that comes back as a red build rather than as a support email.
/// </summary>
public class LocalizationTests
{
    private static readonly ResourceManager Resources =
        new("WEBTechnologies_Final.Resources.SharedResource", typeof(SharedResource).Assembly);

    /// <summary>A string that must exist in every language for the site to be usable.</summary>
    private const string SampleKey = "Cars for sale";

    [Theory]
    [InlineData("sq-AL")]
    [InlineData("it-IT")]
    public void Every_offered_language_actually_resolves_its_resources(string culture)
    {
        var translated = Resources.GetString(SampleKey, new CultureInfo(culture));

        Assert.False(string.IsNullOrWhiteSpace(translated),
            $"No resources found for {culture}. This is the RootNamespace failure: check " +
            "the RootNamespace property in the csproj and the assembly attribute in " +
            "SharedResource.cs.");

        Assert.NotEqual(SampleKey, translated);
    }

    [Fact]
    public void The_neutral_form_of_each_language_resolves_too()
    {
        // Browsers send bare language codes at least as often as regional ones. If "sq"
        // misses while "sq-AL" works, most Albanian visitors get an English site.
        //
        // English is excluded on purpose: there is no English resource file, because the keys
        // ARE the English text. Asking the ResourceManager for it would walk up to the
        // invariant culture and throw, which is exactly why the app goes through
        // IStringLocalizer - that returns the key instead of throwing.
        foreach (var code in TranslatedLanguages())
        {
            var translated = Resources.GetString(SampleKey, new CultureInfo(code));
            Assert.False(string.IsNullOrWhiteSpace(translated), $"Nothing resolved for '{code}'.");
            Assert.NotEqual(SampleKey, translated);
        }
    }

    [Fact]
    public void The_languages_translate_the_same_set_of_strings()
    {
        // A key present in one language and missing from another is a page that is half
        // translated in exactly one language, which is the hardest kind of gap to notice.
        var byCulture = TranslatedLanguages().ToDictionary(c => c, KeysFor);

        var first = byCulture.First();
        foreach (var (culture, keys) in byCulture.Skip(1))
        {
            var missing = first.Value.Except(keys).ToList();
            var extra = keys.Except(first.Value).ToList();

            Assert.True(missing.Count == 0,
                $"{culture} is missing: {string.Join(" | ", missing)}");
            Assert.True(extra.Count == 0,
                $"{culture} has strings {first.Key} does not: {string.Join(" | ", extra)}");
        }
    }

    [Fact]
    public void No_translation_is_left_as_the_untranslated_english()
    {
        // A key copied into a resx without being translated looks finished and is not. The
        // exceptions are the words that are genuinely the same in every language.
        // Borrowed words, abbreviations and body-type names that genuinely do not change.
        // Kept as an explicit list rather than a rule, so adding one is a decision somebody
        // made rather than a translation nobody got round to.
        var sameInEveryLanguage = new HashSet<string>
        {
            "Admin", "CVT", "Chat", "Diesel", "Draft", "Email", "Hatchback", "Manual",
            "Max", "Min", "Model", "Privacy", "SUV", "Sedan", "VIN",

            // A number and a unit symbol. "km" is km in all three.
            "{0} km"
        };

        foreach (var culture in TranslatedLanguages())
        {
            foreach (DictionaryEntry entry in ResourceSetFor(culture))
            {
                var key = (string)entry.Key;
                var value = entry.Value as string;

                if (sameInEveryLanguage.Contains(key)) continue;

                Assert.False(key == value,
                    $"{culture}: \"{key}\" is still the English text.");
            }
        }
    }

    [Fact]
    public void No_two_keys_differ_only_by_capitalisation()
    {
        // .resx resource names are matched CASE-INSENSITIVELY. "Cars for sale" and
        // "cars for sale" are the same name, so having both makes the compiler drop one with
        // only a build warning - and the dropped one then renders in English forever. This
        // bit exactly three strings when the landing pages were translated.
        foreach (var culture in TranslatedLanguages())
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in ResourceSetFor(culture))
            {
                var key = (string)entry.Key;
                Assert.False(seen.TryGetValue(key, out var clash),
                    $"{culture}: \"{key}\" and \"{clash}\" are the same resource name.");
                seen[key] = key;
            }
        }
    }

    /// <summary>
    /// The languages that have a resource file: everything offered except English, whose
    /// translations are the keys themselves.
    /// </summary>
    private static IEnumerable<string> TranslatedLanguages() =>
        AppLanguages.Offered
            .Select(c => c.Split('-')[0])
            .Where(c => !string.Equals(c, "en", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resource sets are keyed on the culture the .resx is named for - "sq", not "sq-AL" -
    /// and tryParents has to stay false, because the parent of any of these is the invariant
    /// culture, which has no resource file and throws rather than returning nothing.
    /// </summary>
    private static ResourceSet ResourceSetFor(string language)
    {
        var set = Resources.GetResourceSet(new CultureInfo(language), true, false);
        Assert.True(set is not null, $"No resource file compiled for '{language}'.");
        return set!;
    }

    [Fact]
    public void Money_is_written_the_same_way_in_every_language()
    {
        // The point of AppLanguages.Build. Italian would otherwise render 43.700,00 while
        // English renders 43,700.00, and the same car would appear to change price when a
        // visitor switches language.
        var rendered = AppLanguages.Offered
            .Select(c => 43700m.ToString("C", AppLanguages.Build(c)))
            .Distinct()
            .ToList();

        Assert.Single(rendered);
        Assert.Contains("€", rendered[0]);
        Assert.Contains("43,700", rendered[0]);
    }

    [Fact]
    public void Every_offered_language_has_a_display_name_in_its_own_language()
    {
        foreach (var code in AppLanguages.Offered)
            Assert.True(AppLanguages.Names.ContainsKey(code),
                $"{code} is offered in the picker with no name to show for it.");
    }

    private static HashSet<string> KeysFor(string language)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in ResourceSetFor(language)) keys.Add((string)entry.Key);
        return keys;
    }
}
