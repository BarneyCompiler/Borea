namespace Borea.Core.Game;

/// <summary>
/// The platform whose code the game build runs. The installed mods and the
/// start entry of a loader follow it, because the Windows build runs Windows
/// code also where Wine runs it on Linux or macOS.
/// </summary>
public interface IGamePlatform
{
    /// <summary>
    /// Windows when the game folder of a Linux or macOS host holds the Windows
    /// build of the game, and else the host.
    /// </summary>
    OsPlatform Current { get; }
}
