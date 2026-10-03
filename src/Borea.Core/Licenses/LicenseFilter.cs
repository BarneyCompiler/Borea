namespace Borea.Core.Licenses;

/// <summary>
/// The License filter of Discover. Its options are the licenses that the license expressions name, and an expression
/// matches when it names one of the chosen licenses, the way the Category filter matches one of the chosen categories.
/// A text that does not parse as an expression counts as one license.
/// </summary>
public static class LicenseFilter
{
    /// <summary>Each license once, the one that the most expressions name first.</summary>
    public static IReadOnlyList<string> Options(IEnumerable<string> expressions)
    {
        ArgumentNullException.ThrowIfNull(expressions);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var expression in expressions)
        {
            foreach (var license in Licenses(expression))
                counts[license] = counts.GetValueOrDefault(license) + 1;
        }

        return counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry.Key)
            .ToList();
    }

    /// <summary>Whether the expression names one of the chosen licenses. With none chosen, every expression matches.</summary>
    public static bool Matches(string expression, IEnumerable<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(chosen);
        var licenses = chosen.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (licenses.Count == 0)
            return true;

        return Licenses(expression).Any(licenses.Contains);
    }

    private static IEnumerable<string> Licenses(string expression)
    {
        if (SpdxExpression.Parse(expression) is { } parsed)
            return parsed.Licenses.Select(license => license.Name).Distinct(StringComparer.OrdinalIgnoreCase);

        var text = expression.Trim();
        return text.Length == 0 ? [] : [text];
    }
}
