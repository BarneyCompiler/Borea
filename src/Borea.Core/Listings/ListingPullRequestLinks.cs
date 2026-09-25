namespace Borea.Core.Listings;

/// <summary>
/// The GitHub pages that start the pull request of one document in content-index, at its <see cref="ListingDraft.Path"/>.
/// For someone without write access, GitHub creates the fork and then shows the pull request form.
/// </summary>
public static class ListingPullRequestLinks
{
    public const string Repository = "KSAModding/content-index";

    public const string Branch = "main";

    /// <summary>
    /// A longer URL is not opened with the document in it, because browsers and GitHub cut long URLs.
    /// GitHub already refused a new-file URL of about 5,900 characters, so the limit stays well below that.
    /// </summary>
    public const int MaxUrlLength = 2000;

    /// <summary>
    /// The new-file page of the document. GitHub fills the file from the undocumented value parameter, so the
    /// text is left out when the URL would be longer than <see cref="MaxUrlLength"/>, and the author pastes it.
    /// </summary>
    public static ListingPullRequestPage NewFile(string path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var page = NewFileWithoutText(path);
        var withText = $"{page.Url.AbsoluteUri}&value={Uri.EscapeDataString(text)}";
        return withText.Length <= MaxUrlLength ? new ListingPullRequestPage(new Uri(withText), CarriesText: true) : page;
    }

    /// <summary>The new-file page of the document with an empty file, for the author to paste the text into.</summary>
    public static ListingPullRequestPage NewFileWithoutText(string path) =>
        new(new Uri($"https://github.com/{Repository}/new/{Branch}?filename={Escape(path)}"), CarriesText: false);

    /// <summary>The edit page of a listed document. It never carries the text, so the author pastes it.</summary>
    public static ListingPullRequestPage Edit(string path) =>
        new(new Uri($"https://github.com/{Repository}/edit/{Branch}/{Escape(path)}"), CarriesText: false);

    private static string Escape(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }
}

/// <param name="CarriesText">Whether the page opens with the document filled in.</param>
public sealed record ListingPullRequestPage(Uri Url, bool CarriesText);
