using Borea.Core.Game;
using Borea.Core.Launch;

namespace Borea.Storage.Game;

/// <summary>
/// The Linux build of the game ships the app host KSA, and it can ship KSA.exe
/// next to it. A folder that holds the app host is a native build, also when
/// it is in a Wine prefix, so the host starts it and the loader gets host
/// paths. The launchers and the loader configuration use this one rule, so
/// they cannot disagree about a folder.
/// </summary>
internal static class NativeLinuxBuild
{
    public static bool IsIn(string gameDirectory) =>
        File.Exists(Path.Combine(gameDirectory, GameExecutable.FileName(OsPlatform.Linux)!));
}
