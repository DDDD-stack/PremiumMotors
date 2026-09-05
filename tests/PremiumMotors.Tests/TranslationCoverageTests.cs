using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using WEBTechnologies_Final;
using WEBTechnologies_Final.Services;
using Xunit;

namespace PremiumMotors.Tests;

/// <summary>
/// Every user-facing string the source asks for by name must exist in every language.
///
/// This is the test that turns the one failure mode translation actually has into a red
/// build. A missing key does not throw and is not logged: the localizer returns the English
/// key, the page renders perfectly, and an Albanian visitor gets an English sentence in the
/// middle of an Albanian page. Nobody notices until somebody complains.
///
/// It reads the source rather than the compiled app on purpose - the whole point is to catch
/// a string that was added to a view and never added to the resx.
/// </summary>
public class TranslationCoverageTests
{
    private static readonly ResourceManager Resources =
        new("WEBTechnologies_Final.Resources.SharedResource", typeof(SharedResource).Assembly);

    /// <summary>
    /// Where a translatable literal is written: views, controllers, the model-binder
    /// messages in Program.cs, the service layer's error sentences, and model metadata.
    /// Anything computed - L[label], _text[result.Error], L[EnumText.Display(x)] - is
    /// skipped, because its key is written somewhere else and gets picked up there.
    /// </summary>
    private static readonly Regex[] Literals =
    {
        new(@"\bL\[\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        new(@"\b_?text\[\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        new(@"\bFail\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        new(@"\bDisplay\(Name = ""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        new(@"\bPrompt = ""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),
        new(@"\bErrorMessage = ""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled)
    };

    [Theory]
    [InlineData("sq")]
    [InlineData("it")]
    public void Every_literal_the_source_looks_up_is_translated(string language)
    {
        var translated = KeysIn(language);

        var missing = SourceKeys()
            .Where(key => !translated.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} string(s) have no {language} translation and will render in " +
            $"English on an otherwise translated page:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", missing.Take(40)));
    }

    [Fact]
    public void Every_placeholder_survives_translation()
    {
        // "{0} km" translated as "km" drops the number and the sentence says nothing. A
        // FormatException would be better than this, but string.Format simply ignores an
        // argument nobody asked for, so the page renders and the value is gone.
        foreach (var language in new[] { "sq", "it" })
        {
            foreach (DictionaryEntry entry in ResourceSetFor(language))
            {
                var key = (string)entry.Key;
                var value = entry.Value as string ?? "";

                Assert.True(Placeholders(key).SetEquals(Placeholders(value)),
                    $"{language}: \"{key}\" has placeholders {Show(key)} but its translation " +
                    $"\"{value}\" has {Show(value)}.");
            }
        }
    }

    private static SortedSet<string> Placeholders(string text) =>
        new(Regex.Matches(text, @"\{\d+\}").Select(m => m.Value), StringComparer.Ordinal);

    private static string Show(string text)
    {
        var found = Placeholders(text);
        return found.Count == 0 ? "none" : string.Join(", ", found);
    }

    private static IEnumerable<string> SourceKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(ProjectDirectory(), "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)) continue;

            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var text = File.ReadAllText(file);
            foreach (var pattern in Literals)
                foreach (Match match in pattern.Matches(text))
                    keys.Add(match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\"));
        }

        Assert.True(keys.Count > 500,
            $"Only found {keys.Count} translatable strings in the source. That is too few to " +
            "be right - the project directory search has probably broken.");

        return keys;
    }

    /// <summary>
    /// Walks up from the test assembly to the repository, then into the web project. Written
    /// as a search rather than a fixed number of "..", so moving the test project one folder
    /// does not silently turn this into a test that scans nothing.
    /// </summary>
    private static string ProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "WEBTechnologies Final");
            if (Directory.Exists(Path.Combine(candidate, "Views"))) return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find the web project from " + AppContext.BaseDirectory);
    }

    private static HashSet<string> KeysIn(string language)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in ResourceSetFor(language)) keys.Add((string)entry.Key);
        return keys;
    }

    private static ResourceSet ResourceSetFor(string language)
    {
        var set = Resources.GetResourceSet(new CultureInfo(language), true, false);
        Assert.True(set is not null, $"No resource file compiled for '{language}'.");
        return set!;
    }
}
