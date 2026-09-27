namespace Borea.Core.GitHub;

public enum GitHubSessionStatus
{
    SignedOut,
    WaitingForCode,
    SignedIn,

    /// <summary>Signing in again with the sign-in that Borea kept from an earlier start.</summary>
    Resuming,
}
