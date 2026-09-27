using System.Text.RegularExpressions;
using Borea.Core.Listings;

namespace Borea.Core.Stewardship;

/// <summary>
/// The issues that tell a steward the watcher of content-index-releases needs help. The watcher itself opens, updates and closes
/// one issue in content-index for each listing it cannot stamp. The watchdog workflow of tools/watchdog.py opens one issue in
/// content-index-releases while the watcher does not tick, and closes it when the watcher runs again.
/// </summary>
public interface IWatcherIssues
{
    /// <summary>
    /// Reads the open issues of both repositories without the token, because they are public, oldest first.
    /// A repository that cannot be read is named in <see cref="WatcherIssues.Failures"/>, and the other one still shows.
    /// </summary>
    Task<WatcherIssues> ListAsync(CancellationToken cancellationToken = default);
}

/// <param name="Listings">The open issues of content-index with the label <see cref="ListingLabel"/>.</param>
/// <param name="Watchdog">The open issues of content-index-releases that say the watcher does not tick. The watchdog keeps at most one.</param>
public sealed partial record WatcherIssues(IReadOnlyList<WatcherIssue> Listings, IReadOnlyList<WatcherIssue> Watchdog, IReadOnlyList<WatcherIssuesFailure> Failures)
{
    /// <summary>The label of the listing issues, as tools/watch.py of content-index-releases sets it.</summary>
    public const string ListingLabel = "watcher";

    /// <summary>The start of the marker that tools/watchdog.py writes into its issue, followed by the workflow it watches.</summary>
    public const string WatchdogMarker = "<!-- watchdog:workflow=";

    public const string ListingsRepository = ListingPullRequestLinks.Repository;

    public const string WatchdogRepository = StewardAccess.ReleasesRepository;

    /// <summary>
    /// The watcher workflow on GitHub, where a steward runs a backfill. Borea asks for no Actions permission, so it does not start the run itself.
    /// </summary>
    public static Uri WorkflowUrl { get; } = new($"https://github.com/{StewardAccess.ReleasesRepository}/actions/workflows/watcher.yml");

    /// <summary>The listing id that the marker <c>&lt;!-- watcher:listing=&lt;id&gt; --&gt;</c> of tools/watch.py names in an issue body, or null.</summary>
    public static string? ListingOf(string? body) =>
        body is not null && ListingMarker().Match(body) is { Success: true } match ? match.Groups["id"].Value : null;

    [GeneratedRegex(@"<!-- watcher:listing=(?<id>\S+) -->")]
    private static partial Regex ListingMarker();
}

/// <param name="Repository">The repository as owner/name.</param>
/// <param name="Updated">When the watcher or anybody else last changed it.</param>
/// <param name="ListingId">The listing that its marker names, or null when it has no marker.</param>
public sealed record WatcherIssue(string Repository, int Number, Uri Url, string Title, DateTimeOffset Updated, string? ListingId);

/// <param name="Repository">The repository that could not be read, as owner/name.</param>
public sealed record WatcherIssuesFailure(string Repository, StewardException Error);
