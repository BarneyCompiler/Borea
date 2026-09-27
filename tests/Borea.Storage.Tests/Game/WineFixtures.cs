using Borea.Core.Game;

namespace Borea.Storage.Tests.Game;

/// <summary>Builds Wine prefixes and wrapper apps in a temp folder.</summary>
internal static class WineFixtures
{
    public const string InfoPlist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleExecutable</key>
            <string>launcher</string>
            <key>CFBundleIdentifier</key>
            <string>com.example.kitten-space-agency</string>
            <key>Program Name and Path</key>
            <string>/Program Files/Kitten Space Agency/KSA.exe</string>
        </dict>
        </plist>
        """;

    /// <summary>The prefix of a wrapper app, without its Info.plist and launcher.</summary>
    public static string WrapperPrefix(string applications, string name = "Kitten Space Agency") =>
        Path.Combine(applications, name + ".app", "Contents", "SharedSupport", "prefix");

    /// <summary>A prefix with an empty dosdevices folder, a system.reg and a drive_c folder.</summary>
    public static string Prefix(string prefix, bool dosdevices = true, bool systemRegistry = true)
    {
        Directory.CreateDirectory(Path.Combine(prefix, "drive_c"));
        if (dosdevices)
            Directory.CreateDirectory(Path.Combine(prefix, "dosdevices"));

        if (systemRegistry)
            File.WriteAllText(Path.Combine(prefix, "system.reg"), "WINE REGISTRY Version 2\n");

        return prefix;
    }

    /// <summary>A dosdevices link, which Windows cannot name. Returns the link, which a test removes before its folder.</summary>
    public static string LinkDrive(string prefix, char letter, string target) =>
        Directory.CreateSymbolicLink(Path.Combine(prefix, "dosdevices", letter + ":"), target).FullName;

    /// <summary>
    /// Removes the links first, so that deleting the folder can never reach
    /// what a link such as z: points to.
    /// </summary>
    public static void Delete(string root, IEnumerable<string> links)
    {
        foreach (var link in links)
            File.Delete(link);

        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    /// <summary>The Info.plist and the launcher of the wrapper around <paramref name="prefix"/>.</summary>
    public static string Wrapper(string prefix, string? infoPlist = InfoPlist, string? launcher = "launcher")
    {
        var contents = Path.GetDirectoryName(Path.GetDirectoryName(prefix))!;
        Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
        if (infoPlist is not null)
            File.WriteAllText(Path.Combine(contents, "Info.plist"), infoPlist);

        if (launcher is not null)
            File.WriteAllText(Path.Combine(contents, "MacOS", launcher), "#!/bin/sh\n");

        return Path.GetDirectoryName(contents)!;
    }

    /// <summary>A map in the host's own form, which the tests on Windows need, because Wine does not run there.</summary>
    public static WineDriveMap Drives(IReadOnlyDictionary<char, string> drives) => new(
        drives,
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
        Path.DirectorySeparatorChar);
}

/// <summary>A probe that finds one prefix by its folder, without links.</summary>
internal sealed class FakeWineProbe(WineInstall install) : IWinePrefixProbe
{
    public List<string> Probed { get; } = [];

    public WineInstall? Find(string directory)
    {
        Probed.Add(directory);
        var full = Path.GetFullPath(directory);
        return full.StartsWith(install.PrefixRoot, StringComparison.OrdinalIgnoreCase) ? install : null;
    }

    public string? ToWindowsPath(WineInstall found, string hostPath) => found.Drives.ToWindows(Path.GetFullPath(hostPath));
}
