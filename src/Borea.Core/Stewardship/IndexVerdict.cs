using Borea.Core.Listings;

namespace Borea.Core.Stewardship;

/// <summary>
/// The verdict comment that the checks of an index repository write on each pull request. Anybody can write the marker
/// into a comment, and another App can comment too, so only the indexer bot's comment with the marker of that repository counts.
/// </summary>
public static class IndexVerdict
{
    public const string BotLogin = "ksamodding-indexer-bot[bot]";

    /// <summary>The marker that the checks of <paramref name="repository"/> put into their comment.</summary>
    /// <exception cref="ArgumentException">The repository is no index repository.</exception>
    public static string MarkerOf(string repository) =>
        string.Equals(repository, ListingPullRequestLinks.Repository, StringComparison.OrdinalIgnoreCase) ? "<!-- content-index:verdict -->"
        : string.Equals(repository, StewardAccess.ReleasesRepository, StringComparison.OrdinalIgnoreCase) ? "<!-- content-index-releases:verdict -->"
        : throw new ArgumentException($"{repository} is no index repository.", nameof(repository));

    /// <summary>The text of the first comment that counts, without its marker, or null.</summary>
    public static string? Find(string repository, IEnumerable<(string? Author, string? Body)> comments)
    {
        ArgumentNullException.ThrowIfNull(comments);
        var marker = MarkerOf(repository);
        foreach (var (author, body) in comments)
        {
            if (string.Equals(author, BotLogin, StringComparison.OrdinalIgnoreCase) && body?.Contains(marker, StringComparison.Ordinal) == true)
                return body.Replace(marker, string.Empty, StringComparison.Ordinal).Trim() is { Length: > 0 } text ? text : null;
        }

        return null;
    }
}
