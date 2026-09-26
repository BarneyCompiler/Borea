using Borea.App.ViewModels;
using Borea.Core.Stewardship;

namespace Borea.App.Tests.ViewModels;

public sealed class ReleaseAmendmentViewModelTests
{
    private readonly StewardSession _session = new();
    private readonly FakeReleaseAmendments _amendments = new();

    [Fact]
    public async Task ContentPage_AmendReleases_PreviewsEachFileBeforeItOpensOnePullRequestOnce()
    {
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");
        var hold = _amendments.HoldOpen = new TaskCompletionSource();

        viewModel.AmendContentReleasesCommand.Execute(null);
        var dialog = viewModel.StewardAmendment!;
        await dialog.WhenDoneAsync();

        Assert.True(viewModel.IsStewardAmendmentOpen);
        Assert.Equal("Amend releases of MeasureTools", dialog.Title);
        Assert.Equal(["1.2.0", "1.1.0", "1.0.0"], dialog.Versions.Select(version => version.Version));
        Assert.False(dialog.CanPreview);

        dialog.Versions[0].IsSelected = true;
        dialog.Yank = true;
        Assert.False(dialog.CanPreview);
        dialog.Reason = "The archive carries malware.";
        Assert.True(dialog.CanPreview);
        Assert.False(dialog.CanOpen);
        await dialog.PreviewCommand.ExecuteAsync(null);

        var request = Assert.Single(_amendments.Previewed);
        Assert.Equal(("MeasureTools", "The archive carries malware."), (request.ListingId, request.Reason));
        Assert.Equal(["1.2.0"], request.Selection.Versions!);
        Assert.Equal((true, null, null, null), (request.Change.Yank, request.Change.YankReason, request.Change.GameMax, request.Change.LoaderMin));
        Assert.Empty(request.Change.AddedDependencies);
        Assert.Empty(request.Change.DependencyBounds);
        var file = Assert.Single(dialog.Files);
        Assert.Equal("releases/MeasureTools/1.2.0.json", file.File.Path);
        Assert.Equal(
            ["@@ -1,3 +1,4 @@", " {", "-  \"version\": \"1.2.0\"", "+  \"version\": \"1.2.0\",", "+  \"yanked\": true", " }"],
            file.Lines.Select(line => line.Text));
        Assert.Equal((false, true, false), (file.Lines[2].IsAdded, file.Lines[2].IsRemoved, file.Lines[0].IsAdded));
        Assert.Equal("The pull request mentions @alice, so the owner is told.", dialog.MentionText);
        Assert.False(dialog.CanPreview);
        Assert.True(dialog.CanOpen);

        var first = dialog.OpenPullRequestCommand.ExecuteAsync(null);
        var second = dialog.OpenPullRequestCommand.ExecuteAsync(null);
        Assert.False(dialog.CanClose);
        hold.SetResult();
        await Task.WhenAll(first, second);
        await dialog.WhenDoneAsync();

        Assert.Same(dialog.Preview, Assert.Single(_amendments.Opened));
        Assert.Equal("Pull request #1 is open. It waits for a steward to merge it.", dialog.OpenedText);
        Assert.False(dialog.CanOpen);
        Assert.False(dialog.CanEdit);

        dialog.CloseCommand.Execute(null);

        Assert.Null(viewModel.StewardAmendment);
    }

    [Fact]
    public async Task EveryReleaseUpToOne_WithBoundsAndDependencies_GoesIntoTheRequestAsTyped()
    {
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);

        dialog.IsScopeUpTo = true;
        dialog.UpTo = "1.1.0";
        dialog.GameMax = " 2026.8.19.5261 ";
        dialog.LoaderMin = "0.4.6";
        dialog.AddMissingDependencyCommand.Execute(null);
        dialog.Dependencies[0].Id = "BadMod";
        dialog.Dependencies[0].Max = "1.2.0";
        dialog.BoundDependencyCommand.Execute(null);
        dialog.Dependencies[1].Id = "Lib";
        dialog.Dependencies[1].Min = "2.1.0";
        dialog.Reason = "It breaks on the new build.";
        await dialog.PreviewCommand.ExecuteAsync(null);

