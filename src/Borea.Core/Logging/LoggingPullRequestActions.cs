using System.Globalization;
using Borea.Core.Stewardship;

namespace Borea.Core.Logging;

public sealed class LoggingPullRequestActions : IPullRequestActions
{
    private readonly IBoreaLog _log;

    public IPullRequestActions Inner { get; }

    public LoggingPullRequestActions(IPullRequestActions inner, IBoreaLog log)
    {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task ReviewAsync(PullRequestReview pullRequest, PullRequestReviewKind kind, string? body, CancellationToken cancellationToken = default) =>
        LogAsync(pullRequest, $"{kind} review", Inner.ReviewAsync(pullRequest, kind, body, cancellationToken));

    public Task MergeAsync(PullRequestReview pullRequest, bool skipRequiredReview, CancellationToken cancellationToken = default) =>
        LogAsync(pullRequest, skipRequiredReview ? "Merge without the required review" : "Merge", Inner.MergeAsync(pullRequest, skipRequiredReview, cancellationToken));

    public Task CloseAsync(PullRequestReview pullRequest, string comment, CancellationToken cancellationToken = default) =>
        LogAsync(pullRequest, "Close", Inner.CloseAsync(pullRequest, comment, cancellationToken));

    private async Task LogAsync(PullRequestReview pullRequest, string action, Task run)
    {
        ArgumentNullException.ThrowIfNull(pullRequest);
        var target = $"{pullRequest.Repository} #{pullRequest.Number.ToString(CultureInfo.InvariantCulture)} at {pullRequest.HeadCommit}";
        try
        {
            await run.ConfigureAwait(false);
            _log.Write($"{action} of {target} done.");
        }
        catch (Exception exception) when (exception is StewardException or PullRequestRefusedException)
        {
            _log.Write($"{action} of {target} failed. {exception.Message}");
            throw;
        }
    }
}
