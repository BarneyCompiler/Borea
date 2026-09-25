using Borea.Core.Index;

namespace Borea.Core.Listings;

/// <summary>
/// Opens the pull request of one listing document or pack version in content-index from the signed-in GitHub account, and follows it.
/// The checks of content-index stay the authority on the verdict and on ownership.
/// </summary>
public interface IListingPublisher
{
    /// <summary>Which ownership proof the checks would find for the signed-in account. It reads only and never blocks.</summary>
    /// <param name="listed">The listed document on main that an edit changes, or null for a new listing.</param>
    /// <param name="snapshot">The content index, which tells for a pack whether another holder has its id in another letter case.</param>
    /// <exception cref="ListingPublishException">Signed out, or GitHub refused the token.</exception>
    Task<ListingOwnership> CheckOwnershipAsync(ListingDraft submitted, ListingDraft? listed, ContentIndexSnapshot? snapshot = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the files to a branch of the author's fork and opens the pull request, or commits them to the
    /// author's open pull request that already changes the document. The author makes the fork and installs the App on it.
    /// The first version of a pack also gets the owner record of the signed-in account, while main has none.
    /// </summary>
    /// <exception cref="ListingPublishException">A step failed. Nothing of the draft is lost.</exception>
    Task<ListingPullRequest> PublishAsync(ListingSubmission submission, IProgress<ListingPublishStep>? progress = null, CancellationToken cancellationToken = default);

    /// <exception cref="ListingPublishException">The status could not be read.</exception>
    Task<ListingPullRequestStatus> GetStatusAsync(int number, CancellationToken cancellationToken = default);
}

/// <param name="Files">The files of the pull request, the document first.</param>
/// <param name="IsEdit">Whether the document changes a listed one.</param>
/// <param name="PackVersion">The version of a pack document, or null for a listing.</param>
public sealed record ListingSubmission(string Id, string Name, IReadOnlyList<ListingFile> Files, bool IsEdit, string? PackVersion = null)
{
    /// <param name="text">The document, as the listing format writes it.</param>
    public static ListingSubmission Of(ListingDraft draft, string text)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(text);
        return new(draft.Id, draft.Name, [new ListingFile(draft.Path, text)], draft.IsEdit, draft.IsPack ? draft.Version : null);
    }

    public bool IsPack => PackVersion is not null;

    public ListingFile Document => Files[0];

    /// <summary>Whether the pull request also adds the owner record of the pack, which makes it the first claim of the pack id.</summary>
    public bool ClaimsPack => IsPack && Files.Any(file => file.Path == ListingPackOwner.PathOf(Id));
}

/// <param name="Path">Where the file goes in content-index.</param>
public sealed record ListingFile(string Path, string Text);

public enum ListingPublishOutcome
{
    Opened,

    /// <summary>The files went to the author's open pull request as new commits.</summary>
    Updated,

    /// <summary>The author's open pull request already has these files.</summary>
    Unchanged,
}

public sealed record ListingPullRequest(int Number, Uri Url, ListingPublishOutcome Outcome);

public enum ListingPublishStep
{
    Ownership,
    FindPullRequest,
    Fork,
    Branch,
    Commit,
    PullRequest,
    Status,
}

public enum ListingPullRequestState
{
    ChecksRunning,
    ValidatedMerging,
    WaitingForSteward,
    Rejected,
    CouldNotEvaluate,
    Merged,
    Closed,
}

/// <param name="Verdict">The comment of the checks as Markdown, without its marker, or null before they commented.</param>
public sealed record ListingPullRequestStatus(ListingPullRequestState State, string? Verdict)
{
    /// <summary>Nothing changes any more.</summary>
    public bool IsFinal => State is ListingPullRequestState.Merged or ListingPullRequestState.Closed;
}

public enum ListingPublishFailure
{
    SignedOut,
    RateLimited,
    NotFound,
    Refused,
    Forbidden,
    NetworkError,

    /// <summary>The author has no fork of content-index.</summary>
    NoFork,

    /// <summary>The Borea App is not installed on the author's fork, which <see cref="ListingPublishException.Detail"/> names.</summary>
    AppNotOnFork,

    /// <summary>The author's open pull request that changes the document does not come from the fork. <see cref="ListingPublishException.Detail"/> holds its number.</summary>
    PullRequestNotOnFork,

    /// <summary>The author's fork, which <see cref="ListingPublishException.Detail"/> names, has to be synced with content-index on GitHub first.</summary>
    ForkNeedsSync,

    NoChange,
    UnexpectedResponse,
}

/// <summary>A step of the pull request failed. The message never holds the token.</summary>
public sealed class ListingPublishException : Exception
{
    public ListingPublishException(ListingPublishFailure failure, ListingPublishStep step, string? detail = null, DateTimeOffset? retryAt = null, Exception? innerException = null, long? repositoryId = null)
        : base(Describe(failure, step, detail) + (innerException is ListingPublishException cause ? $" ({Describe(cause.Failure, null, cause.Detail)})" : string.Empty), innerException)
    {
        Failure = failure;
        Step = step;
        Detail = detail;
        RetryAt = retryAt;
        RepositoryId = repositoryId;
    }

    public ListingPublishFailure Failure { get; }

    public ListingPublishStep Step { get; }

    /// <summary>What GitHub said, or the repository a failure is about.</summary>
    public string? Detail { get; }

    /// <summary>When GitHub accepts requests again after a rate limit.</summary>
    public DateTimeOffset? RetryAt { get; }

    /// <summary>The GitHub id of the repository the failure is about, when known.</summary>
    public long? RepositoryId { get; }

    private static string Describe(ListingPublishFailure failure, ListingPublishStep? step, string? detail) =>
        $"{(step is null ? string.Empty : step + " failed: ")}{failure}{(detail is null ? string.Empty : ", " + detail)}";
}
