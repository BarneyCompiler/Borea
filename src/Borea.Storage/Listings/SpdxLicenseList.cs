using System.Text.Json;
using System.Text.RegularExpressions;
using Borea.Core.Licenses;

namespace Borea.Storage.Listings;

/// <summary>
/// Resolves SPDX license expressions against the SPDX License List that ships with Borea, with the rules of
/// check_license.py of content-index: identifiers compare case-insensitively, and LicenseRef- names a license of your own.
/// </summary>
public sealed partial class SpdxLicenseList
{
    public const string ListUrl = "https://spdx.org/licenses/";

    private static readonly Lazy<SpdxLicenseList> EmbeddedList = new(Load);

    private readonly HashSet<string> _licenses;
    private readonly HashSet<string> _exceptions;

    private SpdxLicenseList(string version, IEnumerable<string> licenses, IEnumerable<string> exceptions)
    {
        Version = version;
        _licenses = new HashSet<string>(licenses, StringComparer.OrdinalIgnoreCase);
        _exceptions = new HashSet<string>(exceptions, StringComparer.OrdinalIgnoreCase);
    }

    public static SpdxLicenseList Embedded => EmbeddedList.Value;

    /// <summary>The version of the SPDX License List, such as 3.29.0.</summary>
    public string Version { get; }

    public bool IsLicense(string id) => _licenses.Contains(id);

    /// <summary>What is wrong with the expression, in the words of the checks. Empty when nothing is.</summary>
    public IReadOnlyList<string> Problems(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Trim().Length == 0)
            return [];

        var depth = 0;
        foreach (var character in expression)
        {
            depth += character switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth < 0)
                break;
        }

        if (depth != 0)
            return [$"'{expression}' has unbalanced parentheses"];

        if (SpdxExpression.Parse(expression) is not { } parsed)
        {
            return [$"'{expression}' does not parse as an SPDX license expression; join several licenses with AND or OR, "
                + "such as GPL-2.0-only AND CC-BY-SA-4.0"];
        }

        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var license in parsed.Licenses)
        {
            if (!IsLicense(license.Id) && !IsReference(license.Id))
                unknown.Add(license.Id);
            if (license.Exception is { } exception && !_exceptions.Contains(exception) && !IsReference(exception))
                unknown.Add(exception);
        }

        return unknown.Count == 0
            ? []
            : [$"'{expression}' names {string.Join(", ", unknown)}, which is not on the SPDX license list; the identifiers are at {ListUrl}"];
    }

    private static SpdxLicenseList Load()
    {
        using var stream = typeof(SpdxLicenseList).Assembly.GetManifestResourceStream("Borea.Storage.Listings.spdx-licenses.json")
            ?? throw new InvalidOperationException("The SPDX license list is missing from this build.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        return new SpdxLicenseList(
            root.GetProperty("licenseListVersion").GetString()!,
            root.GetProperty("licenses").EnumerateArray().Select(item => item.GetString()!),
            root.GetProperty("exceptions").EnumerateArray().Select(item => item.GetString()!));
    }

    private static bool IsReference(string id) => LicenseReference().IsMatch(id);

    [GeneratedRegex(@"^(?:DocumentRef-[A-Za-z0-9.-]+:)?LicenseRef-[A-Za-z0-9.-]+$")]
    private static partial Regex LicenseReference();
}
