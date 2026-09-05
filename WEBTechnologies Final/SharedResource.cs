using Microsoft.Extensions.Localization;

// The assembly name contains a space and so cannot be the namespace. IStringLocalizer
// derives its resource lookup name from the assembly name unless this says otherwise, and
// the failure mode is silent: every translation simply misses and English is served.
[assembly: RootNamespace("WEBTechnologies_Final")]

namespace WEBTechnologies_Final
{
    /// <summary>
    /// Marker type for the site's one shared translation file. It has no members; its only
    /// job is to name Resources/SharedResource.&lt;culture&gt;.resx for IStringLocalizer.
    ///
    /// ONE FILE PER LANGUAGE, NOT ONE PER VIEW. Per-view resource files are the framework
    /// default and they duplicate every shared string - "Log in", "Price", "Cancel" - across
    /// a dozen files, so a translator fixes a wording in one place and misses six. With one
    /// file the same English sentence has exactly one translation, and adding the fourth
    /// language is adding one file rather than forty.
    ///
    /// THE KEY IS THE ENGLISH TEXT. That is deliberate: a key nobody has translated yet
    /// renders as readable English rather than as "Cars_Index_Heading", so a half-finished
    /// language degrades into a mixed-language page instead of a broken one. The cost is that
    /// changing the English text orphans its translations - grep the resx files when you
    /// reword something.
    /// </summary>
    public class SharedResource
    {
    }
}
