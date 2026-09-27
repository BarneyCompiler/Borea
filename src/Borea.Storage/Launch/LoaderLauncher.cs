using System.ComponentModel;
using System.Diagnostics;
using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Core.Paths;
using Borea.Storage.Game;
using Borea.Storage.Instances;

namespace Borea.Storage.Launch;

/// <summary>
/// ILauncher over the configured loader directories and a process starter.
/// It remembers every launch per instance until the process has ended, which
/// includes a restart that took over its streams, so a second launch of a
/// running instance is refused, and then until the next launch of the
/// instance, so its exit watch still finds it.
/// </summary>
public sealed class LoaderLauncher : ILauncher, IDisposable
{
    private readonly IGamePathProvider _pathProvider;
    private readonly IProcessStarter _starter;
    private readonly OsPlatform? _platform;
    private readonly Func<string?> _findDotnet;
    private readonly Func<bool> _isGameRunning;
    private readonly IWinePrefixProbe _wine;
    private readonly object _gate;
    private readonly Dictionary<Guid, IStartedProcess> _running;
    private readonly Dictionary<Guid, (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName)> _starts;
    private readonly Dictionary<Guid, (IStartedProcess Process, DateTime? CrashLogAtLaunch)> _ended;
    private readonly bool _ownsLaunches;
    private readonly TimeSpan _startupWindow;
    private readonly TimeSpan _crashLogWait;

