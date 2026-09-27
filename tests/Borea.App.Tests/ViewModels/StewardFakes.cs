using System.Net;
using System.Text;
using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

/// <summary>Keeps the steward changes in memory: each opened change is an open pull request until it merges, and the others then conflict.</summary>
internal sealed class FakeIndexStatusEditor : IIndexStatusEditor
{
    public List<IndexStatusEntry> Entries { get; } = [];

    public List<IndexStatusPullRequest> Pulls { get; } = [];

    public List<IndexStatusChange> Checked { get; } = [];

    public List<IndexStatusChange> Opened { get; } = [];

    public int Reads { get; private set; }

    public Func<IndexStatusChange, IndexStatusCheck>? Check { get; set; }

    public StewardException? ReadFailure { get; set; }

    public StewardException? CheckFailure { get; set; }

    public Exception? OpenFailure { get; set; }

    public TaskCompletionSource? HoldOpen { get; set; }

    public void Merge(int number)
    {
        Pulls.RemoveAll(pull => pull.Number == number);
        for (var index = 0; index < Pulls.Count; index++)
            Pulls[index] = Pulls[index] with { Conflicts = true };
    }

    public Task<IndexStatusOverview> ReadAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return ReadFailure is { } failure ? Task.FromException<IndexStatusOverview>(failure) : Task.FromResult(new IndexStatusOverview([.. Entries], [.. Pulls]));
    }

    public Task<IndexStatusCheck> CheckAsync(IndexStatusChange change, CancellationToken cancellationToken = default)
    {
        Checked.Add(change);
        return CheckFailure is { } failure
            ? Task.FromException<IndexStatusCheck>(failure)
            : Task.FromResult(Check?.Invoke(change) ?? new IndexStatusCheck(null, [.. Pulls], [], IsOwner: false));
    }

    public async Task<IndexStatusPullRequest> OpenAsync(IndexStatusChange change, CancellationToken cancellationToken = default)
    {
        if (HoldOpen is { } hold)
            await hold.Task;
        if (OpenFailure is { } failure)
            throw failure;

        // the real editor checks the reason only here, because the dialog checks the change before the reason is typed
        if (!IndexStatusChange.IsValidReason(change.Reason))
            throw new IndexStatusRefusedException(IndexStatusRefusal.InvalidReason, change.Id, change.Version);

        Opened.Add(change);
        var pull = new IndexStatusPullRequest(Opened.Count, new Uri($"https://github.com/KSAModding/content-index/pull/{Opened.Count}"), change.Title, "octocat", Conflicts: false);
        Pulls.Add(pull);
        return pull;
    }
}

/// <summary>A signed-in session that answers the main ruleset of each index repository with its bypass.</summary>
internal sealed class StewardSession : IGitHubSession
{
    public bool IsAvailable => true;

    public string ManageAccessUrl => "https://github.com/settings/apps/authorizations";

    public string InstallUrl => "https://github.com/apps/borea-test/installations/new";

    public string InstallUrlFor(long repositoryId) => InstallUrl;

    public GitHubSessionState State { get; private set; } = GitHubSessionState.SignedOut;

    public Dictionary<string, string> Bypass { get; } = new()
    {
        ["KSAModding/content-index"] = "always",
        ["KSAModding/content-index-releases"] = "always",
    };

    public event EventHandler? StateChanged;

    public void SignInDirectly()
    {
        State = GitHubSessionState.SignedInAs("octocat");
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<GitHubSignInResult> SignInAsync(IProgress<GitHubDeviceCode>? progress = null, CancellationToken cancellationToken = default)
    {
        SignInDirectly();
        return Task.FromResult(new GitHubSignInResult(GitHubSignInOutcome.SignedIn, "octocat"));
    }

    public void SignOut()
    {
        State = GitHubSessionState.SignedOut;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var url = request.RequestUri!.AbsoluteUri;
        var repository = Bypass.Keys.FirstOrDefault(name => url.StartsWith($"https://api.github.com/repos/{name}/rulesets", StringComparison.Ordinal));
        var body = repository is null ? null
            : url.EndsWith("/rulesets?per_page=100&page=1", StringComparison.Ordinal) ? """[{"id":1,"target":"branch","enforcement":"active"}]"""
            : """{"id":1,"target":"branch","enforcement":"active","conditions":{"ref_name":{"include":["~DEFAULT_BRANCH"],"exclude":[]}},"current_user_can_bypass":""" + $"\"{Bypass[repository]}\"}}";
        return Task.FromResult(new HttpResponseMessage(body is null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
        {
            Content = new StringContent(body ?? """{"message":"Not Found"}""", Encoding.UTF8, "application/json"),
        });
    }
}
