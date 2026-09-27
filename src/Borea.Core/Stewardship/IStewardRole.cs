namespace Borea.Core.Stewardship;

/// <summary>
/// Whether the signed-in GitHub account is a steward of content-index or content-index-releases.
/// It only decides what Borea shows, because GitHub enforces the rights. It is never saved, and every change of the session drops it.
/// </summary>
public interface IStewardRole
{
    /// <summary>The result of the check since the last sign-in, or null while signed out or before that check ends.</summary>
    StewardAccess? Current { get; }

    /// <summary>Raised on any thread when <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Reads the ruleset of main of both repositories for the signed-in account. Calls during a check share it.
    /// A failure counts as no steward. Null when signed out, or when the session changed during the check.
    /// </summary>
    Task<StewardAccess?> CheckAsync(CancellationToken cancellationToken = default);
}
