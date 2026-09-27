using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// IWatcherIssues on the REST API without the token. The issues are public, and an anonymous read does not depend on a permission of the App.
/// </summary>
public sealed class GitHubWatcherIssues : IWatcherIssues
{
    private readonly GitHubApi _api;

    public GitHubWatcherIssues(IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _api = new GitHubApi(session, http, time);
    }

    public async Task<WatcherIssues> ListAsync(CancellationToken cancellationToken = default)
    {
        var listings = ReadAsync(
            WatcherIssues.ListingsRepository,
            $"labels={WatcherIssues.ListingLabel}",
            _ => true,
            cancellationToken);

        // The label of the watchdog issue is dropped when the repository refuses it, so the marker finds the issue.
        // Anybody can write the marker into an issue, but only an App opens an issue as a bot.
        var watchdog = ReadAsync(
            WatcherIssues.WatchdogRepository,
            null,
            issue => issue.AuthorType == "Bot" && issue.Body?.Contains(WatcherIssues.WatchdogMarker, StringComparison.Ordinal) == true,
            cancellationToken);

        var (listingIssues, listingFailure) = await listings.ConfigureAwait(false);
        var (watchdogIssues, watchdogFailure) = await watchdog.ConfigureAwait(false);
        return new WatcherIssues(listingIssues, watchdogIssues, new[] { listingFailure, watchdogFailure }.OfType<WatcherIssuesFailure>().ToList());
    }

    /// <summary>The open issues of one repository that pass <paramref name="keep"/>, or why it could not be read.</summary>
    private async Task<(IReadOnlyList<WatcherIssue> Issues, WatcherIssuesFailure? Failure)> ReadAsync(
        string repository,
        string? query,
        Func<GitHubIssue, bool> keep,
        CancellationToken cancellationToken)
    {
        try
        {
            var issues = new List<WatcherIssue>();
            await foreach (var issue in _api.GetOpenIssuesAsync(repository, query, cancellationToken).ConfigureAwait(false))
            {
                if (keep(issue))
                    issues.Add(new WatcherIssue(repository, issue.Number, issue.Url, issue.Title, issue.Updated, WatcherIssues.ListingOf(issue.Body)));
            }

            return (issues, null);
        }
        catch (GitHubApiException exception)
        {
            return ([], new WatcherIssuesFailure(repository, exception.ToStewardException()));
        }
    }
}
