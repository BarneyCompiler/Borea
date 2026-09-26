namespace Borea.Core.Launch;

public enum GameExitKind
{
    /// <summary>Borea did not see how the launch ended, because it was no launch Borea watched or its handle was released first.</summary>
    Unknown = 0,

    /// <summary>
    /// The process ended and the game recorded no crash of the run. A quit ends
    /// so, and so does a restart that the game or its loader starts by itself,
    /// because both start the new process and exit with 0.
    /// </summary>
    Closed = 1,

    /// <summary>The process ended with an error, and the game recorded a crash of the run.</summary>
    Crashed = 2,
}

/// <summary>How the process of a started launch ended.</summary>
/// <param name="ExitCode">Null when the kind is <see cref="GameExitKind.Unknown"/>.</param>
/// <param name="Output">The last lines the process wrote to its output and error streams, oldest first.</param>
/// <param name="Crash">The crash the game recorded, when the kind is <see cref="GameExitKind.Crashed"/>.</param>
public sealed record GameExit(GameExitKind Kind, int? ExitCode, IReadOnlyList<string> Output, GameCrash? Crash)
{
    public static GameExit Unknown { get; } = new(GameExitKind.Unknown, null, [], null);
}
