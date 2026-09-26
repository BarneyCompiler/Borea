using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;
using Borea.Storage.Launch;
using Borea.Storage.Tests.Paths;
using static Borea.Storage.Tests.Launch.LaunchProbe;

namespace Borea.Storage.Tests.Launch;

/// <summary>
/// Launches of a real loader process that restarts itself the way the game's
/// Restart button does. It starts its own executable without arguments and
/// exits with 0. The probe is no game process, so the launcher sees a game
/// only while a probe runs.
/// </summary>
public sealed class LoaderRestartTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest " + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly Instance _instance = new("Restarting", InstanceSource.Custom.Value);
    private readonly List<int> _probeIds = new();

    public LoaderRestartTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
        Directory.CreateDirectory(InstanceRoot);

        // the probe goes where the test paths keep StarMap, the way a loader is installed
        var loaderDirectory = Path.Combine(_tempRoot, "StarMap");
        Directory.CreateDirectory(loaderDirectory);
        foreach (var name in new[] { "LaunchProbeFixture.dll", "LaunchProbeFixture.runtimeconfig.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, name), Path.Combine(loaderDirectory, name));
    }

    private string InstanceRoot => Path.GetFullPath(_paths.GetInstanceRoot(_instance.InstanceId));

    private LoaderLauncher Launcher(TimeSpan startupWindow) =>
        new(_paths, new ProcessStarter(), SharedProfileLauncher.CurrentPlatform(), DotnetHost, startupWindow, isGameRunning: () => _probeIds.Any(IsAlive));

    private static ModMetadata ProbeLoader() => LoaderLauncherTests.LoaderListing(provides: LoaderLauncherTests.StarMapProvides(
        launch: "LaunchProbeFixture.dll",
        instance: new InstanceHandover("-InstancePath", InstanceVariable),
        platforms: Enum.GetValues<OsPlatform>().ToDictionary(platform => platform, _ => new LoaderPlatformLaunch("LaunchProbeFixture.dll", "dotnet"))));

    /// <summary>Launches the probe, and waits until it restarted itself and exited.</summary>
    private LaunchResult LaunchAndRestart(LoaderLauncher launcher)
    {
        var started = launcher.Launch(_instance, ProbeLoader(), ["restart"]);
        Assert.True(started.Started, started.Message);
        _probeIds.Add(started.ProcessId!.Value);

        var restart = RestartRecord(InstanceRoot);
        Assert.NotNull(restart);
        _probeIds.Add(restart.Value.ProcessId);
        Assert.True(WaitFor(() => !IsAlive(started.ProcessId.Value)), "The loader did not exit after it restarted itself.");
        return started;
    }

    [Fact]
    public void IsRunning_LoaderRestartedItself_StaysTrueUntilTheRestartEnds()
    {
        using var launcher = Launcher(LoaderLauncher.DefaultStartupWindow);
        LaunchAndRestart(launcher);

        Assert.True(launcher.IsRunning(_instance.InstanceId));
        Assert.Equal(LaunchOutcome.AlreadyRunning, launcher.Launch(_instance, ProbeLoader(), ["restart"]).Outcome);

        Signal(InstanceRoot, "go");
        Signal(InstanceRoot, "stop");

        Assert.True(WaitFor(() => !launcher.IsRunning(_instance.InstanceId)), "The launch did not end with its restart.");
    }

    [Fact]
    public async Task WatchStart_LoaderRestartsItselfBeforeTheGameComesUp_StaysStarted()
    {
        using var launcher = Launcher(TimeSpan.FromMilliseconds(500));
        var started = LaunchAndRestart(launcher);

        var result = await launcher.WatchStartAsync(_instance, started);

        Assert.True(result.Started, result.Message);
        Assert.True(launcher.IsRunning(_instance.InstanceId));
        Assert.Contains("restarted itself", File.ReadAllText(_paths.GetInstanceLaunchLogPath(_instance.InstanceId)));
    }

    public void Dispose()
    {
        try
        {
            Signal(InstanceRoot, "go");
            Signal(InstanceRoot, "stop");
        }
        catch (IOException)
        {
        }

        foreach (var id in _probeIds)
            StopProbe(id);

        // a restart that outlived the wait still holds the directory
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
