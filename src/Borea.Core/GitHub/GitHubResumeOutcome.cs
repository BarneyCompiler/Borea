namespace Borea.Core.GitHub;

public enum GitHubResumeOutcome
{
    /// <summary>Nothing was kept, staying signed in is off, no secret store works, or a sign-in already ran.</summary>
    NothingKept,

    SignedIn,

    /// <summary>GitHub refused the kept sign-in, for example because it expired or was revoked, so Borea deleted it.</summary>
    Refused,

    /// <summary>GitHub could not be reached or gave no usable answer, so the kept sign-in stays for the next start.</summary>
    Unreachable,
}
