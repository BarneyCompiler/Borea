using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Borea.Core.GitHub;
using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.Mods;
using Borea.Network.GitHub;
using Borea.Network.SpaceDock;

namespace Borea.Network.Listings;

/// <summary>
/// IListingPublisher on the signed-in GitHub session: the author's fork of content-index where the Borea App is installed,
/// one branch per listing or pack version, and the pull request from the author's own account. The ownership pre-check
/// follows tools/ownership.py and tools/pack_ownership.py of content-index.
/// </summary>
public sealed class ListingPublisher : IListingPublisher
{
    internal const string Api = GitHubApi.Root;

    internal const string Upstream = ListingPullRequestLinks.Repository;

    internal const string VerdictMarker = "<!-- content-index:verdict -->";

    internal const string StatusContext = "validate";

    internal const string StewardLabel = "needs-steward";

    internal const string MergingDescription = "validated, arming auto-merge";

    private const int SpaceDockGameId = 22409;

    private const int MaxBranchNumber = 100;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IGitHubSession _session;
    private readonly HttpClient _http;
    private readonly IListingFormat _format;
    private readonly GitHubApi _api;
    private readonly string _base;

    /// <param name="http">Reads SpaceDock and the GitHub repositories that the token cannot reach.</param>
    /// <param name="format">Reads the marker file.</param>
    /// <param name="baseBranch">The branch of content-index that the pull requests go to. Null takes <see cref="ListingPullRequestLinks.Branch"/>.</param>
    public ListingPublisher(IGitHubSession session, HttpClient http, IListingFormat format, TimeProvider? time = null, string? baseBranch = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _format = format ?? throw new ArgumentNullException(nameof(format));
        _base = baseBranch ?? ListingPullRequestLinks.Branch;
        _api = new GitHubApi(session, http, time, _base);
    }

