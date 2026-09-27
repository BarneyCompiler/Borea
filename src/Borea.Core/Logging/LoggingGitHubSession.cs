using System.Net;
using Borea.Core.GitHub;
using Borea.Core.Secrets;

namespace Borea.Core.Logging;

public sealed class LoggingGitHubSession : IGitHubSession
{
    private readonly IBoreaLog _log;

    public IGitHubSession Inner { get; }

    public LoggingGitHubSession(IGitHubSession inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsAvailable => Inner.IsAvailable;

    public string ManageAccessUrl => Inner.ManageAccessUrl;

    public string InstallUrl => Inner.InstallUrl;

    public string InstallUrlFor(long repositoryId) => Inner.InstallUrlFor(repositoryId);

    public GitHubSessionState State => Inner.State;

    public event EventHandler? StateChanged
    {
        add => Inner.StateChanged += value;
        remove => Inner.StateChanged -= value;
    }

    public bool KeepSignedIn
    {
        get => Inner.KeepSignedIn;
        set => Inner.KeepSignedIn = value;
    }

    public SecretStoreProblem? KeepSignedInProblem => Inner.KeepSignedInProblem;

    public event EventHandler? KeepSignedInProblemChanged
    {
        add => Inner.KeepSignedInProblemChanged += value;
        remove => Inner.KeepSignedInProblemChanged -= value;
    }

    public async Task<GitHubResumeOutcome> ResumeAsync(CancellationToken cancellationToken = default)
    {
        var outcome = await Inner.ResumeAsync(cancellationToken).ConfigureAwait(false);
        switch (outcome)
        {
            case GitHubResumeOutcome.SignedIn:
                _log.Write($"Signed in to GitHub as {Inner.State.Login} with the sign-in kept on this computer.");
                break;
            case GitHubResumeOutcome.Refused:
                _log.Write("GitHub refused the sign-in kept on this computer, so Borea deleted it.");
                break;
            case GitHubResumeOutcome.Unreachable:
                _log.Write("Cannot reach GitHub to resume the sign-in kept on this computer. It stays for the next start.");
                break;
        }

        if (Inner.KeepSignedInProblem is { } problem)
            _log.Write($"Cannot keep the GitHub sign-in on this computer, {problem}.");

        return outcome;
    }

    public async Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, bool keepSignedIn = true, CancellationToken cancellationToken = default)
    {
        var result = await Inner.SignInAsync(progress, keepSignedIn, cancellationToken).ConfigureAwait(false);
        _log.Write(result.SignedIn
            ? $"Signed in to GitHub as {result.Login}."
            : $"GitHub sign-in failed, {result.Outcome}.");
        return result;
    }

    public void SignOut()
    {
        var wasSignedIn = Inner.State.Status == GitHubSessionStatus.SignedIn;
        Inner.SignOut();
        if (wasSignedIn)
            _log.Write("Signed out of GitHub.");
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var wasSignedIn = Inner.State.Status == GitHubSessionStatus.SignedIn;
        var response = await Inner.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (wasSignedIn && response.StatusCode == HttpStatusCode.Unauthorized && Inner.State.Status != GitHubSessionStatus.SignedIn)
            _log.Write("Signed out of GitHub, because GitHub refused the token.");

        return response;
    }
}
