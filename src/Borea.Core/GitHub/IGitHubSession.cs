using Borea.Core.Secrets;

namespace Borea.Core.GitHub;

/// <summary>
/// The user's GitHub sign-in through the device flow. The access token stays in memory,
/// is never shown, and goes only to https://api.github.com. Only the refresh token can outlive Borea,
/// in the secret store of the system and while <see cref="KeepSignedIn"/> is on.
/// </summary>
public interface IGitHubSession
{
    /// <summary>False when this build of Borea has no GitHub App to sign in with.</summary>
    bool IsAvailable { get; }

    /// <summary>The GitHub page where the user revokes Borea's access.</summary>
    string ManageAccessUrl { get; }

    /// <summary>The GitHub page where the user installs the App on a repository.</summary>
    string InstallUrl { get; }

    /// <summary>The same page with the repository <paramref name="repositoryId"/> already selected, while signed in.</summary>
    string InstallUrlFor(long repositoryId);

    GitHubSessionState State { get; }

    /// <summary>Raised on any thread when <see cref="State"/> changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Asks GitHub for a code, reports it, and waits until the user confirmed or refused it.
    /// Throws <see cref="OperationCanceledException"/> when cancelled or signed out meanwhile,
    /// and <see cref="InvalidOperationException"/> when unavailable, already signed in, or a sign-in already runs.
    /// </summary>
    /// <param name="keepSignedIn">False keeps this sign-in in memory only, even while <see cref="KeepSignedIn"/> is on, and deletes a sign-in that an earlier start kept.</param>
    Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the sign-in outlives Borea, which keeps the refresh token in the secret store of the system. Off by default.
    /// Off deletes the kept token and leaves the session in memory until Borea closes, and so does the first assignment of off,
    /// which removes a token that an earlier start could not delete. On keeps the token of the current session.
    /// </summary>
    bool KeepSignedIn { get; set; }

    /// <summary>Why no sign-in can be kept on this computer, or null while no secret store call failed.</summary>
    SecretStoreProblem? KeepSignedInProblem { get; }

    /// <summary>Raised on any thread when <see cref="KeepSignedInProblem"/> changes.</summary>
    event EventHandler? KeepSignedInProblemChanged;

    /// <summary>
    /// Signs in with the refresh token that an earlier start kept, without the device flow, while <see cref="KeepSignedIn"/> is on.
    /// A token that GitHub refuses is deleted. Throws <see cref="OperationCanceledException"/> when cancelled or signed out meanwhile.
    /// </summary>
    Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the token, deletes a kept one, and stops a running sign-in. The grant stays on GitHub until the user revokes it.
    /// </summary>
    void SignOut();

    /// <summary>
    /// Sends <paramref name="request"/> with the token. Only https://api.github.com is accepted.
    /// A 401 answer to the token signs the session out. Throws <see cref="InvalidOperationException"/> when signed out.
    /// </summary>
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
