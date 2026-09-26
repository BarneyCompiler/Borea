using System.Globalization;
using Borea.Core.Listings;

namespace Borea.Network.Tests.Listings;

/// <summary>Reads <c>key = "value"</c> and <c>key = 123</c> lines under <c>[table]</c> headers.</summary>
internal sealed class TableFormat : IListingFormat
{
    public string Write(AuthoredTable document, string? original = null) => throw new NotSupportedException();

    public AuthoredTable Read(string text)
    {
        var root = new AuthoredTable();
        var table = root;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']'))
                    throw new FormatException("An unclosed table header.");

                table = new AuthoredTable();
                root.Set(line[1..^1], table);
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                throw new FormatException("A line without a value.");

            table.Set(parts[0], parts[1].StartsWith('"') ? parts[1].Trim('"')
                : long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number
                : throw new FormatException("A bare word."));
        }

        return root;
    }
}