    /// <summary>How long a launch is watched when the game does not write its log first.</summary>
    public static readonly TimeSpan DefaultStartupWindow = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long an exit watch waits for the crash log of a process that ended
    /// with an error. The monitor process of the game writes it only after it
    /// saw the game end.
    /// </summary>
    public static readonly TimeSpan DefaultCrashLogWait = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter)
        : this(pathProvider, starter, DefaultStartupWindow)
    {
    }

    /// <param name="startupWindow">How long <see cref="WatchStartAsync"/> watches at most.</param>
    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, TimeSpan startupWindow)
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()), startupWindow)
    {
    }

    /// <param name="launches">The launches this launcher shares with others. Disposing the launcher keeps them.</param>
    /// <param name="isGameRunning">Whether a KSA or StarMap process runs. Null looks for one.</param>
    public LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, RunningLaunches launches, Func<bool>? isGameRunning = null)
        : this(pathProvider, starter, SharedProfileLauncher.CurrentPlatform(), () => DotnetHost.Find(SharedProfileLauncher.CurrentPlatform()), DefaultStartupWindow, launches ?? throw new ArgumentNullException(nameof(launches)), isGameRunning)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet)
        : this(pathProvider, starter, platform, findDotnet, DefaultStartupWindow)
    {
    }

    internal LoaderLauncher(IGamePathProvider pathProvider, IProcessStarter starter, OsPlatform? platform, Func<string?> findDotnet, TimeSpan startupWindow, RunningLaunches? launches = null, Func<bool>? isGameRunning = null, TimeSpan? crashLogWait = null, IWinePrefixProbe? wine = null)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _starter = starter ?? throw new ArgumentNullException(nameof(starter));
        _platform = platform;
        _findDotnet = findDotnet ?? throw new ArgumentNullException(nameof(findDotnet));
        _isGameRunning = isGameRunning ?? RunningProcesses.IsGameRunning;
        _wine = wine ?? new WinePrefixProbe(platform);
        if (startupWindow < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(startupWindow), "The startup window cannot be negative.");

        _startupWindow = startupWindow;
        _crashLogWait = crashLogWait ?? DefaultCrashLogWait;
        _ownsLaunches = launches is null;
        launches ??= new RunningLaunches();
        _gate = launches.Gate;
        _running = launches.Processes;
        _starts = launches.Starts;
        _ended = launches.Ended;
    }

    public LaunchResult Launch(Instance instance, ModMetadata? loader, IReadOnlyList<string>? arguments = null)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        if (loader is null)
        {
            return LaunchResult.Failed(
                LaunchOutcome.NoLoader,
                "No mod loader is set for this launch. The game reads no instance path on its own, so install a loader first.");
        }

        if (loader.Type != ContentType.ModLoader)
            throw new ArgumentException("Only a mod loader can start the game.", nameof(loader));

        lock (_gate)
        {
            Forget(ended: true);

            if (_running.ContainsKey(instance.InstanceId))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.AlreadyRunning,
                    $"Instance '{instance.Name}' is already running from a launch Borea started. Close the game first.");
            }

            if (WindowsBuildOnHost.Refusal(_platform, _pathProvider.GetGameDirectoryPath(), _wine) is { } refusal)
                return LaunchResult.Failed(LaunchOutcome.WindowsBuild, refusal.Message, wine: refusal.Wine);

            // An entry for this platform replaces [provides].launch, with no fallback (RFC 0067).
            var entry = _platform is { } platform ? loader.Provides?.Platforms.GetValueOrDefault(platform) : null;
            if (entry?.UnknownKeys is [var unknownKey, ..])
            {
                return LaunchResult.Failed(
                    LaunchOutcome.UnknownPlatformKey,
                    $"The start entry for this system in the listing of {loader.Name} has the key '{unknownKey}', which Borea does not know. Borea starts nothing. Look for a Borea update.",
                    unknownName: unknownKey);
            }

            if (entry?.Runtime == LoaderRuntime.Unknown)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.UnknownRuntime,
                    $"{loader.Name} runs through '{entry.RuntimeName}' on this system, which Borea does not know. Borea starts nothing. Look for a Borea update.",
                    unknownName: entry.RuntimeName);
            }

            var launch = entry?.Launch ?? loader.Provides?.Launch;
            if (launch is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLaunchTarget,
                    $"The listing of {loader.Name} does not say what to run, so Borea cannot start it.");
            }

            // The launcher uses the table of the listing its caller passes and
            // keeps no copy. The caller passes the live listing, because a
            // release file never carries the table and a stale copy could name
            // a flag the installed loader no longer reads.
            var handover = loader.Provides?.Instance;
            if (handover is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoInstanceHandover,
                    $"The listing of {loader.Name} does not say how it takes an instance, so Borea cannot start one with it. The loader author can add a [provides.instance] table to the listing.");
            }

            var launchArguments = instance.LaunchArguments.Concat(arguments ?? Array.Empty<string>()).ToList();
            if (handover.FlagIn(launchArguments) is { } flag)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.HandoverFlagInArguments,
                    $"{loader.Name} takes the instance folder after '{flag}', and Borea passes that on every launch. A second one could make {loader.Name} use another folder, so remove '{flag}' from the launch arguments.");
            }

            var loaderDirectory = _pathProvider.GetLoaderDirectoryPath(loader.ModId);
            if (loaderDirectory is null)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.NoLoaderDirectory,
                    $"Borea does not know where {loader.Name} is installed. Set its directory in the settings.");
            }

            var plan = LaunchPlan.ForLoader(
                Path.GetFullPath(loaderDirectory),
                launch,
                handover,
                Path.GetFullPath(_pathProvider.GetInstanceRoot(instance.InstanceId)),
                launchArguments);

            if (!File.Exists(plan.Executable))
            {
                return LaunchResult.Failed(
                    LaunchOutcome.LaunchTargetMissing,
                    $"'{plan.Executable}' is not there. Reinstall {loader.Name} or correct its directory in the settings.",
                    plan);
            }

            if (entry?.Runtime == LoaderRuntime.Dotnet)
            {
                var host = _findDotnet();
                if (host is null)
                {
                    return LaunchResult.Failed(
                        LaunchOutcome.DotnetMissing,
                        $"{loader.Name} runs through dotnet on this system, and Borea did not find dotnet. Install the .NET runtime that {loader.Name} needs and try again.");
                }

                plan = plan.ThroughHost(host, plan.Executable);
            }

            IStartedProcess process;
            try
            {
                process = _starter.Start(plan);
            }
            catch (Win32Exception exception)
            {
                return LaunchResult.Failed(
                    LaunchOutcome.StartFailed,
                    $"The system did not start '{plan.Executable}': {exception.Message}",
                    plan);
            }

            var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
            _running[instance.InstanceId] = process;
            _starts[instance.InstanceId] = (LastWrite(gameLog), LastCrashLogWrite(gameLog), loader.Name);
            _ended.Remove(instance.InstanceId);

            return LaunchResult.Success(
                plan,
                process.Id,
                $"Started {loader.Name} for instance '{instance.Name}'.");
        }
    }

    public async Task<LaunchResult> WatchStartAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(started);
        if (!started.Started || started.Plan is null)
            return started;

        IStartedProcess? process;
        (DateTime? GameLogAtLaunch, DateTime? CrashLogAtLaunch, string LoaderName) start;
        lock (_gate)
        {
            if (!_running.TryGetValue(instance.InstanceId, out process) || process.Id != started.ProcessId || !_starts.TryGetValue(instance.InstanceId, out start))
                return started;
        }

        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var ended = false;
        var gameStarted = false;
        int? exitCode;
        try
        {
            var watched = Stopwatch.StartNew();
            while (watched.Elapsed < _startupWindow)
            {
                var slice = Min(PollInterval, _startupWindow - watched.Elapsed);
                if (!ended)
                    ended = await process.WaitForExitAsync(slice, cancellationToken).ConfigureAwait(false);
                else
                    await Task.Delay(slice, cancellationToken).ConfigureAwait(false);

                // the game writes its log once it runs, so the loader got past loading the mods
                if (WrittenSince(gameLog, start.GameLogAtLaunch))
                {
                    gameStarted = true;
                    break;
                }

                // a loader that exits with 0 may have restarted itself, so the log decides;
                // an error exit needs no more waiting
                if (ended && process.ExitCode is not 0)
                    break;
            }

            exitCode = process.HasExited ? process.ExitCode : null;

            // the streams of a process that exited alone close a moment later
            if (exitCode is not null && !ended)
                ended = await process.WaitForExitAsync(PollInterval, cancellationToken).ConfigureAwait(false);

            gameStarted = gameStarted || WrittenSince(gameLog, start.GameLogAtLaunch);
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // the handle was released by another call while this one watched
            return started;
        }

        // a loader that exits with 0 and leaves a game process that holds its streams has restarted itself
        var restarted = exitCode == 0 && !ended && _isGameRunning();
        var output = process.RecentOutput;
        WriteLaunchLog(_pathProvider.GetInstanceLaunchLogPath(instance.InstanceId), started.Plan, output, exitCode, restarted, _platform == OsPlatform.Windows);

        if (exitCode is null || (exitCode == 0 && (gameStarted || restarted)))
            return started.WithOutput(output);

        // StarMap also exits with 0 when it cannot start at all, for example without a game path
        if (exitCode == 0)
        {
            return LaunchResult.ExitedEarly(
                started.Plan,
                0,
                output,
                blamedModId: null,
                $"{start.LoaderName} stopped without starting the game. The details show what it wrote.");
        }

        var blamed = ModBlame.Find(instance, _pathProvider.GetInstanceModsFolder(instance.InstanceId), LoaderCrashReport.AssemblyNames(output));
        var cause = blamed is null ? LoaderCrashCause.Unknown : LoaderCrashCause.ModAssembly;
        if (blamed is null && LoaderCrashReport.StoppedWhileLoadingMods(output))
        {
            cause = LoaderCrashCause.ModLoading;
            var loadOrder = await LoadOrderReader.ReadAsync(_pathProvider, instance.InstanceId, cancellationToken).ConfigureAwait(false);
            var likely = LoaderCrashReport.LikelyLoadingMod(output, loadOrder);
            blamed = likely is null ? null : instance.Mods.FirstOrDefault(mod => ModIds.Equals(mod.ModId, likely));
        }

        var message = (blamed, cause) switch
        {
            (null, _) => $"{start.LoaderName} stopped right after starting (exit code {exitCode}). The details show what it wrote.",
            (_, LoaderCrashCause.ModLoading) => $"{start.LoaderName} stopped while it loaded {blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version}, so that mod is the likely cause. Disable it and try again, or look for an update.",
            _ => $"{blamed.Metadata.Listing?.Name ?? blamed.ModId} {blamed.Version} stopped {start.LoaderName} from starting. It may not work with this version of KSA. Disable it and try again, or look for an update.",
        };
        return LaunchResult.ExitedEarly(started.Plan, exitCode.Value, output, blamed?.ModId, message, cause);
    }

    public async Task<GameExit> WatchExitAsync(Instance instance, LaunchResult started, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(started);
        if (!started.Started || started.ProcessId is not { } processId)
            return GameExit.Unknown;

        IStartedProcess process;
        DateTime? crashLogAtLaunch;
        lock (_gate)
        {
            if (_running.TryGetValue(instance.InstanceId, out var running) && running.Id == processId && _starts.TryGetValue(instance.InstanceId, out var start))
                (process, crashLogAtLaunch) = (running, start.CrashLogAtLaunch);
            else if (_ended.TryGetValue(instance.InstanceId, out var ended) && ended.Process.Id == processId)
                (process, crashLogAtLaunch) = ended;
            else
                return GameExit.Unknown;
        }

        int? endedWith;
        try
        {
            await process.WaitForExitAsync(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            endedWith = process.ExitCode;
        }
        catch (Exception exception) when (exception is ObjectDisposedException or InvalidOperationException)
        {
            // a released handle keeps the exit code of a process that had ended
            endedWith = process.ExitCode;
        }

        if (endedWith is not { } exitCode)
            return GameExit.Unknown;

        // a quit and a restart exit with 0, and neither is a crash
        var output = process.RecentOutput;
        var crash = exitCode == 0 ? null : await FindCrashAsync(instance, processId, crashLogAtLaunch, cancellationToken).ConfigureAwait(false);
        return new GameExit(crash is null ? GameExitKind.Closed : GameExitKind.Crashed, exitCode, output, crash);
    }

    /// <summary>
    /// The crash log that the game wrote for the process after the launch. The
    /// log names the process, so the crash of another run, such as the new
    /// process of a restart, is never taken for this one.
    /// </summary>
    private async Task<GameCrash?> FindCrashAsync(Instance instance, int processId, DateTime? crashLogAtLaunch, CancellationToken cancellationToken)
    {
        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instance.InstanceId);
        var watched = Stopwatch.StartNew();
        while (true)
        {
            var log = GameLogFiles.FindCrashLogs(gameLog)
                .Where(log => log.ProcessId == processId && (crashLogAtLaunch is null || log.File.LastWriteTimeUtc > crashLogAtLaunch))
                .MaxBy(log => log.File.LastWriteTimeUtc);
            if (log is not null && GameCrashLogs.Read(log, instance, modsFolder) is { } crash)
                return crash;

            if (watched.Elapsed >= _crashLogWait)
                return null;

            await Task.Delay(Min(PollInterval, _crashLogWait - watched.Elapsed), cancellationToken).ConfigureAwait(false);
        }
    }

    private static DateTime? LastCrashLogWrite(string gameLogPath) => GameLogFiles.FindCrashLogs(gameLogPath)
        .Select(log => (DateTime?)log.File.LastWriteTimeUtc)
        .Max();

    /// <summary>
    /// Whether a session log appeared or changed after the launch. The files'
    /// own times are compared, because the file system clock is coarser than
    /// DateTime.UtcNow and a fresh write can look older than the launch.
    /// </summary>
    private static bool WrittenSince(string gameLogPath, DateTime? atLaunch) =>
        LastWrite(gameLogPath) is { } now && (atLaunch is null || now > atLaunch);

    private static DateTime? LastWrite(string gameLogPath) => GameLogFiles.Find(gameLogPath)
        .Where(log => log.Kind != GameLogKind.Archive)
        .Select(log => (DateTime?)log.File.LastWriteTimeUtc)
        .Max();

    /// <summary>
    /// What the loader wrote while it was watched, next to the game's log, so
    /// the player can open it later. Each launch replaces the file.
    /// </summary>
    private static void WriteLaunchLog(string path, LaunchPlan plan, IReadOnlyList<string> output, int? exitCode, bool restarted, bool windows)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var header = new[]
            {
                $"Launch at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                $"Executable: {plan.Executable}",
                (exitCode, restarted) switch
                {
                    (null, _) => "The loader was still running when Borea stopped watching.",
                    (_, true) => "The loader restarted itself. Its new process was still running when Borea stopped watching.",
                    ({ } code, _) => $"The loader exited with code {LoaderExitCode.Describe(code, windows)}.",
                },
                string.Empty,
            };
            File.WriteAllLines(path, header.Concat(output));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // the log is a convenience, a launch result does not depend on it
        }
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    public bool IsRunning(Guid instanceId)
    {
        lock (_gate)
        {
            Forget(ended: true);
            return _running.ContainsKey(instanceId);
        }
    }

    /// <summary>Releases the handles, unless the launches are shared. The processes keep running.</summary>
    public void Dispose()
    {
        if (!_ownsLaunches)
            return;

        lock (_gate)
        {
            Forget(ended: false);
        }
    }

    /// <summary>
    /// Whether a launch has ended. After the loader exited, a process that
    /// still holds its streams keeps the launch only while a game process
    /// runs, because then that process is the restart. A browser or a file
    /// manager that the game opened before it closed does not keep it.
    /// </summary>
    private bool HasEnded(IStartedProcess process) => process.HasEnded || (process.HasExited && !_isGameRunning());

    /// <summary>Drops the launches that ended, or every launch, and releases their handles.</summary>
    private void Forget(bool ended)
    {
        if (!ended)
            _ended.Clear();

        foreach (var (instanceId, process) in _running.ToArray())
        {
            if (ended && !HasEnded(process))
                continue;

            if (ended)
                _ended[instanceId] = (process, _starts.GetValueOrDefault(instanceId).CrashLogAtLaunch);
            _running.Remove(instanceId);
            _starts.Remove(instanceId);
            process.Dispose();
        }
    }
}
