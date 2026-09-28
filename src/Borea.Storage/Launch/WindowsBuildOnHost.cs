using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Game;

namespace Borea.Storage.Launch;

/// <summary>
/// The Windows build of the game in the game folder of a Linux or macOS host.
/// Neither the host dotnet nor the game's own executable can start it there.
/// The launchers start it through the launcher of the Wine wrapper app around
/// it on macOS, and refuse it with what Borea recognized around it elsewhere.
/// </summary>
internal static class WindowsBuildOnHost
{
    /// <summary>Whether the game folder of a Linux or macOS host holds the Windows build and no native Linux build.</summary>
    public static bool IsIn(OsPlatform? platform, string? gameDirectory)
    {
        if (platform is not (OsPlatform.Linux or OsPlatform.MacOs)
            || string.IsNullOrWhiteSpace(gameDirectory)
            || !Path.IsPathFullyQualified(gameDirectory))
        {
            return false;
        }

        var directory = Path.GetFullPath(gameDirectory);
        return File.Exists(Path.Combine(directory, GameExecutable.FileName(OsPlatform.Windows)!)) && !NativeLinuxBuild.IsIn(directory);
    }

    /// <summary>How the launchers start the game folder of the host.</summary>
    public static HostStart Check(OsPlatform? platform, string? gameDirectory, IWinePrefixProbe wine)
    {
        if (!IsIn(platform, gameDirectory))
            return default;

        var install = wine.Find(Path.GetFullPath(gameDirectory!));
        if (platform == OsPlatform.MacOs && install?.Wrapper is not null)
            return new HostStart(install, Refusal: null);

        // only macOS runs the launcher of a wrapper app, so elsewhere its prefix is a plain one
        var prefix = install is null ? null : install with { Wrapper = null };
        var message = prefix is null
            ? "The game folder holds the Windows build of KSA, which this system can run only through Wine, and Borea found no Wine prefix around it. Borea starts nothing."
            : $"The game folder holds the Windows build of KSA in the Wine prefix '{prefix.PrefixRoot}'. Borea cannot start a game through Wine yet, so it starts nothing. Start the game through Wine to play.";
        return new HostStart(prefix, message);
    }

    public static string OutsidePrefix(WineInstall install, string path) =>
        $"No drive of the Wine prefix '{install.PrefixRoot}' holds '{path}', so the programs in the prefix cannot open it. Borea starts nothing. Add a drive for this folder in the Wine configuration of the prefix.";
}

/// <param name="Wine">The Wine prefix around the Windows build, or null when there is no Windows build or no prefix around it.</param>
/// <param name="Refusal">Why the launchers start nothing, or null when the launch may go on.</param>
internal readonly record struct HostStart(WineInstall? Wine, string? Refusal)
{
    /// <summary>The install whose wrapper starts the game, or null when the host starts it.</summary>
    public WineInstall? Wrapped => Refusal is null ? Wine : null;
}
