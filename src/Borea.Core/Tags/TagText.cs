using System.Text.RegularExpressions;

namespace Borea.Core.Tags;

/// <summary>
/// The forms of a tag. A tag is stored as lowercase words joined by - (RFC 0031), so a typed entry
/// is turned into that form, and a free-form tag is shown as words with a capital first letter.
/// Case changes use the invariant culture, so the result is the same in every display language.
/// </summary>
public static partial class TagText
{
    /// <summary>
    /// The stored form of a typed entry, which is lowercase with each run of spaces and _ turned into one - and no - at the start or the end.
    /// The result can still be an invalid tag, which <see cref="IsValid"/> tells.
    /// </summary>
    public static string Normalize(string entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Separators().Replace(entry.ToLowerInvariant(), "-").Trim('-');
    }

    /// <summary>Whether the text is a tag in its stored form, the tag pattern of the authored schema.</summary>
    public static bool IsValid(string? tag) => tag is not null && StoredForm().IsMatch(tag);

    /// <summary>A stored tag as players read it, "space-station" as "Space Station".</summary>
    public static string Display(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var words = tag.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? tag : string.Join(' ', words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z")]
    private static partial Regex StoredForm();
}
