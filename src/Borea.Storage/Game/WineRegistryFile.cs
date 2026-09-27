using System.Globalization;
using System.Text;

namespace Borea.Storage.Game;

/// <summary>
/// Reads the string values of registry keys from a Wine registry file such as
/// system.reg, the text Wine keeps a hive in. It never writes the file.
/// </summary>
internal static class WineRegistryFile
{
    private const string Header = "WINE REGISTRY Version 2";

    /// <summary>
    /// The string values of every key directly below
    /// <paramref name="parentKey"/>, by key name. Names compare without case,
    /// as the registry compares them. Empty when the file is missing or is not
    /// a Wine registry file.
    /// </summary>
    /// <param name="parentKey">The key path relative to the root of the hive, with backslashes.</param>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ReadSubKeys(string path, string parentKey)
    {
        var keys = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return keys;

        using var reader = new StreamReader(path, Encoding.UTF8);
        if (reader.ReadLine()?.TrimEnd() != Header)
            return keys;

        Dictionary<string, string>? values = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
            {
                var index = 1;
                values = null;
                if (Unescape(line, ref index, ']') is { } key && ChildName(key, parentKey) is { } child)
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    keys[child] = values;
                }
            }
            else if (values is not null && line.StartsWith('"'))
            {
                var index = 1;
                if (Unescape(line, ref index, '"') is not { } name
                    || index + 1 >= line.Length
                    || line[index] != '='
                    || line[index + 1] != '"')
                {
                    continue;
                }

                index += 2;
                if (Unescape(line, ref index, '"') is { } value)
                    values[name] = value;
            }
        }

        return keys;
    }

    private static string? ChildName(string key, string parentKey)
    {
        if (key.Length <= parentKey.Length + 1
            || !key.StartsWith(parentKey, StringComparison.OrdinalIgnoreCase)
            || key[parentKey.Length] != '\\')
        {
            return null;
        }

        var child = key[(parentKey.Length + 1)..];
        return child.Contains('\\') ? null : child;
    }

    /// <summary>
    /// Reads a quoted name or value up to <paramref name="end"/>, with the
    /// escapes Wine writes: C escapes, \x and up to four hex digits, and up
    /// to three octal digits. Null when the text does not end.
    /// </summary>
    private static string? Unescape(string line, ref int index, char end)
    {
        var text = new StringBuilder();
        while (index < line.Length)
        {
            var character = line[index++];
            if (character == end)
                return text.ToString();

            if (character != '\\')
            {
                text.Append(character);
                continue;
            }

            if (index >= line.Length)
                return null;

            character = line[index++];
            switch (character)
            {
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 'e': text.Append('\u001b'); break;
                case 'f': text.Append('\f'); break;
                case 'n': text.Append('\n'); break;
                case 'r': text.Append('\r'); break;
                case 't': text.Append('\t'); break;
                case 'v': text.Append('\v'); break;
                case 'x':
                    var hex = Digits(line, index, 4, IsHexDigit);
                    if (hex == 0)
                    {
                        text.Append('x');
                        break;
                    }

                    text.Append((char)int.Parse(line.AsSpan(index, hex), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    index += hex;
                    break;
                case >= '0' and <= '7':
                    var octal = 1 + Digits(line, index, 2, IsOctalDigit);
                    text.Append((char)Convert.ToInt32(line.Substring(index - 1, octal), 8));
                    index += octal - 1;
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }

        return null;
    }

    private static int Digits(string line, int start, int max, Func<char, bool> isDigit)
    {
        var count = 0;
        while (count < max && start + count < line.Length && isDigit(line[start + count]))
            count++;

        return count;
    }

    private static bool IsHexDigit(char character) => char.IsAsciiHexDigit(character);

    private static bool IsOctalDigit(char character) => character is >= '0' and <= '7';
}
