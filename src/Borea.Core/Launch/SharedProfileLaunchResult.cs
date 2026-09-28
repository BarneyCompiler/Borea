using Borea.Core.Game;

namespace Borea.Core.Launch;

public sealed class SharedProfileLaunchResult
{
    public SharedProfileLaunchOutcome Outcome { get; }

    /// <summary>What happened and what to do about it, for the user.</summary>
    public string Message { get; }

    /// <summary>What was run, or would have been run. Null when no plan was built.</summary>
    public LaunchPlan? Plan { get; }

    /// <summary>Null unless the launch started.</summary>
    public int? ProcessId { get; }

    public bool Started => Outcome == SharedProfileLaunchOutcome.Started;

    /// <summary>The Wine prefix around a Windows build that stopped the launch, or null when Borea found none.</summary>
    public WineInstall? Wine { get; private init; }

    /// <summary>The host path that no drive of the Wine prefix holds, when that stopped the launch.</summary>
    public string? UnmappedPath { get; private init; }

    private SharedProfileLaunchResult(SharedProfileLaunchOutcome outcome, string message, LaunchPlan? plan, int? processId)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A launch result needs a message for the user.", nameof(message));

        Outcome = outcome;
        Message = message;
        Plan = plan;
        ProcessId = processId;
    }

    public static SharedProfileLaunchResult Success(LaunchPlan plan, int processId, string message)
    {
        if (plan is null)
            throw new ArgumentNullException(nameof(plan));

        return new SharedProfileLaunchResult(SharedProfileLaunchOutcome.Started, message, plan, processId);
    }

    public static SharedProfileLaunchResult Failed(SharedProfileLaunchOutcome outcome, string message, LaunchPlan? plan = null, WineInstall? wine = null, string? unmappedPath = null)
    {
        if (outcome == SharedProfileLaunchOutcome.Started)
            throw new ArgumentException("A started launch is a success, not a failure.", nameof(outcome));

        return new SharedProfileLaunchResult(outcome, message, plan, processId: null) { Wine = wine, UnmappedPath = unmappedPath };
    }
}
