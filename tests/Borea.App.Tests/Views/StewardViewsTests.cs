using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Listings;
using Borea.Core.Stewardship;
using StewardPageView = Borea.App.Views.Pages.StewardPage;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class StewardViewsTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();
    private readonly FakeStewardQueue _queue = new();
    private readonly FakeWatcherIssues _watcher = new();
    private readonly FakePullRequestReviews _reviews = new();

    [Fact]
    public async Task StewardPage_ShowsEachStateWithLiftAndTheOpenPullRequests()
    {
        _editor.Entries.Add(new IndexStatusEntry("GoneMod", "delisted", null, "2026-09-20T10:00:00Z", "Taken down on request."));
        _editor.Pulls.Add(new IndexStatusPullRequest(3, new Uri("https://github.com/KSAModding/content-index/pull/3"), "Dispute Other", "alice", Conflicts: true));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.ShowStatusCommand.ExecuteAsync(null);

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardHeading, texts);
        Assert.Contains("GoneMod", texts);
        Assert.Contains("Delisted", texts);
        Assert.Contains("Taken down on request.", texts);
        Assert.Contains("#3 Dispute Other, by alice", texts);
        Assert.Contains(harness.Localization.StewardStatusConflict, texts);
        Assert.Contains(harness.Localization.StewardLift, buttons);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task StewardPage_OpensOnTheQueue_AndShowsEachPullRequestWithItsKindsAndVerdict()
    {
        _queue.Items.Add(FakeStewardQueue.Item(26) with { IsDraft = true, HasOtherFiles = true, Kinds = [StewardQueueKind.Listing, StewardQueueKind.IndexStatus] });
        _queue.Items.Add(FakeStewardQueue.Item(27, verdict: "Validated, and ownership is not verified, so a steward decides."));
        _queue.Failures.Add(new StewardQueueFailure("KSAModding/content-index-releases", new StewardException(StewardFailure.Forbidden)));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        viewModel.StewardPage.Queue.SelectScopeCommand.Execute(StewardQueueScope.All);

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Queue (2)", buttons);
        Assert.Contains(harness.Localization.StewardTabStatus, buttons);
        Assert.Contains(harness.Localization.StewardQueueScopeAll, texts);
        Assert.Contains(harness.Localization.StewardQueueAllOpen, texts);
        Assert.Contains("content-index #26", texts);
        Assert.Contains("Pull 26", texts);
        Assert.Contains(harness.Localization.StewardQueueDraft, texts);
        Assert.Contains(harness.Localization.StewardQueueOtherFiles, texts);
        Assert.Contains("Index status", texts);
        Assert.Equal(2, texts.Count(text => text == "Listing"));
        Assert.Contains(harness.Localization.StewardQueueNoVerdict, texts);
        Assert.Contains("Validated, and ownership is not verified, so a steward decides.", texts);
        Assert.Contains(viewModel.StewardQueueFailureText(_queue.Failures[0]), texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueWaiting, texts);
        Assert.DoesNotContain(harness.Localization.StewardStatusHint, texts);
    }

    [Fact]
    public async Task WatcherTab_ShowsTheWatchdogIssue_EachListingIssue_AndTheFailures()
    {
        _watcher.Watchdog.Add(FakeWatcherIssues.WatchdogIssue(81));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(102, "MeasureTools"));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(103, null));
        _watcher.Listings.Add(FakeWatcherIssues.Listing(104, "GoneMod"));
        _watcher.Failures.Add(new WatcherIssuesFailure("KSAModding/content-index", new StewardException(StewardFailure.RateLimited, retryAt: DateTimeOffset.Now.AddMinutes(20))));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.ShowWatcherCommand.ExecuteAsync(null);
        var name = viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools").Name;

        var (texts, buttons) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var visible = window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string).ToList();
                return Task.FromResult((shown, visible));
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(harness.Localization.StewardTabWatcher, buttons);
        Assert.Contains(harness.Localization.StewardWatcherHint, texts);
        Assert.Contains(harness.Localization.StewardWatcherWorkflow, texts);
        Assert.Contains(harness.Localization.StewardWatcherNotTicking, texts);
        Assert.Contains("content-index-releases #81", texts);
        Assert.Contains("The watcher is not ticking", texts);
        Assert.DoesNotContain(harness.Localization.StewardWatcherTicking, texts);
        Assert.Contains(name, texts);
        Assert.Contains("MeasureTools: the watcher found a problem", texts);
        Assert.Contains(harness.Localization.StewardWatcherNoListing, texts);
        Assert.Contains("GoneMod", texts);
        Assert.Contains(harness.Localization.StewardWatcherUnknownListing, texts);
        Assert.Contains(viewModel.StewardWatcherFailureText(_watcher.Failures[0]), texts);
        Assert.DoesNotContain(harness.Localization.StewardWatcherListingsEmpty, texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task ReviewPage_ShowsThePullRequestInPlaceOfTheTabs_WithItsDocumentAndFiles()
    {
        const string Listing = "spec_version = 1\nid = \"MyMod\"\nname = \"My Mod\"\nabstract = \"Adds a thing.\"\ndescription = \"It adds a thing.\"\n";
        _queue.Items.Add(FakeStewardQueue.Item(5));
        using var harness = await CreateAsync();
        var pull = FakePullRequestReviews.Review(5, headRepository: "alice/content-index") with { Title = "Add My Mod", Verdict = "Validated, and ownership is not verified." };
        _reviews.Reviews[("KSAModding/content-index", 5)] = pull with
        {
            Files = [FakePullRequestReviews.File(pull, "listings/MyMod.toml", "@@ -0,0 +1,2 @@\n+id = \"MyMod\""), FakePullRequestReviews.File(pull, "packs/my-pack/icon.png", null)],
            Documents =
            [
                new PullRequestDocument("listings/MyMod.toml", StewardQueueKind.Listing, Listing, harness.Services.ListingFormat.Read(Listing), null, new ListingOwnership(ListingOwnershipState.Verified, ListingOwnershipProof.Owner, Repository: "alice/MyMod")),
                new PullRequestDocument("listings/Broken.toml", StewardQueueKind.Listing, "name = ", null, "Expected a value.", null),
            ],
        };
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.Queue.WhenLoadedAsync();
        await viewModel.StewardPage.Queue.Items[0].OpenCommand.ExecuteAsync(null);

        var texts = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new Window { Width = 1280, Height = 832, Content = new StewardPageView(), DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text);
                var markdown = window.GetVisualDescendants().OfType<MarkdownView>().Where(view => view.IsEffectivelyVisible).Select(view => view.Markdown);
                return Task.FromResult(shown.Concat(markdown).ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("content-index #5", texts);
        Assert.Contains("Add My Mod", texts);
        Assert.Contains("from alice/content-index", texts);
        Assert.Contains("Head commit 0123456", texts);
        Assert.Contains("validate: passed", texts);
        Assert.Contains(harness.Localization.StewardReviewRunChecks, texts);
        Assert.Contains("Validated, and ownership is not verified.", texts);
        Assert.Contains("My Mod", texts);
        Assert.Contains("Adds a thing.", texts);
        Assert.Contains("It adds a thing.", texts);
        Assert.Contains("alice owns alice/MyMod.", texts);
        Assert.Contains("The document does not parse: Expected a value.", texts);
        Assert.Contains("Changed files (2)", texts);
        Assert.Contains("+id = \"MyMod\"", texts);
        Assert.Contains(harness.Localization.StewardReviewNoPatch, texts);
        Assert.DoesNotContain(harness.Localization.StewardQueueHint, texts);
    }

    [Fact]
    public async Task IndexStatusModal_NamesTheChangeAndOpensThePullRequestOnAClick()
    {
        _editor.Check = _ => new IndexStatusCheck(null, [new IndexStatusPullRequest(4, new Uri("https://github.com/KSAModding/content-index/pull/4"), "Dispute Other", "bob")], ["alice"], IsOwner: false);
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == "MeasureTools"));
        viewModel.DelistContentCommand.Execute(null);
        await viewModel.StewardChange!.WhenDoneAsync();
        viewModel.StewardChange.Reason = "The author asked for it.";

        var texts = await HeadlessApp.RunAsync(harness, async () =>
        {
            var modal = new IndexStatusModal();
            var window = new Window { Width = 1280, Height = 832, Content = modal, DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var shown = modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
                var open = modal.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && (button.Content as TextBlock)?.Text == harness.Localization.ListingPublish);
                var point = open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                await viewModel.StewardChange.WhenDoneAsync();
                window.UpdateLayout();
                shown.AddRange(modal.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
                return shown;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Delist MeasureTools", texts);
        Assert.Contains(harness.Localization.StewardDelistEffect, texts);
        Assert.Contains(harness.Localization.StewardPullRequestExplanation, texts);
        Assert.Contains("Pull request #4 also changes index-status.toml. The one that merges second will have a conflict.", texts);
        Assert.Contains("The pull request mentions @alice, so the owner is told.", texts);
        Assert.Contains("Pull request #1 is open. It waits for a steward to merge it.", texts);
        Assert.Single(_editor.Opened);
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor, stewardQueue: _queue, watcherIssues: _watcher, pullRequestReviews: _reviews);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
