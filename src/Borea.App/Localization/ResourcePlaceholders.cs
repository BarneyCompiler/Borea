using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Borea.App.Localization;

/// <summary>
/// A translated text must keep the placeholders of its English text and the same number of braces,
/// so the string.Format call that fills the English text fills the translation too.
/// </summary>
internal static partial class ResourcePlaceholders
{
    public static bool Match(string english, string translation)
        => Placeholders(english).SequenceEqual(Placeholders(translation))
            && Braces(english) == Braces(translation);

    private static string[] Placeholders(string text)
        => PlaceholderPattern().Matches(text).Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    private static (int Open, int Close) Braces(string text)
        => (text.Count(character => character == '{'), text.Count(character => character == '}'));

    [GeneratedRegex(@"\{\d+(?:,-?\d+)?(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
