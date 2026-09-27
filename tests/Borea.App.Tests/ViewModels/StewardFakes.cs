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

/// <summary>Answers the queue from its lists and records each filter it was asked for.</summary>
internal sealed class FakeStewardQueue : IStewardQueue
{
    public List<StewardQueueItem> Items { get; } = [];

    public List<StewardQueueFailure> Failures { get; } = [];

    public List<StewardQueueFilter> Filters { get; } = [];

    public StewardException? Failure { get; set; }

    public TaskCompletionSource? Hold { get; set; }

    public static StewardQueueItem Item(int number, bool needsSteward = true, string repository = "KSAModding/content-index", string? verdict = null) =>
        new(repository, number, new Uri($"https://github.com/{repository}/pull/{number}"), $"Pull {number}", "alice", DateTimeOffset.UtcNow.AddDays(-3), IsDraft: false, needsSteward, [StewardQueueKind.Listing], HasOtherFiles: false, verdict);

    public async Task<StewardQueue> ListAsync(StewardQueueFilter filter = StewardQueueFilter.NeedsSteward, CancellationToken cancellationToken = default)
    {
        Filters.Add(filter);
        if (Hold is { } hold)
            await hold.Task;
        if (Failure is { } failure)
            throw failure;

        return new StewardQueue([.. Items.Where(item => filter == StewardQueueFilter.AllOpen || item.NeedsSteward)], [.. Failures]);
    }
}

/// <summary>Answers each review from <see cref="Reviews"/> and records every read.</summary>
internal sealed class FakePullRequestReviews : IPullRequestReviews
{
    public Dictionary<(string Repository, int Number), PullRequestReview> Reviews { get; } = [];

    public List<(string Repository, int Number)> Reads { get; } = [];

    public StewardException? Failure { get; set; }

    public const string Head = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>A pull request of alice with no files, no documents, a green validate and no verdict.</summary>
    public static PullRequestReview Review(int number, string repository = "KSAModding/content-index", string? headRepository = null) =>
        new(repository, number, new Uri($"https://github.com/{repository}/pull/{number}"), $"Pull {number}", "alice", PullRequestState.Open, IsDraft: false, [], "main", Head, headRepository ?? repository, new ValidateStatus(ValidateState.Success), null, null, [], []);

    public static PullRequestFile File(PullRequestReview review, string path, string? patch, string status = "added", string? previousPath = null) =>
        new(path, status, previousPath, 4, 1, patch, PullRequestFile.DiffUrlOf(review.Url, path));

    public Task<PullRequestReview> ReadAsync(string repository, int number, CancellationToken cancellationToken = default)
    {
        Reads.Add((repository, number));
        return Failure is { } failure ? Task.FromException<PullRequestReview>(failure) : Task.FromResult(Reviews[(repository, number)]);
    }
}

/// <summary>Records every action it was sent, and answers each with the next of <see cref="Failures"/> or a success.</summary>
internal sealed class FakePullRequestActions : IPullRequestActions
{
    public List<(string Action, PullRequestReview PullRequest, string? Text, bool SkipRequiredReview)> Sent { get; } = [];

    public Queue<Exception> Failures { get; } = [];

    public TaskCompletionSource? Hold { get; set; }

    public Task ReviewAsync(PullRequestReview pullRequest, PullRequestReviewKind kind, string? body, CancellationToken cancellationToken = default) =>
        RunAsync(kind.ToString(), pullRequest, body, false);

    public Task MergeAsync(PullRequestReview pullRequest, bool skipRequiredReview, CancellationToken cancellationToken = default) =>
        RunAsync("Merge", pullRequest, null, skipRequiredReview);

    public Task CloseAsync(PullRequestReview pullRequest, string comment, CancellationToken cancellationToken = default) =>
        RunAsync("Close", pullRequest, comment, false);

    private async Task RunAsync(string action, PullRequestReview pullRequest, string? text, bool skipRequiredReview)
    {
        Sent.Add((action, pullRequest, text, skipRequiredReview));
        if (Hold is { } hold)
            await hold.Task;
        if (Failures.TryDequeue(out var failure))
            throw failure;
    }
}

/// <summary>Answers the Watcher tab from its lists and counts the reads.</summary>
internal sealed class FakeWatcherIssues : IWatcherIssues
{
    public List<WatcherIssue> Listings { get; } = [];

    public List<WatcherIssue> Watchdog { get; } = [];

    public List<WatcherIssuesFailure> Failures { get; } = [];

    public int Reads { get; private set; }

    public static WatcherIssue Listing(int number, string? listingId) =>
        new(WatcherIssues.ListingsRepository, number, new Uri($"https://github.com/{WatcherIssues.ListingsRepository}/issues/{number}"), $"{listingId ?? "Something"}: the watcher found a problem", DateTimeOffset.UtcNow.AddHours(-2), listingId);

    public static WatcherIssue WatchdogIssue(int number) =>
        new(WatcherIssues.WatchdogRepository, number, new Uri($"https://github.com/{WatcherIssues.WatchdogRepository}/issues/{number}"), "The watcher is not ticking", DateTimeOffset.UtcNow.AddMinutes(-50), null);

    public Task<WatcherIssues> ListAsync(CancellationToken cancellationToken = default)
    {
        Reads++;
        return Task.FromResult(new WatcherIssues([.. Listings], [.. Watchdog], [.. Failures]));
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
