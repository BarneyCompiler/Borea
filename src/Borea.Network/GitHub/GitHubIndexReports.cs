using Borea.Core.GitHub;
using Borea.Core.Stewardship;

namespace Borea.Network.GitHub;

/// <summary>
/// IIndexReports on the REST API without the token. The Borea App has no Issues permission, and the issues of content-index are public.
/// </summary>
public sealed class GitHubIndexReports : IIndexReports
{
    private readonly GitHubApi _api;

    public GitHubIndexReports(IGitHubSession session, HttpClient http, TimeProvider? time = null)
    {
        _api = new GitHubApi(session, http, time);
    }

    public async Task<IReadOnlyList<IndexReport>> ListAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The label of the forms can be taken off a report, so the tag in the title finds it.
            var reports = new List<IndexReport>();
            await foreach (var issue in _api.GetOpenIssuesAsync(IndexReport.Repository, null, cancellationToken).ConfigureAwait(false))
            {
                if (IndexReport.FromIssue(issue.Number, issue.Url, issue.Title, issue.Author, issue.Created, issue.Body) is { } report)
                    reports.Add(report);
            }

            return reports;
        }
        catch (GitHubApiException exception)
        {
            throw exception.ToStewardException();
        }
    }
}