    public async Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, ContentIndexSnapshot? snapshot = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submitted);
        var login = Login(ListingPublishStep.Ownership);
        try
        {
            var user = await GetAsync<UserDto>($"{Api}/user", ListingPublishStep.Ownership, cancellationToken).ConfigureAwait(false);
            var ownership = submitted.IsPack
                ? await VerifyPackAsync(submitted.Id, new ListingPackOwner(user.Login.Length > 0 ? user.Login : login, user.Id), snapshot, cancellationToken).ConfigureAwait(false)
                : await VerifyChangeAsync(submitted, listed, login, user.Id, cancellationToken).ConfigureAwait(false);
            if (ownership.State is not (ListingOwnershipState.Verified or ListingOwnershipState.FirstClaim))
                return ownership;

            // The files go into this pull request, and content-index sends a pull request with other files to a steward.
            string[] paths = ownership.State == ListingOwnershipState.FirstClaim ? [submitted.Path, ListingPackOwner.PathOf(submitted.Id)] : [submitted.Path];
            var open = await FindOpenPullRequestAsync(login, paths, cancellationToken).ConfigureAwait(false);
            return open is { OnlyTheseFiles: false }
                ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PullRequestHasOtherFiles, PullRequest: open.Pull.Number)
                : ownership;
        }
        catch (ListingPublishException exception) when (exception.Failure != ListingPublishFailure.SignedOut)
        {
            return ListingOwnership.Unknown;
        }
    }

    public async Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var login = Login(ListingPublishStep.Fork);

        progress?.Report(ListingPublishStep.Fork);
        var fork = await FindForkAsync(login, cancellationToken).ConfigureAwait(false);
        submission = await WithOwnerRecordAsync(submission, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.FindPullRequest);
        if (await FindOpenPullRequestAsync(login, submission.Files.Select(file => file.Path).ToList(), cancellationToken).ConfigureAwait(false) is { } open)
        {
            if (!string.Equals(open.Pull.Head!.Repo!.FullName, fork.FullName, StringComparison.OrdinalIgnoreCase))
                throw new ListingPublishException(ListingPublishFailure.PullRequestNotOnFork, ListingPublishStep.FindPullRequest, open.Pull.Number.ToString(CultureInfo.InvariantCulture));

            progress?.Report(ListingPublishStep.Commit);
            return await CommitToOpenAsync(open.Pull, submission, cancellationToken).ConfigureAwait(false);
        }

        // The branch starts at a commit the fork already has, because writing newer history of content-index into the fork
        // needs the Workflows permission whenever that history changes a workflow.
        var compare = await GetAsync<CompareDto>(
            $"{Api}/repos/{Upstream}/compare/{_base}...{fork.FullName.Replace('/', ':')}:{Uri.EscapeDataString(fork.DefaultBranch)}?per_page=1",
            ListingPublishStep.Branch,
            cancellationToken).ConfigureAwait(false);
        var main = compare.BaseCommit?.Sha ?? throw Unexpected(ListingPublishStep.Branch);
        var start = compare.MergeBaseCommit?.Sha ?? throw Unexpected(ListingPublishStep.Branch);
        var listed = new List<RepositoryFile?>();
        foreach (var file in submission.Files)
            listed.Add(await ReadFileAsync(Upstream, file.Path, main, ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false));
        if (submission.Files.Select((file, index) => listed[index]?.Text == file.Text).All(same => same))
            throw new ListingPublishException(ListingPublishFailure.NoChange, ListingPublishStep.Commit);

        if (start != main)
        {
            for (var index = 0; index < submission.Files.Count; index++)
            {
                var based = await ReadFileAsync(Upstream, submission.Files[index].Path, start, ListingPublishStep.Branch, cancellationToken).ConfigureAwait(false);
                if (based?.Sha != listed[index]?.Sha)
                    throw new ListingPublishException(ListingPublishFailure.ForkNeedsSync, ListingPublishStep.Branch, fork.FullName);
            }
        }

        progress?.Report(ListingPublishStep.Branch);
        var branch = await CreateBranchAsync(fork, BranchName(submission), start, cancellationToken).ConfigureAwait(false);

        // The pull request opens after every file is on the branch, so the first run of the checks sees them all.
        progress?.Report(ListingPublishStep.Commit);
        for (var index = 0; index < submission.Files.Count; index++)
            await PutFileAsync(fork.FullName, branch, submission, submission.Files[index], listed[index]?.Sha, cancellationToken).ConfigureAwait(false);

        progress?.Report(ListingPublishStep.PullRequest);
        var name = submission.Name.Trim().Length > 0 ? submission.Name.Trim() : submission.Id;
        var (title, text) = submission switch
        {
            { PackVersion: { } version, ClaimsPack: true } => ($"List {name}", $"Lists the pack {name} with its first version {version}."),
            { PackVersion: { } version } => ($"Add {name} {version}", $"Adds version {version} of the pack {name}."),
            { IsEdit: true } => ($"Update {name}", $"Updates the listing of {name}."),
            _ => ($"List {name}", $"Lists {name}."),
        };
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["head"] = $"{fork.Owner}:{branch}",
            ["base"] = _base,
            ["body"] = text,
            ["maintainer_can_modify"] = true,
        };
        var reply = await SendAsync(HttpMethod.Post, $"{Api}/repos/{Upstream}/pulls", body, ListingPublishStep.PullRequest, cancellationToken).ConfigureAwait(false);
        var pull = Parse<PullDto>(Ensure(reply, ListingPublishStep.PullRequest), ListingPublishStep.PullRequest);
        return new ListingPullRequest(pull.Number, PullRequestUrl(pull, ListingPublishStep.PullRequest), ListingPublishOutcome.Opened);
    }

    public async Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default)
    {
        const ListingPublishStep step = ListingPublishStep.Status;
        Login(step);
        var pull = await GetAsync<PullDto>($"{Api}/repos/{Upstream}/pulls/{number}", step, cancellationToken).ConfigureAwait(false);
        if (pull.Merged)
            return new ListingPullRequestStatus(ListingPullRequestState.Merged, null);
        if (pull.State == "closed")
            return new ListingPullRequestStatus(ListingPullRequestState.Closed, null);

        var headSha = pull.Head?.Sha ?? throw Unexpected(step);
        var combined = await GetPublicAsync<CombinedStatusDto>($"{Api}/repos/{Upstream}/commits/{headSha}/status", step, cancellationToken).ConfigureAwait(false);
        var comments = await GetPublicAsync<List<CommentDto>>($"{Api}/repos/{Upstream}/issues/{number}/comments?per_page=100", step, cancellationToken).ConfigureAwait(false);

        // Only the bot's own comment counts, because anybody can write the marker into a comment.
        var verdict = comments
            .FirstOrDefault(comment => comment.User?.Type == "Bot" && comment.Body?.Contains(VerdictMarker, StringComparison.Ordinal) == true)
            ?.Body!.Replace(VerdictMarker, string.Empty, StringComparison.Ordinal).Trim();

        var validate = combined.Statuses.FirstOrDefault(status => status.Context == StatusContext);
        var state = validate is null ? ListingPullRequestState.ChecksRunning : validate.State switch
        {
            "failure" => ListingPullRequestState.Rejected,
            "error" => ListingPullRequestState.CouldNotEvaluate,
            "success" when validate.Description == MergingDescription && pull.Labels.All(label => label.Name != StewardLabel) => ListingPullRequestState.ValidatedMerging,
            "success" => ListingPullRequestState.WaitingForSteward,
            _ => ListingPullRequestState.ChecksRunning,
        };
        return new ListingPullRequestStatus(state, string.IsNullOrEmpty(verdict) ? null : verdict);
    }

    private string Login(ListingPublishStep step) =>
        _session.State is { Status: GitHubSessionStatus.SignedIn, Login: { } login }
            ? login
            : throw new ListingPublishException(ListingPublishFailure.SignedOut, step);

    /// <summary>RFC 0048: an edit proves control of the listed host, and of the new one when it moves.</summary>
    private async Task<ListingOwnership> VerifyChangeAsync(ListingDraft submitted, ListingDraft? listed, string login, long authorId, CancellationToken cancellationToken)
    {
        if (listed is null)
            return await VerifyAsync(submitted, login, authorId, cancellationToken).ConfigureAwait(false);

        var current = await VerifyAsync(listed, login, authorId, cancellationToken).ConfigureAwait(false);
        var listedHost = ListingAuthority.Of(listed);
        var submittedHost = ListingAuthority.Of(submitted);
        if (listedHost is null ? submittedHost is null : listedHost.IsSameHost(submittedHost))
            return current;

        if (current.State != ListingOwnershipState.Verified
            && !await IsRenamedIntoAsync(listedHost, submittedHost, cancellationToken).ConfigureAwait(false))
        {
            return current;
        }

        return await VerifyAsync(submitted, login, authorId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A pack has no release host, so its owner record on main decides (RFC 0033). The paths of content-index are case-sensitive,
    /// so a missing record makes the id free only while no listing or pack of the snapshot holds it in another letter case.
    /// </summary>
    private async Task<ListingOwnership> VerifyPackAsync(string id, ListingPackOwner author, ContentIndexSnapshot? snapshot, CancellationToken cancellationToken)
    {
        // An id that cannot be a folder of content-index has no owner record, and it is never free to claim.
        if (!ModIds.IsValid(id))
            return ListingOwnership.Unknown;

        var record = await ReadFileAsync(Upstream, ListingPackOwner.PathOf(id), _base, ListingPublishStep.Ownership, cancellationToken).ConfigureAwait(false);
        if (record is not null)
        {
            return ListingPackOwner.Parse(record.Text) switch
            {
                null => ListingOwnership.Unknown,
                { Id: var owner } when owner == author.Id => new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.PackOwner),
                var other => new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PackOwnedByOther, PackOwner: other.Login),
            };
        }

        if (snapshot is null)
            return ListingOwnership.Unknown;

        return ListingPackOwner.HolderOf(snapshot, id) is { } holder
            ? new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.PackIdTaken, TakenBy: holder)
            : new ListingOwnership(ListingOwnershipState.FirstClaim, Claim: author);
    }

    /// <summary>A pack without an owner record on main is a first claim, so the record of the signed-in account goes into the same pull request.</summary>
    private async Task<ListingSubmission> WithOwnerRecordAsync(ListingSubmission submission, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Ownership;
        if (!submission.IsPack)
            return submission;

        var path = ListingPackOwner.PathOf(submission.Id);
        if (await ReadFileAsync(Upstream, path, _base, step, cancellationToken).ConfigureAwait(false) is not null)
            return submission;

        var user = await GetAsync<UserDto>($"{Api}/user", step, cancellationToken).ConfigureAwait(false);
        if (user.Id <= 0 || user.Login.Length == 0)
            throw Unexpected(step);

        return submission with { Files = [.. submission.Files.Where(file => file.Path != path), new ListingFile(path, new ListingPackOwner(user.Login, user.Id).ToJson())] };
    }

    private async Task<ListingOwnership> VerifyAsync(ListingDraft draft, string login, long authorId, CancellationToken cancellationToken)
    {
        return ListingAuthority.Of(draft) switch
        {
            { Kind: ListingAuthority.GitHub } github => await VerifyRepositoryAsync(github.Target, draft.Id, login, authorId, cancellationToken).ConfigureAwait(false),
            { Kind: ListingAuthority.SpaceDock } spaceDock => await VerifySpaceDockAsync(spaceDock.Target, draft.Id, login, authorId, cancellationToken).ConfigureAwait(false),
            _ => new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.NoHost),
        };
    }

    private async Task<ListingOwnership> VerifyRepositoryAsync(string target, string id, string login, long authorId, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Ownership;
        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{target}", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return NotVerified(ListingOwnershipProblem.RepositoryMissing, target);

        var repository = Parse<RepositoryDto>(Ensure(reply, step), step);
        if (!string.Equals(repository.FullName, target, StringComparison.OrdinalIgnoreCase))
            return NotVerified(ListingOwnershipProblem.RepositoryRenamed, target) with { RenamedTo = repository.FullName };
        if (repository.Fork)
            return NotVerified(ListingOwnershipProblem.RepositoryFork, target);
        if (repository.Owner?.Id == authorId)
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: target);

        var topics = await SendAsync(HttpMethod.Get, $"{Api}/repos/{target}/topics", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        var names = topics.Status == HttpStatusCode.NotFound ? [] : Parse<TopicsDto>(Ensure(topics, step), step).Names;
        if (names.Contains(ListingOwnership.TopicFor(login), StringComparer.Ordinal))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Topic, Repository: target);

        var marker = await ReadFileAsync(target, ListingOwnership.MarkerPath, null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (marker?.Text is { } text && MarkerNames(text, id, login))
            return new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.MarkerFile, Repository: target);

        return NotVerified(ListingOwnershipProblem.NoProof, target);
    }

    /// <summary>A SpaceDock mod binds to the GitHub repository of its source code link, which only its authors can set.</summary>
    private async Task<ListingOwnership> VerifySpaceDockAsync(string modId, string id, string login, long authorId, CancellationToken cancellationToken)
    {
        var unusable = new ListingOwnership(ListingOwnershipState.NotVerified, Problem: ListingOwnershipProblem.SpaceDockModUnusable, SpaceDockMod: modId);
        if (modId.Length == 0 || !modId.All(char.IsAsciiDigit))
            return unusable;

        SpaceDockModDto? mod;
        try
        {
            using var response = await _http.GetAsync($"{SpaceDockModRepository.BaseUrl}/api/mod/{modId}", cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return unusable;

            var refused = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
            if (!response.IsSuccessStatusCode && !refused)
                return ListingOwnership.Unknown;

            mod = JsonSerializer.Deserialize<SpaceDockModDto>(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), Json);
            if (mod is null || (refused && !mod.Error))
                return ListingOwnership.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            return ListingOwnership.Unknown;
        }

        if (mod.Error)
            return unusable;
        if (mod.Id?.ToString() != modId)
            return ListingOwnership.Unknown;
        if (mod.GameId != SpaceDockGameId)
            return unusable;
        if (ListingAuthority.GitHubRepositoryOf(mod.SourceCode) is not { } repository)
            return unusable with { Problem = ListingOwnershipProblem.SpaceDockNoSourceLink };

        var result = await VerifyRepositoryAsync(repository, id, login, authorId, cancellationToken).ConfigureAwait(false);
        return result with { SpaceDockMod = modId };
    }

    /// <summary>GitHub answers the old name of a renamed or transferred repository with the new one.</summary>
    private async Task<bool> IsRenamedIntoAsync(ListingAuthority? listed, ListingAuthority? submitted, CancellationToken cancellationToken)
    {
        if (listed?.Kind != ListingAuthority.GitHub || submitted?.Kind != ListingAuthority.GitHub)
            return false;

        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{listed.Target}", null, ListingPublishStep.Ownership, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return false;

        var repository = Parse<RepositoryDto>(Ensure(reply, ListingPublishStep.Ownership), ListingPublishStep.Ownership);
        return !repository.Fork && string.Equals(repository.FullName, submitted.Target, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A marker that names only the login covers every listing of the repository.</summary>
    private bool MarkerNames(string text, string id, string login)
    {
        AuthoredTable marker;
        try
        {
            marker = _format.Read(text);
        }
        catch (FormatException)
        {
            return false;
        }

        var claimed = IsSet(marker["login"]) ? marker["login"] : marker["account"];
        var identifier = IsSet(marker["id"]) ? marker["id"] : marker["listing"];
        return claimed is string account
            && string.Equals(account, login, StringComparison.OrdinalIgnoreCase)
            && (identifier is not string named || string.Equals(named, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSet(object? value) => value switch
    {
        null => false,
        string text => text.Length > 0,
        long number => number != 0,
        double number => number != 0,
        bool flag => flag,
        AuthoredTable table => table.Count > 0,
        IReadOnlyList<object> list => list.Count > 0,
        _ => true,
    };

    private static ListingOwnership NotVerified(ListingOwnershipProblem problem, string repository) =>
        new(ListingOwnershipState.NotVerified, Problem: problem, Repository: repository);

    /// <summary>
    /// The author's open pull request that changes the document, the first of <paramref name="paths"/>. A pull request
    /// of the same pack with another version file is another pull request, even when it shares the owner record.
    /// </summary>
    private async Task<OpenPullRequest?> FindOpenPullRequestAsync(string login, IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.FindPullRequest;
        var pulls = Pages(step, _api.GetPagesAsync<PullDto>($"{Api}/repos/{Upstream}/pulls?state=open", cancellationToken), cancellationToken);
        await foreach (var pull in pulls.ConfigureAwait(false))
        {
            if (!string.Equals(pull.User?.Login, login, StringComparison.OrdinalIgnoreCase) || pull.Head?.Repo is null)
                continue;

            var files = await FilesAsync(pull.Number, cancellationToken).ConfigureAwait(false);
            if (files.Contains(paths[0], StringComparer.Ordinal))
                return new OpenPullRequest(pull, files.All(file => paths.Contains(file, StringComparer.Ordinal)));
        }

        return null;
    }

    private async Task<List<string>> FilesAsync(int number, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        var files = Pages(ListingPublishStep.FindPullRequest, _api.GetPagesAsync<PullFileDto>($"{Api}/repos/{Upstream}/pulls/{number}/files", cancellationToken), cancellationToken);
        await foreach (var file in files.ConfigureAwait(false))
            names.Add(file.Filename);

        return names;
    }

    private async Task<ListingPullRequest> CommitToOpenAsync(PullDto pull, ListingSubmission submission, CancellationToken cancellationToken)
    {
        var url = PullRequestUrl(pull, ListingPublishStep.Commit);
        var repository = pull.Head!.Repo!.FullName;
        var branch = pull.Head.Ref;
        var outcome = ListingPublishOutcome.Unchanged;
        foreach (var file in submission.Files)
        {
            var current = await ReadFileAsync(repository, file.Path, branch, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false);
            if (current?.Text == file.Text)
                continue;

            await PutFileAsync(repository, branch, submission, file, current?.Sha, cancellationToken).ConfigureAwait(false);
            outcome = ListingPublishOutcome.Updated;
        }

        return new ListingPullRequest(pull.Number, url, outcome);
    }

    /// <summary>
    /// The fork of content-index among the repositories of the App's installation on the author's own account.
    /// Without one, a read without the token tells a missing fork from a fork without the App.
    /// </summary>
    private async Task<Fork> FindForkAsync(string login, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Fork;
        if (await FindInstallationAsync(login, cancellationToken).ConfigureAwait(false) is { } installation)
        {
            var repositories = Pages(step, _api.GetPagesAsync<InstallationRepositoriesDto, RepositoryDto>($"{Api}/user/installations/{installation}/repositories", page => page.Repositories, cancellationToken), cancellationToken);
            await foreach (var candidate in repositories.ConfigureAwait(false))
            {
                if (!candidate.Fork)
                    continue;

                var repository = await GetAsync<RepositoryDto>($"{Api}/repos/{candidate.FullName}", step, cancellationToken).ConfigureAwait(false);
                if (IsForkOfUpstream(repository) && ForkOf(repository) is { } fork)
                    return fork;
            }
        }

        var reply = await SendAsync(HttpMethod.Get, $"{Api}/repos/{login}/content-index", null, step, cancellationToken, anonymous: true).ConfigureAwait(false);
        if (reply.Status != HttpStatusCode.NotFound && Parse<RepositoryDto>(Ensure(reply, step), step) is var named && IsForkOfUpstream(named))
            throw new ListingPublishException(ListingPublishFailure.AppNotOnFork, step, named.FullName, repositoryId: named.Id > 0 ? named.Id : null);

        throw new ListingPublishException(ListingPublishFailure.NoFork, step);
    }

    private async Task<long?> FindInstallationAsync(string login, CancellationToken cancellationToken)
    {
        var installations = Pages(ListingPublishStep.Fork, _api.GetPagesAsync<InstallationsDto, InstallationDto>($"{Api}/user/installations", page => page.Installations, cancellationToken), cancellationToken);
        await foreach (var installation in installations.ConfigureAwait(false))
        {
            if (string.Equals(installation.Account?.Login, login, StringComparison.OrdinalIgnoreCase))
                return installation.Id;
        }

        return null;
    }

    private static bool IsForkOfUpstream(RepositoryDto repository) =>
        repository.Fork && string.Equals(repository.Parent?.FullName, Upstream, StringComparison.OrdinalIgnoreCase);

    private static Fork? ForkOf(RepositoryDto repository)
    {
        var slash = repository.FullName.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && repository.DefaultBranch.Length > 0 ? new Fork(repository.FullName, repository.FullName[..slash], repository.DefaultBranch) : null;
    }

    /// <summary>listing-&lt;id&gt; for a listing, pack-&lt;id&gt;-&lt;version&gt; for a pack version.</summary>
    private static string BranchName(ListingSubmission submission) =>
        (submission.PackVersion is { } version ? $"pack-{submission.Id}-{version}" : "listing-" + submission.Id).ToLowerInvariant();

    /// <summary>Creates <paramref name="baseName"/> at <paramref name="sha"/>, or the first free name with -N. A refusal asks for a sync of the fork.</summary>
    private async Task<string> CreateBranchAsync(Fork fork, string baseName, string sha, CancellationToken cancellationToken)
    {
        try
        {
            return await CreateFreeBranchAsync(fork, baseName, sha, cancellationToken).ConfigureAwait(false);
        }
        catch (ListingPublishException exception) when (exception.Failure == ListingPublishFailure.Forbidden)
        {
            throw new ListingPublishException(ListingPublishFailure.ForkNeedsSync, ListingPublishStep.Branch, fork.FullName, innerException: exception);
        }
    }

    private async Task<string> CreateFreeBranchAsync(Fork fork, string baseName, string sha, CancellationToken cancellationToken)
    {
        const ListingPublishStep step = ListingPublishStep.Branch;
        for (var number = 1; number <= MaxBranchNumber; number++)
        {
            var name = number == 1 ? baseName : $"{baseName}-{number.ToString(CultureInfo.InvariantCulture)}";
            var existing = await SendAsync(HttpMethod.Get, $"{Api}/repos/{fork.FullName}/git/ref/heads/{name}", null, step, cancellationToken).ConfigureAwait(false);
            if (existing.Status == HttpStatusCode.OK)
                continue;
            if (existing.Status != HttpStatusCode.NotFound)
                Ensure(existing, step);

            var body = new Dictionary<string, object> { ["ref"] = "refs/heads/" + name, ["sha"] = sha };
            var created = await SendAsync(HttpMethod.Post, $"{Api}/repos/{fork.FullName}/git/refs", body, step, cancellationToken).ConfigureAwait(false);
            if (created.Status == HttpStatusCode.Created)
                return name;
            if (created.Status == HttpStatusCode.UnprocessableEntity && created.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            Ensure(created, step);
            throw Unexpected(step);
        }

        throw new ListingPublishException(ListingPublishFailure.Refused, step, $"{baseName} to {baseName}-{MaxBranchNumber} are taken");
    }

    private async Task PutFileAsync(string repository, string branch, ListingSubmission submission, ListingFile file, string? sha, CancellationToken cancellationToken)
    {
        var message = submission switch
        {
            _ when file.Path != submission.Document.Path => $"Claim {submission.Id}",
            { PackVersion: { } version } => $"Add {submission.Id} {version}",
            { IsEdit: true } => $"Update {submission.Id}",
            _ => $"List {submission.Id}",
        };
        var body = new Dictionary<string, object>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Text)),
            ["branch"] = branch,
        };
        if (sha is not null)
            body["sha"] = sha;

        Ensure(await SendAsync(HttpMethod.Put, $"{Api}/repos/{repository}/contents/{file.Path}", body, ListingPublishStep.Commit, cancellationToken).ConfigureAwait(false), ListingPublishStep.Commit);
    }

    /// <summary>The file and its blob sha at <paramref name="reference"/>, or null when it is not there.</summary>
    private async Task<RepositoryFile?> ReadFileAsync(string repository, string path, string? reference, ListingPublishStep step, CancellationToken cancellationToken, bool anonymous = false)
    {
        var url = $"{Api}/repos/{repository}/contents/{path}" + (reference is null ? string.Empty : "?ref=" + Uri.EscapeDataString(reference));
        var reply = await SendAsync(HttpMethod.Get, url, null, step, cancellationToken, anonymous).ConfigureAwait(false);
        if (reply.Status == HttpStatusCode.NotFound)
            return null;

        var file = Parse<ContentDto>(Ensure(reply, step), step);
        if (file.Encoding != "base64" || file.Sha.Length == 0)
            throw Unexpected(step);

        try
        {
            return new RepositoryFile(file.Sha, StrictUtf8.GetString(Convert.FromBase64String(file.Content)));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return new RepositoryFile(file.Sha, null);
        }
    }

    private Task<T> GetAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken) =>
        At(step, _api.GetAsync<T>(url, cancellationToken));

    private Task<T> GetPublicAsync<T>(string url, ListingPublishStep step, CancellationToken cancellationToken) =>
        At(step, _api.GetPublicAsync<T>(url, cancellationToken));

    private Task<GitHubReply> SendAsync(HttpMethod method, string url, object? body, ListingPublishStep step, CancellationToken cancellationToken, bool anonymous = false) =>
        At(step, _api.SendAsync(method, url, body, cancellationToken, anonymous));

    private static string Ensure(GitHubReply reply, ListingPublishStep step) => At(step, () => GitHubApi.Ensure(reply));

    private static T Parse<T>(string body, ListingPublishStep step) => At(step, () => GitHubApi.Parse<T>(body));

    private static async Task<T> At<T>(ListingPublishStep step, Task<T> call)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (GitHubApiException exception)
        {
            throw Failed(exception, step);
        }
    }

    private static T At<T>(ListingPublishStep step, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (GitHubApiException exception)
        {
            throw Failed(exception, step);
        }
    }

    private static async IAsyncEnumerable<T> Pages<T>(ListingPublishStep step, IAsyncEnumerable<T> items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = items.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await At(step, enumerator.MoveNextAsync().AsTask()).ConfigureAwait(false))
                yield return enumerator.Current;
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static ListingPublishException Failed(GitHubApiException exception, ListingPublishStep step)
    {
        var failure = exception.Failure switch
        {
            GitHubApiFailure.SignedOut => ListingPublishFailure.SignedOut,
            GitHubApiFailure.RateLimited => ListingPublishFailure.RateLimited,
            GitHubApiFailure.NotFound => ListingPublishFailure.NotFound,
            GitHubApiFailure.Refused or GitHubApiFailure.ProtectedBranch => ListingPublishFailure.Refused,
            GitHubApiFailure.Forbidden => ListingPublishFailure.Forbidden,
            GitHubApiFailure.NetworkError => ListingPublishFailure.NetworkError,
            _ => ListingPublishFailure.UnexpectedResponse,
        };
        return new ListingPublishException(failure, step, exception.Detail, exception.RetryAt, exception.InnerException);
    }

    private static Uri PullRequestUrl(PullDto pull, ListingPublishStep step) =>
        Uri.TryCreate(pull.HtmlUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps && pull.Number > 0 ? url : throw Unexpected(step);

    private static ListingPublishException Unexpected(ListingPublishStep step) => new(ListingPublishFailure.UnexpectedResponse, step);

    private sealed record Fork(string FullName, string Owner, string DefaultBranch);

    private sealed record RepositoryFile(string Sha, string? Text);

    private sealed record OpenPullRequest(PullDto Pull, bool OnlyTheseFiles);

    private sealed class UserDto
    {
        public long Id { get; set; }

        public string Login { get; set; } = string.Empty;
    }

    private sealed class OwnerDto
    {
        public long Id { get; set; }

        public string Login { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;
    }

    private sealed class RepositoryDto
    {
        public long Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public bool Fork { get; set; }

        public string DefaultBranch { get; set; } = string.Empty;

        public OwnerDto? Owner { get; set; }

        public RepositoryDto? Parent { get; set; }
    }

    private sealed class InstallationsDto
    {
        public List<InstallationDto> Installations { get; set; } = [];
    }

    private sealed class InstallationDto
    {
        public long Id { get; set; }

        public OwnerDto? Account { get; set; }
    }

    private sealed class InstallationRepositoriesDto
    {
        public List<RepositoryDto> Repositories { get; set; } = [];
    }

    private sealed class CompareDto
    {
        public CommitDto? BaseCommit { get; set; }

        public CommitDto? MergeBaseCommit { get; set; }
    }

    private sealed class CommitDto
    {
        public string? Sha { get; set; }
    }

    private sealed class ContentDto
    {
        public string Sha { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public string Encoding { get; set; } = string.Empty;
    }

    private sealed class TopicsDto
    {
        public List<string> Names { get; set; } = [];
    }

    private sealed class PullDto
    {
        public int Number { get; set; }

        public string HtmlUrl { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public bool Merged { get; set; }

        public OwnerDto? User { get; set; }

        public PullHeadDto? Head { get; set; }

        public List<LabelDto> Labels { get; set; } = [];
    }

    private sealed class PullHeadDto
    {
        public string Ref { get; set; } = string.Empty;

        public string? Sha { get; set; }

        public RepositoryDto? Repo { get; set; }
    }

    private sealed class LabelDto
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PullFileDto
    {
        public string Filename { get; set; } = string.Empty;
    }

    private sealed class CombinedStatusDto
    {
        public List<CommitStatusDto> Statuses { get; set; } = [];
    }

    private sealed class CommitStatusDto
    {
        public string Context { get; set; } = string.Empty;

        public string State { get; set; } = string.Empty;

        public string? Description { get; set; }
    }

    private sealed class CommentDto
    {
        public string? Body { get; set; }

        public OwnerDto? User { get; set; }
    }

    private sealed class SpaceDockModDto
    {
        public JsonElement? Id { get; set; }

        public long? GameId { get; set; }

        public string? SourceCode { get; set; }

        public bool Error { get; set; }
    }
}
