namespace Borea.Storage.Launch;

/// <summary>
/// The launches that still run, shared by every <see cref="LoaderLauncher"/>
/// built with this object, so a launcher built after a settings change still
/// knows the games an earlier one started.
/// </summary>
public sealed class RunningLaunches
{
    internal object Gate { get; } = new();

    internal Dictionary<Guid, IStartedProcess> Processes { get; } = new();

    internal Dictionary<Guid, (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName)> Starts { get; } = new();

    /// <summary>
    /// The last launch per instance that ended, until the next launch of the
    /// instance, so an exit watch that starts after the end still finds it.
    /// </summary>
    internal Dictionary<Guid, (IStartedProcess Process, DateTime? CrashLogAtLaunch)> Ended { get; } = new();
}
