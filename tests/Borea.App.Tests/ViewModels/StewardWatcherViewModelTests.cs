using Borea.App.ViewModels;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class StewardWatcherViewModelTests
{
    private const string Index = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeWatcherIssues _watcher = new();

    [Fact]
    public async Task WatcherTab_ListsEachListingIssue_WithTheListingOfItsMarker()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(103, null));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(104, "GoneMod"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;

        var tab = await OpenAsync(viewModel);

        Assert.True(viewModel.StewardPage.IsWatcherTab);
        Assert.Equal(["content-index #102", "content-index #103", "content-index #104"], tab.Listings.Select(issue => issue.NumberText));
        var named = tab.Listings[0];
        Assert.Equal(("MeasureTools: the watcher found a problem", true, false, false), (named.Title, named.CanOpenListing, named.IsListingUnknown, named.HasNoListing));
        Assert.Equal(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name, named.ListingName);
        Assert.Equal("updated " + viewModel.AgeText(named.Issue.Updated), named.UpdatedText);
        var unmarked = tab.Listings[1];
        Assert.Equal((false, false, true), (unmarked.CanOpenListing, unmarked.IsListingUnknown, unmarked.HasNoListing));
        var unknown = tab.Listings[2];
        Assert.Equal((false, true, false), (unknown.CanOpenListing, unknown.IsListingUnknown, unknown.HasNoListing));
        Assert.False(tab.IsListingsEmpty);
        Assert.Empty(tab.Failures);
    }

    [Fact]
    public async Task ListingOfAnIssue_OpensItsContentPage()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var tab = await OpenAsync(viewModel);

        await tab.Listings[0].OpenListingCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowContent);
        Assert.False(viewModel.CurrentWindowSteward);
        Assert.Equal("MeasureTools", viewModel.SelectedContent?.ModId);
    }

    [Fact]
    public async Task IssueAndWorkflow_OpenOnGitHub()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        var tab = await OpenAsync(viewModel);

        tab.Listings[0].OpenCommand.Execute(null);
        tab.Watchdog[0].OpenCommand.Execute(null);
        tab.OpenWorkflowCommand.Execute(null);

        Assert.Equal(
            [$"https://github.com/{Index}/issues/102", $"https://github.com/{Releases}/issues/81", $"https://github.com/{Releases}/actions/workflows/watcher.yml"],
            opened);
        Assert.Null(tab.Error);
    }

    [Fact]
    public async Task OpenWatchdogIssue_ShowsThatTheWatcherDoesNotTick()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        var watchdog = Assert.Single(tab.Watchdog);
        Assert.Equal(("content-index-releases #81", "The watcher is not ticking"), (watchdog.NumberText, watchdog.Title));
        Assert.False(tab.IsTicking);
        Assert.True(tab.IsListingsEmpty);
    }

    [Fact]
    public async Task NoWatchdogIssue_SaysThatTheWatcherTicks()
    {
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Empty(tab.Watchdog);
        Assert.True(tab.IsTicking);
        Assert.True(tab.IsListingsEmpty);
    }

    [Fact]
    public async Task ListingsFail_SaysWhy_AndTheWatchdogStillShows()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        _watcher.Failures.Add(new WatcherIssuesFailure(Index, new StewardException(StewardFailure.NetworkError)));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal(["Cannot read the issues of KSAModding/content-index. Cannot reach GitHub. Try again."], tab.Failures);
        Assert.Equal([81], tab.Watchdog.Select(issue => issue.Issue.Number));
        Assert.False(tab.IsListingsEmpty);
        Assert.Null(tab.Error);
    }

    [Theory]
    [InlineData(StewardFailure.Forbidden, "Cannot read the issues of KSAModding/content-index-releases. GitHub refused access.")]
    [InlineData(StewardFailure.NotFound, "Cannot read the issues of KSAModding/content-index-releases. GitHub did not find the repository.")]
    public async Task WatchdogFails_SaysWhy_AndTheListingsStillShow(StewardFailure failure, string text)
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Failures.Add(new WatcherIssuesFailure(Releases, new StewardException(failure)));
        using var harness = await CreateAsync();

        var tab = await OpenAsync(harness.ViewModel);

        Assert.Equal([text], tab.Failures);
        Assert.Equal([102], tab.Listings.Select(issue => issue.Issue.Number));
        Assert.False(tab.IsTicking);
    }

    [Fact]
    public async Task Refresh_ReadsTheWatcherTab_AndTheTabReadsOnlyOnceWhenItShowsAgain()
    {
        using var harness = await CreateAsync();
        var page = harness.ViewModel.StewardPage;
        await OpenAsync(harness.ViewModel);

        await page.ShowQueueCommand.ExecuteAsync(null);
        await page.ShowWatcherCommand.ExecuteAsync(null);
        await page.RefreshTabCommand.ExecuteAsync(null);

        Assert.Equal(2, _watcher.Reads);
        Assert.Single(_queue.Filters);
        Assert.Equal(0, _editor.Reads);
    }

    [Fact]
    public async Task LanguageSwitch_ShowsTheWatcherTabInTheNewLanguage()
    {
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Failures.Add(new WatcherIssuesFailure(Releases, new StewardException(StewardFailure.Forbidden)));
        using var harness = await CreateAsync();
        var tab = await OpenAsync(harness.ViewModel);

        harness.Localization.TrySetCulture("de");

        Assert.Equal(["Die Issues von KSAModding/content-index-releases sind nicht lesbar. GitHub hat den Zugriff verweigert."], tab.Failures);
        Assert.StartsWith("aktualisiert ", Assert.Single(tab.Listings).UpdatedText, StringComparison.Ordinal);
        Assert.Equal(1, _watcher.Reads);
    }

    private static async Task<StewardWatcherTab> OpenAsync(MainViewModel viewModel)
    {
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowWatcherCommand.ExecuteAsync(null);
        await viewModel.StewardPage.Watcher.WhenLoadedAsync();
        return viewModel.StewardPage.Watcher;
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, watcherIssues: _watcher);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
