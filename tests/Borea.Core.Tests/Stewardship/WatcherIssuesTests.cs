using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class WatcherIssuesTests
{
    [Fact]
    public void ListingOf_TakesTheIdFromTheMarkerOfTheWatcher()
    {
        const string body = "<!-- watcher:listing=Compendium -->\n<!-- watcher:signature=28a9f08f3c40a6e8 -->\nThe watcher found a problem with `Compendium`.";

        Assert.Equal("Compendium", WatcherIssues.ListingOf(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("The watcher found a problem with `Compendium`.")]
    [InlineData("<!-- watcher:signature=28a9f08f3c40a6e8 -->")]
    [InlineData("<!-- watcher:listing= -->")]
    public void ListingOf_NoMarker_IsNull(string? body)
    {
        Assert.Null(WatcherIssues.ListingOf(body));
    }

    [Fact]
    public void WorkflowUrl_IsTheWatcherWorkflowOfTheReleases()
    {
        Assert.Equal("https://github.com/KSAModding/content-index-releases/actions/workflows/watcher.yml", WatcherIssues.WorkflowUrl.AbsoluteUri);
    }
}
