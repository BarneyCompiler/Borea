using System.Text.Json;
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
            issue => issue.User?.Type == "Bot" && issue.Body?.Contains(WatcherIssues.WatchdogMarker, StringComparison.Ordinal) == true,
            cancellationToken);

        var (listingIssues, listingFailure) = await listings.ConfigureAwait(false);
        var (watchdogIssues, watchdogFailure) = await watchdog.ConfigureAwait(false);
        return new WatcherIssues(listingIssues, watchdogIssues, new[] { listingFailure, watchdogFailure }.OfType<WatcherIssuesFailure>().ToList());
    }

    /// <summary>The open issues of one repository that pass <paramref name="keep"/>, or why it could not be read.</summary>
    private async Task<(IReadOnlyList<WatcherIssue> Issues, WatcherIssuesFailure? Failure)> ReadAsync(
        string repository,
        string? query,
        Func<IssueDto, bool> keep,
        CancellationToken cancellationToken)
    {
        try
        {
            var issues = new List<WatcherIssue>();
            var url = $"{GitHubApi.Root}/repos/{repository}/issues?state=open&sort=created&direction=asc" + (query is null ? string.Empty : "&" + query);
            await foreach (var issue in _api.GetPagesAsync<IssueDto>(url, cancellationToken, anonymous: true).ConfigureAwait(false))
            {
                // the issues endpoint lists the pull requests too
                if (issue.PullRequest is not null || !keep(issue))
                    continue;

                if (!Uri.TryCreate(issue.HtmlUrl, UriKind.Absolute, out var link) || link.Scheme != Uri.UriSchemeHttps || issue.Number <= 0)
                    throw new GitHubApiException(GitHubApiFailure.UnexpectedResponse);

                issues.Add(new WatcherIssue(repository, issue.Number, link, issue.Title, issue.UpdatedAt, WatcherIssues.ListingOf(issue.Body)));
            }

            return (issues, null);
        }
        catch (GitHubApiException exception)
        {
            return ([], new WatcherIssuesFailure(repository, exception.ToStewardException()));
        }
    }

    private sealed class IssueDto
    {
        public int Number { get; set; }

        public string Title { get; set; } = string.Empty;

        public string HtmlUrl { get; set; } = string.Empty;

        public string? Body { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public UserDto? User { get; set; }

        public JsonElement? PullRequest { get; set; }
    }

    private sealed class UserDto
    {
        public string? Type { get; set; }
    }
}
