namespace Borea.Core.Stewardship;

/// <summary>
/// The steward actions on one pull request of an index repository, with the token of the signed-in account. Each one acts on the pull request
/// as its review showed it, and refuses when the pull request has another head commit or another base branch, or is no longer open.
/// </summary>
public interface IPullRequestActions
{
    /// <summary>Sends a review of the head commit that the review showed, so GitHub attaches it to that commit.</summary>
    /// <param name="body">The text of the review, which Request changes and Comment need.</param>
    /// <exception cref="ArgumentException">The repository is no index repository, or the review needs a text and has none.</exception>
    /// <exception cref="PullRequestRefusedException">The pull request changed since the review showed it. Nothing was sent.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the review.</exception>
    Task ReviewAsync(PullRequestReview pullRequest, PullRequestReviewKind kind, string? body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Squash merges the head commit that the review showed. It first reads validate on that commit, the verdict and the review decision again,
    /// because the bypass of a steward lets GitHub merge without them.
    /// </summary>
    /// <param name="skipRequiredReview">The steward confirmed a second time that the merge skips a review that GitHub still requires.</param>
    /// <exception cref="ArgumentException">The repository is no index repository.</exception>
    /// <exception cref="PullRequestRefusedException">Borea does not merge the pull request, or GitHub has another head commit or base branch. Nothing was merged.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the merge.</exception>
    Task MergeAsync(PullRequestReview pullRequest, bool skipRequiredReview, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the comment on the pull request and closes it without a merge. When the last comment on the pull request is already this comment
    /// of the signed-in account, such as after a close that failed after its comment, it only closes, so a retry does not post the comment twice.
    /// </summary>
    /// <exception cref="ArgumentException">The repository is no index repository, or the comment is empty.</exception>
    /// <exception cref="PullRequestRefusedException">The pull request changed since the review showed it. Nothing was sent.</exception>
    /// <exception cref="StewardException">A request failed, or GitHub refused the change.</exception>
    Task CloseAsync(PullRequestReview pullRequest, string comment, CancellationToken cancellationToken = default);
}

public enum PullRequestReviewKind
{
    Approve,
    RequestChanges,
    Comment,
}

/// <summary>Why Borea does not act on a pull request.</summary>
public enum PullRequestRefusal
{
    /// <summary>The pull request is closed or merged.</summary>
    NotOpen,

    /// <summary>The pull request has another head commit or another base branch than the review showed.</summary>
    Changed,

    Draft,

    /// <summary>validate on the head commit is not success.</summary>
    Validate,

    /// <summary>The indexer bot left no verdict comment.</summary>
    Verdict,

    /// <summary>GitHub still requires a review, such as the one of a code owner, and the steward did not confirm that the merge skips it.</summary>
    ReviewRequired,
}

public sealed class PullRequestRefusedException(PullRequestRefusal refusal) : Exception(refusal.ToString())
{
    public PullRequestRefusal Refusal { get; } = refusal;
}

public static class PullRequestMerge
{
    /// <summary>The merge method that auto-merge of the index repositories uses too.</summary>
    public const string Method = "squash";

    /// <summary>
    /// Why Borea does not merge the pull request as <paramref name="pullRequest"/> shows it, or an empty list. A steward bypasses the ruleset,
    /// so GitHub would merge without these checks.
    /// </summary>
    public static IReadOnlyList<PullRequestRefusal> BlockersOf(PullRequestReview pullRequest)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        var blockers = new List<PullRequestRefusal>();
        if (pullRequest.State != PullRequestState.Open)
            blockers.Add(PullRequestRefusal.NotOpen);
        if (pullRequest.IsDraft)
            blockers.Add(PullRequestRefusal.Draft);
        if (pullRequest.Validate?.State != ValidateState.Success)
            blockers.Add(PullRequestRefusal.Validate);
        if (pullRequest.Verdict is null)
            blockers.Add(PullRequestRefusal.Verdict);

        return blockers;
    }

    /// <summary>Request changes and Comment carry a text, and an approval can go without one.</summary>
    public static bool NeedsBody(PullRequestReviewKind kind) => kind != PullRequestReviewKind.Approve;
}
