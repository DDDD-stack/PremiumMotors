using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// The English text of an enum's [Display(Name)], which is what the translation files are
    /// keyed on.
    ///
    /// Without this a view renders the C# identifier - "PluginHybrid", "SemiAutomatic" - and
    /// those are neither readable nor translatable. Reading the attribute keeps one English
    /// wording next to the value it describes, instead of a second copy in every view.
    /// </summary>
    public static class EnumText
    {
        public static string Display(Enum value)
        {
            var name = value.ToString();

            var display = value.GetType()
                .GetField(name, BindingFlags.Public | BindingFlags.Static)
                ?.GetCustomAttribute<DisplayAttribute>()
                ?.Name;

            return string.IsNullOrWhiteSpace(display) ? name : display;
        }
    }
}
