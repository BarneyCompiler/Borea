using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Borea.Storage.Tests.Launch;

/// <summary>Runs and stops the LaunchProbeFixture processes of the launch tests.</summary>
internal static class LaunchProbe
{
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    /// <summary>The variable a restart of the probe reads its instance folder from.</summary>
    public const string InstanceVariable = "BOREA_PROBE_INSTANCE";

    /// <summary>The dotnet host that runs this test, found from the shared runtime it loaded.</summary>
    public static string DotnetHost()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    }

    public static string ProbeAssembly() => Path.Combine(AppContext.BaseDirectory, "LaunchProbeFixture.dll");

    /// <summary>What a restart of the probe received, once it runs, or null.</summary>
    public static (string[] Arguments, string? Variable, int ProcessId)? RestartRecord(string instanceRoot)
    {
        var path = Path.Combine(instanceRoot, "restarted.json");
        if (!WaitFor(() => File.Exists(path)))
            return null;

        using var record = JsonDocument.Parse(File.ReadAllText(path));
        var root = record.RootElement;
        return (
            root.GetProperty("Arguments").EnumerateArray().Select(argument => argument.GetString()!).ToArray(),
            root.GetProperty("Variable").GetString(),
            root.GetProperty("ProcessId").GetInt32());
    }

    /// <summary>Creates a file a probe waits for, such as "go" or "stop".</summary>
    public static void Signal(string directory, string name) => File.WriteAllText(Path.Combine(directory, name), string.Empty);

    public static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(50);
        }

        return condition();
    }

    public static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static void StopProbe(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
                process.Kill();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
        }
    }
}
