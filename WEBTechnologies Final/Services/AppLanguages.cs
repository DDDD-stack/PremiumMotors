using System.Globalization;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// The languages the site is offered in. Adding one is adding an entry here and a
    /// Resources/SharedResource.&lt;code&gt;.resx file; nothing else enumerates languages.
    /// </summary>
    public static class AppLanguages
    {
        /// <summary>
        /// What the picker offers, in the order it offers them. Region-specific because a
        /// stored preference should say which Albanian or which English, and because the date
        /// formats differ between English regions in ways that matter (01/02 is not the same
        /// day in London as in New York).
        /// </summary>
        public static readonly string[] Offered = { "en-GB", "sq-AL", "it-IT" };

        /// <summary>
        /// Each language named in itself. "Albanian" is no use to somebody who cannot read
        /// the English word for their own language, which is exactly who the picker is for.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Names =
            new Dictionary<string, string>
            {
                ["en-GB"] = "English",
                ["sq-AL"] = "Shqip",
                ["it-IT"] = "Italiano"
            };

        /// <summary>
        /// Everything the request pipeline will accept, which is more than the picker shows.
        ///
        /// Browsers send whatever they like: "sq", "sq-AL", "sq-MK", "it", "en-US". A
        /// supported list of only "sq-AL" matches a request for "sq-AL" and misses a plain
        /// "sq" - the framework narrows a request to a supported parent, never widens it to a
        /// supported child - so an Albanian browser would silently land in English. Listing
        /// the neutral form alongside each region closes that.
        /// </summary>
        public static IEnumerable<CultureInfo> Supported() =>
            Offered
                .SelectMany(name => new[] { name, name.Split('-')[0] })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(Build);

        /// <summary>
        /// A culture that speaks its own language but counts money the same as every other.
        ///
        /// Every price on the site is euro, and the three locales disagree about how to write
        /// one - 43,700.00 against 43.700,00. A buyer who switches language mid-session and
        /// sees the same car at a differently-punctuated price has to stop and work out
        /// whether the price changed. So the number format is en-GB everywhere and only the
        /// DATE format follows the language.
        /// </summary>
        public static CultureInfo Build(string name)
        {
            var culture = new CultureInfo(name);

            var euro = (NumberFormatInfo)new CultureInfo("en-GB").NumberFormat.Clone();
            euro.CurrencySymbol = "€";
            culture.NumberFormat = euro;

            return culture;
        }
    }
}
