using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Borea.App.Links;

/// <summary>
/// Writes borea.desktop and the icon under the XDG data folder and makes the entry the default handler through xdg-mime.
/// </summary>
/// <param name="runXdgMime">Runs xdg-mime with the arguments and returns its output, or null when xdg-mime is missing.</param>
internal sealed class LinuxLinkRegistrar(string dataHome, Func<byte[]> icon, Func<IReadOnlyList<string>, string?> runXdgMime) : ILinkRegistrar
{
    internal const string DesktopFileName = "borea.desktop";

    private const string MimeType = "x-scheme-handler/borea";

    internal string DesktopFilePath => LinuxDesktopEntry.FilePath(dataHome, DesktopFileName);

    internal string IconPath => LinuxDesktopEntry.IconPath(dataHome);

    public bool Register(string executablePath)
    {
        var changed = LinuxDesktopEntry.WriteIfDifferent(IconPath, icon());
        changed |= LinuxDesktopEntry.WriteIfDifferent(DesktopFilePath, DesktopEntry(executablePath));

        if (runXdgMime(["query", "default", MimeType]) is { } current && current.Trim() != DesktopFileName)
        {
            runXdgMime(["default", DesktopFileName, MimeType]);
            changed = true;
        }

        return changed;
    }

    public bool Unregister(string executablePath)
    {
        if (!File.Exists(DesktopFilePath) || ExecLine(File.ReadAllText(DesktopFilePath)) != ExecLine(DesktopEntry(executablePath)))
            return false;

        File.Delete(DesktopFilePath);

        // The launcher entry shows the same icon.
        if (!File.Exists(LinuxDesktopEntry.FilePath(dataHome, LinuxLauncher.DesktopFileName)))
            File.Delete(IconPath);
        return true;
    }

    internal string DesktopEntry(string executablePath) => string.Join('\n',
        "[Desktop Entry]",
        "Type=Application",
        "Name=Borea",
        "Comment=Opens borea:// links in Borea",
        $"Exec={LinuxDesktopEntry.ExecProgram(executablePath)} %u",
        $"Icon={LinuxDesktopEntry.Escape(IconPath)}",
        "Terminal=false",
        "NoDisplay=true",
        $"MimeType={MimeType};",
        string.Empty);

    private static string? ExecLine(string entry)
        => entry.Split('\n').FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal))?.TrimEnd('\r');
}
