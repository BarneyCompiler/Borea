using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Paths;
using Borea.Storage.Launch;

namespace Borea.Storage.Instances;

/// <summary>
/// Reads the crash logs of the game in the logs folder of an instance. The runs
/// Borea showed are kept in a file there too, one per line, so the record goes
/// away with the logs, and a run whose crash logs are gone leaves it.
/// </summary>
public sealed class FileGameCrashReader : IGameCrashReader
{
    internal const string ReportedFileName = "borea-crash-shown.txt";

    private readonly IGamePathProvider _pathProvider;

    public FileGameCrashReader(IGamePathProvider pathProvider)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
    }

    public Task<GameCrash?> GetUnreportedAsync(Instance instance, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var gameLog = _pathProvider.GetInstanceGameLogPath(instance.InstanceId);
        var modsFolder = _pathProvider.GetInstanceModsFolder(instance.InstanceId);
        return Task.Run(
            () =>
            {
                var reported = ReadReported(gameLog);
                var log = GameLogFiles.FindCrashLogs(gameLog)
                    .Where(log => log.File.LastWriteTimeUtc > since.UtcDateTime && !reported.Contains(log.Run))
                    .MaxBy(log => log.File.LastWriteTimeUtc);
                return log is null ? null : GameCrashLogs.Read(log, instance, modsFolder);
            },
            cancellationToken);
    }

    public Task MarkReportedAsync(Guid instanceId, GameCrash crash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(crash);

        var gameLog = _pathProvider.GetInstanceGameLogPath(instanceId);
        var path = ReportedPath(gameLog);
        return Task.Run(
            () =>
            {
                try
                {
                    var runs = GameLogFiles.FindCrashLogs(gameLog).Select(log => log.Run).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var reported = ReadReported(gameLog).Where(runs.Contains).Append(crash.Run).Distinct(StringComparer.OrdinalIgnoreCase);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllLines(path, reported);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // without the record the crash is shown once more at the next start, which is no harm
                }
            },
            cancellationToken);
    }

    private static HashSet<string> ReadReported(string gameLog)
    {
        try
        {
            return File.ReadAllLines(ReportedPath(gameLog))
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string ReportedPath(string gameLog) => Path.Combine(Path.GetDirectoryName(gameLog)!, ReportedFileName);
}
