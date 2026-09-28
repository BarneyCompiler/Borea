using System.Security;
using System.Xml;
using System.Xml.Linq;
using Borea.Core.Game;
using Borea.Storage.Launch;

namespace Borea.Storage.Game;

/// <summary>
/// Recognizes a Wine prefix by the dosdevices folder and the system.reg file
/// that Wine creates in every prefix, and reads its drives from the links in
/// dosdevices. It only reads, and it finds nothing on Windows.
/// </summary>
public sealed class WinePrefixProbe : IWinePrefixProbe
{
    // The limit of links Linux follows in one path lookup.
    private const int MaxLinks = 40;

    private const long MaxInfoPlistBytes = 1024 * 1024;

    private const string ProgramKey = "Program Name and Path";

    private readonly OsPlatform? _platform;

    public WinePrefixProbe()
        : this(SharedProfileLauncher.CurrentPlatform())
    {
    }

    /// <param name="platform">The host. Only Linux and macOS are probed.</param>
    public WinePrefixProbe(OsPlatform? platform)
    {
        _platform = platform;
    }

    public WineInstall? Find(string directory)
    {
        if (_platform is not (OsPlatform.Linux or OsPlatform.MacOs) || string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            return null;

        try
        {
            for (var folder = ResolveLinks(directory); folder is not null; folder = Path.GetDirectoryName(folder))
            {
                if (Directory.Exists(Path.Combine(folder, "dosdevices")) && File.Exists(Path.Combine(folder, "system.reg")))
                    return new WineInstall(folder, ReadDrives(folder), FindWrapper(folder));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            // a folder Borea cannot read is not recognized as a prefix
        }

        return null;
    }

    public string? ToWindowsPath(WineInstall install, string hostPath)
    {
        ArgumentNullException.ThrowIfNull(install);
        if (string.IsNullOrWhiteSpace(hostPath) || !Path.IsPathFullyQualified(hostPath))
            return null;

        try
        {
            return ResolveLinks(hostPath) is { } resolved ? install.Drives.ToWindows(resolved) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return null;
        }
    }

    private WineDriveMap ReadDrives(string prefix)
    {
        var dosdevices = Path.Combine(prefix, "dosdevices");
        var drives = new Dictionary<char, string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(dosdevices))
        {
            // "c::" and the like are the raw devices of the drives
            var name = Path.GetFileName(entry);
            if (name.Length != 2 || name[1] != ':' || !char.IsAsciiLetter(name[0]))
                continue;

            if (LinkTarget(entry) is not { } target || ResolveLinks(Path.GetFullPath(target, dosdevices)) is not { } root)
                continue;

            if (Directory.Exists(root))
                drives[char.ToLowerInvariant(name[0])] = root;
        }

        var comparison = _platform == OsPlatform.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return new WineDriveMap(drives, comparison, Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// The wrapper whose prefix is <c>&lt;name&gt;.app/Contents/SharedSupport/prefix</c>,
    /// when its Info.plist is an XML plist that names the Windows program and
    /// a launcher in Contents/MacOS that is there.
    /// </summary>
    private static WineWrapper? FindWrapper(string prefix)
    {
        var sharedSupport = Path.GetDirectoryName(prefix);
        var contents = Path.GetDirectoryName(sharedSupport);
        var bundle = Path.GetDirectoryName(contents);
        if (Path.GetFileName(prefix) != "prefix"
            || Path.GetFileName(sharedSupport) != "SharedSupport"
            || Path.GetFileName(contents) != "Contents"
            || bundle is null
            || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var info = ReadInfoPlist(Path.Combine(contents!, "Info.plist"));
        if (info is null || !info.ContainsKey(ProgramKey) || !info.TryGetValue("CFBundleExecutable", out var executable))
            return null;

        if (string.IsNullOrWhiteSpace(executable) || Path.GetFileName(executable) != executable || executable is "." or "..")
            return null;

        var launcher = Path.Combine(contents!, "MacOS", executable);
        return File.Exists(launcher) ? new WineWrapper(bundle, launcher) : null;
    }

    /// <summary>
    /// The string values of the top dictionary of an XML property list, or
    /// null when the file is missing, too large, binary or not a plist.
    /// </summary>
    private static Dictionary<string, string>? ReadInfoPlist(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > MaxInfoPlistBytes)
            return null;

        XDocument document;
        try
        {
            using var stream = file.OpenRead();
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
            using var reader = XmlReader.Create(stream, settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }

        if (document.Root is not { Name.LocalName: "plist" } root || root.Element("dict") is not { } dictionary)
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? key = null;
        foreach (var element in dictionary.Elements())
        {
            if (element.Name.LocalName == "key")
            {
                key = element.Value;
                continue;
            }

            if (key is not null && element.Name.LocalName == "string")
                values.TryAdd(key, element.Value);

            key = null;
        }

        return values;
    }

    /// <summary>
    /// The absolute path with every link on it resolved, as Wine compares
    /// paths, or null when the links form a loop.
    /// </summary>
    private static string? ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        var pending = new Stack<string>(Parts(full).Reverse());
        var followed = 0;
        while (pending.TryPop(out var part))
        {
            var next = Path.Combine(current, part);
            if (LinkTarget(next) is not { } target)
            {
                current = next;
                continue;
            }

            if (++followed > MaxLinks)
                return null;

            var resolved = Path.GetFullPath(target, current);
            current = Path.GetPathRoot(resolved)!;
            foreach (var targetPart in Parts(resolved).Reverse())
                pending.Push(targetPart);
        }

        return current;
    }

    private static IEnumerable<string> Parts(string fullPath) =>
        fullPath[Path.GetPathRoot(fullPath)!.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

    private static string? LinkTarget(string path)
    {
        try
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return entry.LinkTarget;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
