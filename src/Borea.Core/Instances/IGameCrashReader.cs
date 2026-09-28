using Borea.Core.Launch;

namespace Borea.Core.Instances;

/// <summary>
/// Reads the crashes the game recorded in an instance, and remembers the ones
/// Borea showed, so each crash is shown once.
/// </summary>
public interface IGameCrashReader
{
    /// <summary>The newest crash the game recorded after <paramref name="since"/> that Borea has not shown, or null.</summary>
    Task<GameCrash?> GetUnreportedAsync(Instance instance, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>Records that Borea showed the crash, so that neither log of its run is shown again.</summary>
    Task MarkReportedAsync(Guid instanceId, GameCrash crash, CancellationToken cancellationToken = default);
}
