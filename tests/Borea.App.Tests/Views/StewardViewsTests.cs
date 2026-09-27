using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.Core.Stewardship;
using StewardPageView = Borea.App.Views.Pages.StewardPage;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class StewardViewsTests
{
    private readonly StewardSession _session = new();
    private readonly FakeIndexStatusEditor _editor = new();

    [Fact]
    public async Task StewardPage_ShowsEachStateWithLiftAndTheOpenPullRequests()
    {
        _editor.Entries.Add(new IndexStatusEntry("GoneMod", "delisted", null, "2026-09-20T10:00:00Z", "Taken down on request."));
        _editor.Pulls.Add(new IndexStatusPullRequest(3, new Uri("https://github.com/KSAModding/content-index/pull/3"), "Dispute Other", "alice", Conflicts: true));
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.OpenStewardPageCommand.Execute(null);
        await viewModel.StewardPage.WhenLoadedAsync();

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
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, indexStatusEditor: _editor);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }
}
