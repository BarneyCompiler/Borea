using System.Diagnostics;
using Borea.Core.Updates;

namespace Borea.Storage.Updates;

/// <summary>
/// Starts the new build as a process of its own. Its streams are not redirected, because the
/// process that starts it ends right after and a pipe into an ended process would break.
/// </summary>
public sealed class HandoverStarter : IHandoverStarter
{
    public void Start(string programPath, IReadOnlyList<string> arguments)
    {
        using var started = Process.Start(StartInfo(programPath, arguments))
            ?? throw new InvalidOperationException($"No process was started for '{programPath}'.");
    }

    /// <summary>
    /// A new build that takes over from the App gets its console without a window, which it then frees, so that
    /// Windows versions that ignore the console allocation policy show no console. A command keeps its terminal.
    /// </summary>
    internal static ProcessStartInfo StartInfo(string programPath, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programPath);
        ArgumentNullException.ThrowIfNull(arguments);

        var handover = arguments.ToArray();
        var startInfo = new ProcessStartInfo
        {
            FileName = programPath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(programPath)),
            UseShellExecute = false,
            CreateNoWindow = SelfUpdateHandover.Take(ref handover) is { FromCommandLine: false },
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }
}
