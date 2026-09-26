using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Borea.App.Links;

/// <summary>What the desktop entries of Borea share: the XDG data folder, the icon and the rules of the desktop entry spec.</summary>
internal static class LinuxDesktopEntry
{
    /// <summary>Reads $XDG_DATA_HOME and falls back to ~/.local/share, as the XDG base directory spec says.</summary>
    internal static string DataHome(Func<string, string?> environment, string home)
        => environment("XDG_DATA_HOME") is { Length: > 0 } value && Path.IsPathRooted(value) ? value : Path.Combine(home, ".local", "share");

    internal static string FilePath(string dataHome, string fileName) => Path.Combine(dataHome, "applications", fileName);

    internal static string IconPath(string dataHome) => Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", "borea.png");

    /// <summary>The program as the first word of an Exec line.</summary>
    internal static string ExecProgram(string executablePath) => Escape(Quote(executablePath)).Replace("%", "%%", StringComparison.Ordinal);

    /// <summary>Escapes a string value, whose backslash starts an escape sequence.</summary>
    internal static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    internal static bool WriteIfDifferent(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return true;
    }

    internal static bool WriteIfDifferent(string path, string entry) => WriteIfDifferent(path, Encoding.UTF8.GetBytes(entry));

    /// <summary>Quotes an Exec argument, which escapes the characters the desktop entry spec reserves inside quotes.</summary>
    private static string Quote(string argument)
    {
        if (argument.Any(char.IsControl))
            throw new ArgumentException("The program path has a control character, which a desktop entry cannot hold.", nameof(argument));

        var quoted = new StringBuilder("\"");
        foreach (var character in argument)
        {
            if (character is '"' or '`' or '$' or '\\')
                quoted.Append('\\');
            quoted.Append(character);
        }

        return quoted.Append('"').ToString();
    }
}
