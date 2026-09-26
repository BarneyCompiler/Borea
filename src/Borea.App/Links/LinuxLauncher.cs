using System;

namespace Borea.App.Links;

/// <summary>
/// Writes borea-app.desktop, the entry that puts Borea in the application menu, so that a player can start
/// the App without a terminal. The borea:// handler entry stays apart, because it follows its own switch.
/// </summary>
internal sealed class LinuxLauncher(string dataHome, Func<byte[]> icon)
{
    internal const string DesktopFileName = "borea-app.desktop";

    internal string DesktopFilePath => LinuxDesktopEntry.FilePath(dataHome, DesktopFileName);

    /// <summary>The window class that Avalonia gives the window on X11, so that the desktop puts the window under this entry.</summary>
    internal static string WindowClass => typeof(LinuxLauncher).Assembly.GetName().Name!;

    /// <returns>True when something was written. Nothing is written when the entry is already current.</returns>
    public bool Write(string executablePath)
    {
        var entry = DesktopEntry(executablePath);
        var changed = LinuxDesktopEntry.WriteIfDifferent(LinuxDesktopEntry.IconPath(dataHome), icon());
        changed |= LinuxDesktopEntry.WriteIfDifferent(DesktopFilePath, entry);
        return changed;
    }

    /// <summary>TryExec hides the entry once the program file is gone, for example after the Borea folder was deleted.</summary>
    internal string DesktopEntry(string executablePath) => string.Join('\n',
        "[Desktop Entry]",
        "Type=Application",
        "Name=Borea",
        "GenericName=Content manager",
        "Comment=Manages mods and mod packs for Kitten Space Agency",
        $"Exec={LinuxDesktopEntry.ExecProgram(executablePath)}",
        $"TryExec={LinuxDesktopEntry.Escape(executablePath)}",
        $"Icon={LinuxDesktopEntry.Escape(LinuxDesktopEntry.IconPath(dataHome))}",
        "Terminal=false",
        "Categories=Game;",
        $"StartupWMClass={WindowClass}",
        string.Empty);
}