        var request = Assert.Single(_amendments.Previewed);
        Assert.Equal(("1.1.0", null), (request.Selection.UpToVersion, request.Selection.Versions));
        Assert.Equal(("2026.8.19.5261", "0.4.6", false), (request.Change.GameMax, request.Change.LoaderMin, request.Change.Yank));
        Assert.Equal([new ReleaseDependencyAddition("BadMod", "conflict")], request.Change.AddedDependencies);
        Assert.Equal([new ReleaseDependencyBounds("BadMod", null, "1.2.0"), new ReleaseDependencyBounds("Lib", "2.1.0", null)], request.Change.DependencyBounds);
        Assert.Equal(["1.1.0", "1.0.0"], dialog.Files.Select(file => file.File.Version));

        dialog.IsScopeAll = true;

        Assert.Null(dialog.Preview);
        Assert.Empty(dialog.Files);
        Assert.False(dialog.CanOpen);
        await dialog.PreviewCommand.ExecuteAsync(null);
        Assert.Same(ReleaseSelection.All, _amendments.Previewed[^1].Selection);
    }

    [Fact]
    public async Task AnyEdit_DropsThePreview_SoWhatIsSentIsWhatWasShown()
    {
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);
        await PreviewYankAsync(dialog);
        Assert.True(dialog.CanOpen);

        dialog.Reason = "Another reason.";

        Assert.Null(dialog.Preview);
        Assert.False(dialog.CanOpen);
        Assert.True(dialog.CanPreview);

        await dialog.PreviewCommand.ExecuteAsync(null);
        dialog.Versions[1].IsSelected = true;

        Assert.Null(dialog.Preview);
        Assert.Empty(_amendments.Opened);
    }

    [Fact]
    public async Task AChangeTheClassRefuses_ShowsWhyAndSendsNoPullRequest()
    {
        _amendments.PreviewFailure = new ReleaseAmendmentRefusedException(ReleaseAmendmentRefusal.Widens, ["game_max_revision rises from 5261 to 5348", "only the verified owner of the listing widens a release"]);
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);

        await PreviewYankAsync(dialog);

        Assert.Equal(harness.Localization.StewardAmendRefusedWidens, dialog.Refusal);
        Assert.Equal("game_max_revision rises from 5261 to 5348 only the verified owner of the listing widens a release", dialog.RefusalDetails);
        Assert.Null(dialog.Preview);
        Assert.False(dialog.CanOpen);
        Assert.Empty(_amendments.Opened);

        dialog.Reason = "Another reason.";

        Assert.Null(dialog.Refusal);
    }

    [Fact]
    public async Task AReleaseFileThatChangedOnMain_ShowsTheNewPreviewAndOpensOnlyOnTheNextClick()
    {
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);
        await PreviewYankAsync(dialog, "1.2.0", "1.1.0");
        _amendments.Yanked.Add("1.1.0");
        var current = _amendments.Derive(dialog.Request);
        _amendments.OpenFailure = new ReleaseAmendmentChangedException(current);

        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Empty(_amendments.Opened);
        Assert.Same(current, dialog.Preview);
        Assert.Equal(harness.Localization.StewardAmendChanged, dialog.Notice);
        Assert.Equal((true, false), (dialog.Files[0].IsChanged, dialog.Files[1].IsChanged));
        Assert.Equal(harness.Localization.StewardAmendUnchanged, dialog.Files[1].UnchangedText);
        Assert.True(dialog.CanOpen);

        await dialog.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Same(current, Assert.Single(_amendments.Opened));
    }

    [Fact]
    public async Task EverySelectedReleaseAlreadySaysThis_OffersNothingToOpen()
    {
        _amendments.Yanked.Add("1.2.0");
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);

        await PreviewYankAsync(dialog);

        Assert.Equal(harness.Localization.StewardAmendNothing, dialog.NothingText);
        Assert.False(dialog.CanOpen);
    }

    [Theory]
    [InlineData(StewardFailure.NotSteward, "GitHub does not let this account bypass the rules of content-index-releases, so it cannot do this.")]
    [InlineData(StewardFailure.NetworkError, "Cannot reach GitHub. Try again.")]
    public async Task ReleasesThatCannotBeRead_SayWhy_AndCanBeReadAgain(StewardFailure failure, string text)
    {
        _amendments.ReleasesFailure = new StewardException(failure);
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);

        Assert.Equal(text, dialog.Error);
        Assert.True(dialog.CanRetry);
        Assert.False(dialog.HasVersions);

        _amendments.ReleasesFailure = null;
        await dialog.RetryCommand.ExecuteAsync(null);

        Assert.Null(dialog.Error);
        Assert.Equal(3, dialog.Versions.Count);
    }

    [Theory]
    [InlineData(ReleaseAmendmentRefusal.UnknownRelease, "the listing has no stamped release", "content-index-releases has no stamped release of this listing yet.")]
    [InlineData(ReleaseAmendmentRefusal.NotStamperFile, "'latest' does not parse as SemVer 2.0.0", "A release file is not one that the stamper writes. Change it by hand on GitHub.")]
    public async Task ReleasesTheClassRefuses_SayWhyWithTheDetails_AndOfferNoRetry(ReleaseAmendmentRefusal refusal, string detail, string text)
    {
        _amendments.ReleasesFailure = new ReleaseAmendmentRefusedException(refusal, detail);
        using var harness = await CreateAsync();
        var dialog = await OpenDialogAsync(harness.ViewModel);

        Assert.Equal(text, dialog.Refusal);
        Assert.Equal(detail, dialog.RefusalDetails);
        Assert.Null(dialog.Error);
        Assert.False(dialog.CanRetry);
        Assert.False(dialog.HasVersions);
    }

    [Theory]
    [InlineData("always", "never")]
    [InlineData("never", "always")]
    public async Task OnlyAStewardOfContentIndexReleases_SeesAmendReleases(string index, string releases)
    {
        _session.Bypass["KSAModding/content-index"] = index;
        _session.Bypass["KSAModding/content-index-releases"] = releases;
        using var harness = await CreateAsync();
        var viewModel = harness.ViewModel;
        await OpenListingAsync(viewModel, "MeasureTools");

        viewModel.AmendContentReleasesCommand.Execute(null);

        Assert.Equal(releases == "always", viewModel.CanAmendContentReleases);
        Assert.Equal(releases == "always", viewModel.StewardAmendment is not null);
        if (releases == "always")
            await viewModel.StewardAmendment!.WhenDoneAsync();

        viewModel.SignOutOfGitHubCommand.Execute(null);

        Assert.False(viewModel.CanAmendContentReleases);
    }

    private static async Task PreviewYankAsync(ReleaseAmendmentDialog dialog, params string[] versions)
    {
        foreach (var version in dialog.Versions.Where(version => versions.Length == 0 ? version.Version == "1.2.0" : versions.Contains(version.Version)))
            version.IsSelected = true;
        dialog.Yank = true;
        dialog.Reason = "The archive carries malware.";
        await dialog.PreviewCommand.ExecuteAsync(null);
    }

    private static async Task<ReleaseAmendmentDialog> OpenDialogAsync(MainViewModel viewModel)
    {
        await OpenListingAsync(viewModel, "MeasureTools");
        viewModel.AmendContentReleasesCommand.Execute(null);
        await viewModel.StewardAmendment!.WhenDoneAsync();
        return viewModel.StewardAmendment;
    }

    private async Task<ViewModelHarness> CreateAsync()
    {
        _session.SignInDirectly();
        var harness = await ViewModelHarness.CreateAsync(gitHub: _session, releaseAmendments: _amendments);
        await harness.ViewModel.WhenStewardRoleCheckedAsync();
        return harness;
    }

    private static async Task OpenListingAsync(MainViewModel viewModel, string id)
    {
        await viewModel.EnsureDiscoverLoadedAsync();
        await viewModel.OpenContentAsync(viewModel.DiscoverItems.First(item => item.ModId == id));
    }
}
