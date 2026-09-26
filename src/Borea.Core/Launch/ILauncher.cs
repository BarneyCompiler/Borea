using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Launch;

/// <summary>
/// Starts the game for one instance through its mod loader.
/// </summary>
public interface ILauncher
{
    /// <summary>
    /// Starts the loader with the instance handover, then the saved
    /// <see cref="Instance.LaunchArguments"/>, then <paramref name="arguments"/>
    /// for this launch only.
    /// </summary>
    LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null);

    /// <summary>
    /// Watches a launch that <see cref="Launch"/> just started until the game
    /// comes up, the loader stops, or the startup window ends. A loader that
    /// stops with a non-zero exit code in that time turns the result into
    /// <see cref="LaunchOutcome.ExitedEarly"/>, with its output and the mod it
    /// likely stopped on. Any other result is returned as it is.
    /// </summary>
    Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits until the process of a launch that <see cref="WatchStartAsync"/>
    /// reported as started ends, and says how it ended. Only a process that
    /// ends with an error can be <see cref="GameExitKind.Crashed"/>, and only
    /// with a crash log of its own run, so a quit and a restart that exit with
    /// 0 are <see cref="GameExitKind.Closed"/>, and so is the crash of a new
    /// process that a restart started.
    /// </summary>
    Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a launch this launcher started for the instance is still
    /// running. A loader that restarts itself keeps its launch running while
    /// the new process runs. A launch from an earlier session and a game
    /// started by hand are not seen.
    /// </summary>
    bool IsRunning(Guid instanceId);
}
