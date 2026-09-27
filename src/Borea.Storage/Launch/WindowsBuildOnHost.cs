using Borea.Core.Game;
using Borea.Core.Launch;
using Borea.Storage.Game;

namespace Borea.Storage.Launch;

/// <summary>
/// The Windows build of the game in the game folder of a Linux or macOS host.
/// Neither the host dotnet nor the game's own executable can start it there,
/// so the launchers refuse it with what Borea recognized around it.
/// </summary>
internal static class WindowsBuildOnHost
{
    /// <summary>Null when the launch may go on.</summary>
    public static (string Message, WineInstall? Wine)? Refusal(OsPlatform? platform, string? gameDirectory, IWinePrefixProbe wine)
    {
        if (platform is not (OsPlatform.Linux or OsPlatform.MacOs)
            || string.IsNullOrWhiteSpace(gameDirectory)
            || !Path.IsPathFullyQualified(gameDirectory))
        {
            return null;
        }

        var directory = Path.GetFullPath(gameDirectory);
        if (!File.Exists(Path.Combine(directory, GameExecutable.FileName(OsPlatform.Windows)!))
            || NativeLinuxBuild.IsIn(directory))
        {
            return null;
        }

        var install = wine.Find(directory);
        var message = install switch
        {
            { Wrapper: { } wrapper } =>
                $"The game folder holds the Windows build of KSA in the Wine wrapper '{wrapper.BundlePath}'. Borea cannot start a game through a Wine wrapper yet, so it starts nothing. Start the wrapper to play.",
            { } prefix =>
                $"The game folder holds the Windows build of KSA in the Wine prefix '{prefix.PrefixRoot}'. Borea cannot start a game through Wine yet, so it starts nothing. Start the game through Wine to play.",
            null =>
                "The game folder holds the Windows build of KSA, which this system can run only through Wine, and Borea found no Wine prefix around it. Borea starts nothing.",
        };
        return (message, install);
    }
}
