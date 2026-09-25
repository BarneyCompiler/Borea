using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Core.Listings;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ListingEditorTests
{
    private const string Repository = """
        {
          "name": "KSA-MyMod", "full_name": "owner/KSA-MyMod", "html_url": "https://github.com/owner/KSA-MyMod",
          "description": "Does a thing.", "homepage": "https://forums.ahwoo.com/threads/my-mod.42/", "has_issues": true,
          "owner": { "login": "owner", "type": "User" }, "license": { "spdx_id": "MIT" }
        }
        """;

    private static readonly string Releases = $$"""
        [ { "tag_name": "v1.0.0", "draft": false, "published_at": "2026-09-08T00:00:00Z", "assets": [
          { "name": "MyMod.zip", "state": "uploaded", "size": {{Archive().Length}}, "browser_download_url": "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" }
        ] } ]
        """;

    internal const string StarMapListing = """
        spec_version = 1
        id = "StarMap"
        type = "mod-loader"
        name = "StarMap"
        authors = ["KlaasWhite"]
        abstract = "Mod loader that runs code mods for Kitten Space Agency."
        license = "MIT"
        tags = ["library"]

        [releases]
        github = "StarMapLoader/StarMap"

        [links]
        forums = "https://forums.ahwoo.com/threads/starmap-mod-loader.384/"

        [compatibility]
        game_min = "2026.8.3.5117"

        [install]
        target = "standalone"

        [provides]
        launch = "StarMap.exe"
        content-dir = "mods"
        """;

    [Fact]
    public async Task OpenListing_ShowsTheStartStepWithTheListedMods()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();

        await viewModel.OpenListingAsync();

        Assert.True(viewModel.CurrentWindowListing);
        Assert.False(viewModel.CurrentWindowDiscover);
        Assert.True(viewModel.IsDiscoverSection);
        Assert.True(viewModel.ListingEditor.IsStartStep);
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools", "StarMap"], viewModel.ListingEditor.ListedMatches.Select(listing => listing.Id));

        viewModel.SetMainWindowHome();

        Assert.False(viewModel.CurrentWindowListing);
        Assert.False(viewModel.IsDiscoverSection);
    }

    [Fact]
    public async Task ReadSource_GitHubRepository_FillsTheFormFromTheHostTheArchiveAndTheForums()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        var opening = harness.ViewModel.OpenListingAsync();
        editor.SourceText = "https://github.com/owner/KSA-MyMod";

        await editor.ReadSourceCommand.ExecuteAsync(null);
        await opening;

        Assert.Null(editor.SourceError);
        Assert.True(editor.IsFormStep);
        Assert.False(editor.IsEdit);
        Assert.Equal("MyMod", editor.Id);
        Assert.Equal("KSA-MyMod", editor.Name);
        Assert.Equal("owner", editor.Authors);
        Assert.Equal("MIT", editor.License);
        Assert.Equal("https://forums.ahwoo.com/threads/my-mod.42/", editor.Forums);
        Assert.Equal("owner/KSA-MyMod", editor.ReleasesGitHub);
        Assert.True(editor.UsesLoader);
        Assert.Equal("0.4.6", editor.LoaderMin);
        Assert.Equal("2026.9.7.5402", editor.GameMin);
        Assert.True(editor.CuratedTags.Single(chip => chip.Tag == "gameplay").IsSelected);
        Assert.Contains("MyMod", editor.ArchiveText);
        Assert.Contains("id = \"MyMod\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("[loader]\nid = \"StarMap\"\nmin = \"0.4.6\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
        Assert.True(editor.CanOpenPullRequest);
    }

    [Fact]
    public async Task ReadSource_TextThatNamesNoHost_SaysSo()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        editor.SourceText = "not a source";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.ListingSourceInvalid, editor.SourceError);
        Assert.True(editor.IsStartStep);
    }

    [Fact]
    public async Task ReadSource_UnknownRepository_StaysOnTheStartStep()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var editor = harness.ViewModel.ListingEditor;
        editor.SourceText = "owner/gone";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.Contains("owner/gone", editor.SourceError);
        Assert.True(editor.IsStartStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadSource_CancelledOrPageLeft_StopsWithoutLoadingOrError(bool leavePage)
    {
        ViewModelHarness? owner = null;
        var serve = Serve();
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                if (leavePage)
                    owner!.ViewModel.SetMainWindowHome();
                else
                    owner!.ViewModel.ListingEditor.CancelCommand.Execute(null);
            }

            return serve(request);
        });
        owner = harness;
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.SourceText = "owner/KSA-MyMod";

        await editor.ReadSourceCommand.ExecuteAsync(null);

        Assert.True(editor.IsStartStep);
        Assert.Null(editor.SourceError);
        Assert.False(editor.IsBusy);
        Assert.Equal(string.Empty, editor.Id);
    }

    [Fact]
    public async Task Forums_TypedThreadLink_SelectsTheTagsOfItsPrefix()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        editor.ForumsDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        await editor.TagProposal;

        Assert.True(editor.CuratedTags.Single(chip => chip.Tag == "gameplay").IsSelected);
        Assert.Contains("tags = [\"gameplay\"]\n", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forums_TagAlreadyChosen_IsLeftAsItIs()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: Serve(), editSnapshot: WithGameplayPrefix);
        var editor = harness.ViewModel.ListingEditor;
        editor.ForumsDelay = TimeSpan.Zero;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.CuratedTags.Single(chip => chip.Tag == "library").IsSelected = true;

        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        await editor.TagProposal;

        Assert.Equal(["library"], editor.CuratedTags.Where(chip => chip.IsSelected).Select(chip => chip.Tag));
    }

    [Fact]
    public async Task Fields_ChecksFollowWhatTheAuthorTypes()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        Assert.True(editor.HasErrors);
        Assert.False(editor.CanOpenPullRequest);

        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";

        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
        Assert.True(editor.CanOpenPullRequest);

        editor.License = "MIT, GPL";

        Assert.Contains(editor.Errors, issue => issue.Location == "license");
        Assert.False(editor.CanOpenPullRequest);

        editor.License = "MIT";
        editor.ReleasesSpaceDock = "abc";

        Assert.Contains(editor.Errors, issue => issue.Location == "releases.spacedock" && issue.Message == "'abc' is not a number.");
    }

    [Fact]
    public async Task Overview_NewForm_ListsWhatIsMissingInsteadOfErrors()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        Assert.True(editor.HasErrors);
        Assert.Empty(editor.VisibleErrors);
        Assert.Contains(localization.ListingName, editor.MissingText);
        Assert.Contains(localization.LinkForum, editor.MissingText);
        var steps = editor.Steps.ToDictionary(step => step.Key);
        Assert.Equal(ListingStepState.ToDo, steps["about"].State);
        Assert.Equal(ListingStepState.ToDo, steps["links"].State);
        Assert.Equal(localization.ListingStepRecommended, steps["releases"].Detail);
        Assert.Equal(ListingStepState.Optional, steps["dependencies"].State);
        Assert.Equal(localization.ListingPreviewName, editor.PreviewName);
    }

    [Fact]
    public async Task Overview_EmptyRowsAreMissing_AndARealErrorStillShowsWithItsName()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.AddIconCommand.Execute(null);
        editor.AddDependencyCommand.Execute(null);

        Assert.Contains(localization.ListingIcon, editor.MissingText);
        Assert.Contains(localization.FormatListingDependencyNumber(1), editor.MissingText);
        Assert.Empty(editor.VisibleErrors);

        editor.License = "MIT, GPL";

        var error = Assert.Single(editor.VisibleErrors);
        Assert.StartsWith(localization.ListingLicense + ": ", error, StringComparison.Ordinal);
        Assert.Equal(ListingStepState.Fix, editor.Steps.Single(step => step.Key == "about").State);
    }

    [Fact]
    public async Task Overview_FilledSections_AreDone()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);

        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";

        Assert.Null(editor.MissingText);
        Assert.Empty(editor.VisibleErrors);
        var steps = editor.Steps.ToDictionary(step => step.Key);
        Assert.Equal(ListingStepState.Done, steps["about"].State);
        Assert.Equal(ListingStepState.Done, steps["links"].State);
        Assert.Equal(ListingStepState.Done, steps["releases"].State);
        Assert.Equal("My Mod", editor.PreviewName);
        Assert.Equal(harness.Localization.FormatContentByAuthor("Maxi"), editor.PreviewAuthors);
    }

    [Fact]
    public async Task OpenPullRequest_NewListing_OpensTheNewFilePageWithTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        var url = Assert.Single(opened);
        Assert.StartsWith("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml&value=spec_version%20%3D%201%0A", url, StringComparison.Ordinal);
        Assert.True(editor.LastPullRequestPage!.CarriesText);
        Assert.Null(window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedWithText, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_FileTooLongForTheUrl_OpensThePageAndCopiesTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);
        editor.Description = string.Concat(Enumerable.Repeat("A long description line.\n", 400));

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml", Assert.Single(opened));
        Assert.False(editor.LastPullRequestPage!.CarriesText);
        Assert.Equal(editor.DocumentText, window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedPaste, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_PageWithTheFileDoesNotOpen_OpensItEmptyAndCopiesTheFile()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidNewListingAsync(harness);
        harness.ViewModel.OpenWithSystem = url =>
        {
            if (url.Contains("&value=", StringComparison.Ordinal))
                throw new Win32Exception("The URL is too long.");
            opened.Add(url);
        };

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=listings/MyMod.toml", Assert.Single(opened));
        Assert.Equal(editor.DocumentText, window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedPaste, editor.OutputMessage);
    }

    [Fact]
    public async Task OpenPullRequest_NoBrowser_SaysWhyWithoutTheUrl()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        harness.ViewModel.OpenWithSystem = _ => throw new Win32Exception("No browser.");

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal(harness.Localization.FormatListingOpenFailed("No browser."), editor.OutputMessage);
        Assert.Equal(editor.DocumentText, window.CopiedText);
    }

    [Fact]
    public async Task LoadListed_Loader_KeepsItsTablesAndOpensTheEditPage()
    {
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri!.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/StarMap.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(StarMapListing) }
                : null);
        var editor = harness.ViewModel.ListingEditor;
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "starm";

        await editor.LoadListedCommand.ExecuteAsync(null);
        editor.Abstract = "Runs code mods.";
        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.True(editor.IsEdit);
        Assert.False(editor.CanUseLoader);
        Assert.EndsWith("[install]\ntarget = \"standalone\"\n\n[provides]\nlaunch = \"StarMap.exe\"\ncontent-dir = \"mods\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("abstract = \"Runs code mods.\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal("https://github.com/KSAModding/content-index/edit/main/listings/StarMap.toml", Assert.Single(opened));
        Assert.Equal(editor.DocumentText, window.CopiedText);
    }

    [Fact]
    public async Task LoadListed_Since_IsReadIntoItsFieldAndWrittenBack()
    {
        var listed = StarMapListing.Replace("github = \"StarMapLoader/StarMap\"", "github = \"StarMapLoader/StarMap\"\nsince = \"1.2\"", StringComparison.Ordinal);
        using var harness = await ViewModelHarness.CreateAsync(respond: request =>
            request.RequestUri!.AbsoluteUri == "https://raw.githubusercontent.com/KSAModding/content-index/main/listings/StarMap.toml"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(listed) }
                : null);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "starm";

        await editor.LoadListedCommand.ExecuteAsync(null);

        Assert.Equal("1.2", editor.ReleasesSince);
        Assert.Contains("[releases]\ngithub = \"StarMapLoader/StarMap\"\nsince = \"1.2\"\n", editor.DocumentText, StringComparison.Ordinal);

        editor.ReleasesSince = "0.9";

        Assert.Contains("[releases]\ngithub = \"StarMapLoader/StarMap\"\nsince = \"0.9\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location.StartsWith("releases", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Since_NewListing_IsWrittenWithAHostAndCheckedAsAVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, _) = await ValidNewListingAsync(harness);

        editor.ReleasesSince = "1.2";

        Assert.True(editor.HasReleasesHost);
        Assert.Contains("[releases]\ngithub = \"owner/MyMod\"\nsince = \"1.2\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));

        editor.ReleasesSince = "latest";

        Assert.Contains(editor.Errors, issue => issue.Location == "releases.since");
        Assert.Contains(editor.VisibleErrors, error => error.StartsWith(harness.Localization.ListingReleasesSince + ": ", StringComparison.Ordinal));

        editor.ReleasesGitHub = string.Empty;

        Assert.False(editor.HasReleasesHost);
        Assert.DoesNotContain("since", editor.DocumentText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("flight com", "AdvancedFlightComputer")]
    [InlineData("MEASURE", "MeasureTools")]
    [InlineData("ksarm", "KSArmory")]
    public async Task ListedQuery_FindsPartOfTheNameOrTheIdInAnyCaseAndChoosesIt(string query, string id)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = query;

        Assert.Equal(id, Assert.Single(editor.ListedMatches).Id);
        Assert.Equal(id, editor.SelectedListed?.Id);
        Assert.True(editor.LoadListedCommand.CanExecute(null));
    }

    [Fact]
    public async Task ListedQuery_NoMatch_SaysSoAndLoadsNothing()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = "nothing like it";

        Assert.Empty(editor.ListedMatches);
        Assert.True(editor.HasNoListedMatch);
        Assert.Null(editor.SelectedListed);
        Assert.False(editor.LoadListedCommand.CanExecute(null));
    }

    [Fact]
    public async Task MoveListedSelection_StepsThroughTheMatchesAndStopsAtTheEnds()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.ListedQuery = "s";

        editor.MoveListedSelection(1);
        editor.MoveListedSelection(1);
        editor.MoveListedSelection(1);
        var last = editor.SelectedListed?.Id;
        editor.MoveListedSelection(-1);

        Assert.Equal(["KSArmory", "MeasureTools", "StarMap"], editor.ListedMatches.Select(listing => listing.Id));
        Assert.Equal("StarMap", last);
        Assert.Equal("MeasureTools", editor.SelectedListed?.Id);
    }

    [Fact]
    public async Task SignedIn_OwnListingsComeFirstUntilTheSignOut()
    {
        var session = new ListingPullRequestViewModelTests.FakeSession();
        session.SignIn();
        using var harness = await ViewModelHarness.CreateAsync(gitHub: session, editSnapshot: json => json
            .Replace("StarMapLoader/StarMap", "OctoCat/StarMap", StringComparison.Ordinal)
            .Replace("LaurensDeV/KSArmory", "octocat-org/KSArmory", StringComparison.Ordinal));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        var signedIn = editor.ListedMatches.ToList();
        editor.SelectedListed = signedIn[0];
        session.SignOut();

        Assert.Equal(
            [("StarMap", true), ("AdvancedFlightComputer", false), ("KSArmory", false), ("MeasureTools", false)],
            signedIn.Select(listing => (listing.Id, listing.IsOwn)));
        Assert.Equal(["AdvancedFlightComputer", "KSArmory", "MeasureTools", "StarMap"], editor.ListedMatches.Select(listing => listing.Id));
        Assert.DoesNotContain(editor.ListedMatches, listing => listing.IsOwn);
        Assert.Same(editor.ListedMatches[3], editor.SelectedListed);
    }

    [Fact]
    public async Task ListedSearch_FindsAnAuthorAndNamesTheAuthors()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.ListedQuery = "laurens";

        var match = Assert.Single(editor.ListedMatches);
        Assert.Equal("KSArmory", match.Id);
        Assert.Equal(harness.ViewModel.Localization.FormatContentByAuthor("Laurens"), match.AuthorsText);

        // the "by" around the names is display text, not something a listing is found by
        editor.ListedQuery = "by";
        Assert.Empty(editor.ListedMatches);
    }

    [Fact]
    public async Task LeavingThePage_KeepsTheDraft()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await viewModel.OpenListingAsync();
        viewModel.ListingEditor.StartEmptyCommand.Execute(null);
        viewModel.ListingEditor.Name = "Kept";

        viewModel.SetMainWindowLibrary();
        await viewModel.OpenListingAsync();

        Assert.True(viewModel.ListingEditor.IsFormStep);
        Assert.Equal("Kept", viewModel.ListingEditor.Name);
        Assert.Equal("Kept", viewModel.ListingEditor.Draft.Name);
    }

    [Fact]
    public async Task ChooseFile_MeasuresTheImageForItsRecord()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        window.ImageToOpen = new PickedBinaryFile("icon.png", Png(512, 512));
        editor.AddIconCommand.Execute(null);
        var icon = editor.Icon!;
        icon.Url = "https://example.com/icon.png";

        await icon.ChooseFileCommand.ExecuteAsync(null);

        Assert.True(icon.IsMeasured);
        Assert.Null(icon.Problem);
        Assert.Contains("[images.icon]\nurl = \"https://example.com/icon.png\"\nsha256 = \"", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("width = 512\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
    }

    [Fact]
    public async Task ChooseFile_ImageOutsideTheLimits_SaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        window.ImageToOpen = new PickedBinaryFile("small.png", Png(100, 100));
        editor.AddIconCommand.Execute(null);

        await editor.Icon!.ChooseFileCommand.ExecuteAsync(null);

        Assert.False(editor.Icon.IsMeasured);
        Assert.Contains("outside the limits", editor.Icon.Problem);
    }

    [Fact]
    public async Task ChooseFile_FailsAfterAMeasuredImage_DropsTheOldFacts()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, _, window) = await ValidNewListingAsync(harness);
        editor.AddIconCommand.Execute(null);
        var icon = editor.Icon!;
        icon.Url = "https://example.com/icon.png";
        window.ImageToOpen = new PickedBinaryFile("icon.png", Png(512, 512));
        await icon.ChooseFileCommand.ExecuteAsync(null);

        window.ImageToOpen = new PickedBinaryFile("small.png", Png(100, 100));
        await icon.ChooseFileCommand.ExecuteAsync(null);

        Assert.False(icon.IsMeasured);
        Assert.Null(icon.Width);
        Assert.Null(icon.Size);
        Assert.DoesNotContain("sha256", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartPack_OpensThePackFormWithoutReleasesLoaderOrDependencies()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        editor.StartPackCommand.Execute(null);

        Assert.True(editor.IsFormStep);
        Assert.True(editor.IsPack);
        Assert.False(editor.CanUseLoader);
        Assert.Equal("1.0.0", editor.PackVersion);
        Assert.Matches("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$", editor.ReleasedAt);
        Assert.Equal(["about", "links", "pack", "members", "compatibility", "tags", "images"], editor.Steps.Select(step => step.Key));
        Assert.Contains(harness.Localization.ListingMembers, editor.MissingText);
        Assert.Empty(editor.VisibleErrors);
    }

    [Fact]
    public async Task PackDraft_WritesADocumentTheSchemaAcceptsAtThePackPath()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        FillPack(editor);

        Assert.False(editor.HasErrors, string.Join("\n", editor.Errors));
        Assert.True(editor.CanOpenPullRequest);
        Assert.Equal("packs/my-pack/1.0.0.toml", editor.Draft.Path);
        Assert.Contains("type = \"modpack\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("version = \"1.0.0\"\nreleased_at = \"", editor.DocumentText, StringComparison.Ordinal);
        Assert.EndsWith("[[mods]]\nid = \"AdvancedFlightComputer\"\nversion = \"0.7.5\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.DoesNotContain("[loader]", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberPicker_OffersNoYankedReleaseNoDelistedListingAndNoLoader()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: YankAndDelist);
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartPackCommand.Execute(null);
        var offered = editor.MemberMatches.Select(listing => listing.Id).ToList();

        editor.MemberQuery = "measure";
        editor.AddMemberCommand.Execute(null);
        editor.MemberQuery = string.Empty;

        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], offered);
        var row = Assert.Single(editor.Members);
        Assert.Equal(["1.1.9", "1.1.8", "1.1.7"], row.Releases.Select(release => release.Version));
        Assert.Equal(new ListingReleaseChoice("1.1.9", harness.Localization.ReleaseStable), row.Selected);
        Assert.Null(row.Note);
        Assert.Equal(["AdvancedFlightComputer"], editor.MemberMatches.Select(listing => listing.Id));
    }

    [Fact]
    public async Task LoadedPinThatTheSnapshotDoesNotOffer_StaysInTheFileWithANote()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: YankAndDelist);
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();

        editor.Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Id = "my-pack",
            Version = "1.0.0",
            Mods = [new ListingPackMember("Unlisted", "1.0.0"), new ListingPackMember("MeasureTools", "1.1.10"), new ListingPackMember("AdvancedFlightComputer", "0.7.5")],
        });

        Assert.Contains("[[mods]]\nid = \"Unlisted\"\nversion = \"1.0.0\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("[[mods]]\nid = \"MeasureTools\"\nversion = \"1.1.10\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal(
            [localization.FormatListingMemberNotListed("Unlisted"), localization.FormatListingMemberNotOffered("MeasureTools", "1.1.10"), null],
            editor.Members.Select(row => row.Note));
        Assert.Equal(new ListingReleaseChoice("1.1.10", string.Empty), editor.Members[1].Selected);
        Assert.Contains($"{localization.ListingMembers}: {localization.FormatListingMemberNotListed("Unlisted")}", editor.VisibleNotes);
        Assert.Contains($"{localization.ListingMembers}: {localization.FormatListingMemberNotOffered("MeasureTools", "1.1.10")}", editor.VisibleNotes);
    }

    [Fact]
    public async Task GoneRelease_IsNotOfferedAndALoadedPinOfItKeepsItsGameMin()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: MarkGone);
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        var date = MainViewModel.DateText(DateTimeOffset.Parse("2026-09-23T10:24:00Z", CultureInfo.InvariantCulture));
        await harness.ViewModel.OpenListingAsync();
        editor.StartPackCommand.Execute(null);

        editor.MemberQuery = "measure";
        editor.AddMemberCommand.Execute(null);

        Assert.Equal(["1.1.9", "1.1.8", "1.1.7"], Assert.Single(editor.Members).Releases.Select(release => release.Version));

        editor.Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Id = "my-pack",
            Version = "1.0.0",
            GameMin = "2026.8.19.5261",
            Mods = [new ListingPackMember("MeasureTools", "1.1.10"), new ListingPackMember("AdvancedFlightComputer", "0.7.5"), new ListingPackMember("KSArmory", "0.8.44")],
        });

        Assert.Equal(
            [localization.FormatListingMemberGone("MeasureTools", "1.1.10", date), localization.FormatListingMemberGone("AdvancedFlightComputer", "0.7.5", date), null],
            editor.Members.Select(row => row.Note));
        Assert.Equal("2026.9.4.5400", editor.GameMinProposal);
    }

    [Fact]
    public async Task PackGameMin_ProposesTheHighestGameMinOfThePinnedReleases()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Id = "my-pack",
            GameMin = "2026.8.19.5261",
            Mods = [new ListingPackMember("KSArmory", "0.8.44"), new ListingPackMember("AdvancedFlightComputer", "0.7.5")],
        });
        var proposal = harness.Localization.FormatListingGameMinProposal("2026.9.4.5400");

        Assert.Equal("2026.9.4.5400", editor.GameMinProposal);
        Assert.Contains(editor.Notes, note => note.Location == "compatibility" && note.Message == proposal);

        editor.UseGameMinProposalCommand.Execute(null);

        Assert.Equal("2026.9.4.5400", editor.GameMin);
        Assert.Null(editor.GameMinProposal);
        Assert.DoesNotContain(editor.Notes, note => note.Message == proposal);
    }

    [Fact]
    public async Task PackGameMinProposal_FollowsALanguageChange()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Id = "my-pack",
            GameMin = "2026.8.19.5261",
            Mods = [new ListingPackMember("AdvancedFlightComputer", "0.7.5")],
        });
        var english = editor.GameMinProposalText;
        var changed = new List<string?>();
        editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        harness.Localization.TrySetCulture("de");

        Assert.Contains(nameof(ListingEditor.GameMinProposalText), changed);
        Assert.Equal(harness.Localization.FormatListingGameMinProposal("2026.9.4.5400"), editor.GameMinProposalText);
        Assert.NotEqual(english, editor.GameMinProposalText);
    }

    [Fact]
    public async Task PackChangelog_WithWindowsLineEnds_IsWrittenWithLineFeedsOnly()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        FillPack(editor);

        editor.Changelog = "Fixed A.\r\nAdded B.\r\n";

        Assert.Equal("Fixed A.\nAdded B.", editor.Draft.Changelog);
        Assert.DoesNotContain("\\r", editor.DocumentText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenPullRequest_Pack_OpensTheNewFilePageOfThePackPath()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidPackAsync(harness);

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.StartsWith("https://github.com/KSAModding/content-index/new/main?filename=packs/my-pack/1.0.0.toml&value=spec_version%20%3D%201%0A", Assert.Single(opened), StringComparison.Ordinal);
        Assert.True(editor.LastPullRequestPage!.CarriesText);
        Assert.Null(window.CopiedText);
    }

    [Fact]
    public async Task OpenPullRequest_PackLinkOverTwoThousandCharacters_IsCopiedInstead()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var (editor, opened, window) = await ValidPackAsync(harness);
        editor.Description = new string('a', 1500);

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("https://github.com/KSAModding/content-index/new/main?filename=packs/my-pack/1.0.0.toml", Assert.Single(opened));
        Assert.Equal(editor.DocumentText, window.CopiedText);
        Assert.Equal(harness.Localization.ListingOpenedPaste, editor.OutputMessage);
    }

    /// <summary>A new pack that the checks accept, with AdvancedFlightComputer 0.7.5 as its one member.</summary>
    internal static void FillPack(ListingEditor editor)
    {
        editor.StartPackCommand.Execute(null);
        editor.Id = "my-pack";
        editor.Name = "My Pack";
        editor.Authors = "Maxi";
        editor.Abstract = "Pins a few mods.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-pack.77/";
        editor.GameMin = "2026.9.4.5400";
        editor.MemberQuery = "flight";
        editor.AddMemberCommand.Execute(null);
        editor.MemberQuery = string.Empty;
    }

    private static async Task<(ListingEditor Editor, List<string> Opened, FakeWindowServices Window)> ValidPackAsync(ViewModelHarness harness)
    {
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        FillPack(editor);
        Assert.True(editor.CanOpenPullRequest, string.Join("\n", editor.Errors));
        return (editor, opened, window);
    }

    /// <summary>Marks the downloads of MeasureTools 1.1.10 and AdvancedFlightComputer 0.7.5 as gone (RFC 0078).</summary>
    private static string MarkGone(string json)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var listing in root["listings"]!.AsArray())
        {
            var gone = (string?)listing!["id"] switch { "MeasureTools" => "1.1.10", "AdvancedFlightComputer" => "0.7.5", _ => null };
            foreach (var release in listing["releases"]!.AsArray())
            {
                if (gone is not null && (string?)release!["version"] == gone)
                    release["download"]!["unavailable_since"] = "2026-09-23T10:24:00Z";
            }
        }

        return root.ToJsonString();
    }

    /// <summary>Yanks MeasureTools 1.1.10 and delists KSArmory.</summary>
    private static string YankAndDelist(string json)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var listing in root["listings"]!.AsArray())
        {
            if ((string?)listing!["id"] == "KSArmory")
                listing["index_status"] = new JsonObject { ["state"] = "delisted" };
            if ((string?)listing["id"] == "MeasureTools")
                listing["releases"]![0]!["yanked"] = true;
        }

        return root.ToJsonString();
    }

    [Fact]
    public async Task LoadListedPack_ThePackOfContentIndex117_ProposesTheNextVersionWithThePinsOfTheNewestOne()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        editor.ListedQuery = "flight planning";

        Assert.Equal(new ListedListing(Pack117Id, "Flight Planning Essentials", harness.Localization.FormatContentByAuthor("Maxi"), IsOwn: false, IsPack: true), editor.SelectedListed);
        await editor.LoadListedCommand.ExecuteAsync(null);

        Assert.True(editor.IsNextVersion);
        Assert.False(editor.IsEdit);
        Assert.True(editor.HasFixedId);
        Assert.Equal("1.0.2", editor.PackVersion);
        Assert.Equal($"packs/{Pack117Id}/1.0.2.toml", editor.Draft.Path);
        Assert.Equal([("DeltaVMap", "1.2.6"), ("AdvancedFlightComputer", "0.8.0"), ("Compendium", "0.9.13")], editor.Draft.Mods.Select(pin => (pin.Id, pin.Version)));
        Assert.NotEqual("2026-09-23T20:00:00Z", editor.ReleasedAt);
        Assert.True(DateTimeOffset.Parse(editor.ReleasedAt, System.Globalization.CultureInfo.InvariantCulture) >= before);
        Assert.Contains("[images.icon]\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Contains("repository = \"https://github.com/renancamm/ksa-beiks-flight-planning-essentials-pack/\"\n", editor.DocumentText, StringComparison.Ordinal);
        Assert.Equal(harness.Localization.FormatListingNextVersion(Pack117Id, "1.0.1"), editor.NextVersionText);
        Assert.Null(editor.OutputMessage);
    }

    [Fact]
    public async Task LoadListedPack_OwnId_GivesNoIdError()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        await editor.MakeNextVersionAsync(Pack117Id);

        Assert.True(editor.IsNextVersion);
        Assert.DoesNotContain(editor.Errors, issue => issue.Location == "id");
        Assert.DoesNotContain(editor.Notes, issue => issue.Location == "links.forums");
    }

    [Fact]
    public async Task LoadListedPack_HighestVersionRetracted_ProposesAVersionAboveItAndShowsTheReason()
    {
        const string reason = "Removed at the request of the author of KSArmory.";
        var pins = new[] { PackViewModelTests.Pin("MeasureTools", "1.1.9"), PackViewModelTests.Pin("KSArmory", "0.8.44") };
        var main = new Dictionary<string, string> { ["armory-pack/1.0.1.toml"] = PackToml("armory-pack", "1.0.1", ("MeasureTools", "1.1.9"), ("KSArmory", "0.8.44")) };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.0", pins), Retracted(PackViewModelTests.Version("1.0.1", pins), reason))));
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();

        await editor.MakeNextVersionAsync("armory-pack");

        Assert.Equal("1.0.2", editor.PackVersion);
        Assert.Null(editor.OutputMessage);
        Assert.Contains(editor.Notes, issue => issue.Location == "version" && issue.Message == localization.FormatListingPackRetracted("1.0.1", reason));
        var named = localization.FormatListingMemberNamedInRetraction("1.0.1", "KSArmory", reason);
        Assert.Contains(editor.Notes, issue => issue.Location == "mods[1]" && issue.Message == named);
        Assert.Equal([null, named], editor.Members.Select(row => row.RetractionNote));
        Assert.Contains($"{localization.ListingMembers}: {named}", editor.VisibleNotes);
    }

    [Fact]
    public async Task LoadListedPack_ProposedPathThatMainHas_IsRaisedAgainAndStartsFromThatFile()
    {
        var newer = Pack117.Replace("version = \"1.0.1\"", "version = \"1.0.2\"", StringComparison.Ordinal).Replace("\"0.9.13\"", "\"0.9.14\"", StringComparison.Ordinal);
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117, [$"{Pack117Id}/1.0.2.toml"] = newer };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();

        await editor.MakeNextVersionAsync(Pack117Id);

        Assert.Equal("1.0.3", editor.PackVersion);
        Assert.Equal(harness.Localization.FormatListingPackVersionTaken("1.0.2", "1.0.3"), editor.OutputMessage);
        Assert.Equal([("DeltaVMap", "1.2.6"), ("AdvancedFlightComputer", "0.8.0"), ("Compendium", "0.9.14")], editor.Draft.Mods.Select(pin => (pin.Id, pin.Version)));
        Assert.Equal(harness.Localization.FormatListingNextVersion(Pack117Id, "1.0.2"), editor.NextVersionText);
        Assert.Contains(harness.Requests, uri => uri.AbsoluteUri == $"{RawPacks}{Pack117Id}/1.0.2.toml");
        Assert.DoesNotContain(harness.Requests, uri => uri.AbsolutePath.Contains("/contents/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OpenPullRequest_NextVersionThatMainGainedAfterTheLoad_IsRaisedAgain()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        harness.ViewModel.WindowServices = new FakeWindowServices();
        await harness.ViewModel.OpenListingAsync();
        await editor.MakeNextVersionAsync(Pack117Id);
        Assert.True(editor.CanOpenPullRequest, string.Join("\n", editor.Errors));
        main[$"{Pack117Id}/1.0.2.toml"] = Pack117;

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("1.0.3", editor.PackVersion);
        Assert.StartsWith($"https://github.com/KSAModding/content-index/new/main?filename=packs/{Pack117Id}/1.0.3.toml", Assert.Single(opened), StringComparison.Ordinal);
        Assert.StartsWith(harness.Localization.FormatListingPackVersionTaken("1.0.2", "1.0.3"), editor.OutputMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextVersion_VersionWithBuildMetadata_IsReadAndCheckedAtItsExactPath()
    {
        var main = new Dictionary<string, string>
        {
            ["armory-pack/1.0.1%2Bb.2.toml"] = PackToml("armory-pack", "1.0.1+b.2", ("KSArmory", "0.8.44")),
            ["armory-pack/1.0.2%2Bb.1.toml"] = PackToml("armory-pack", "1.0.2+b.1", ("KSArmory", "0.8.44")),
        };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(
            PackViewModelTests.Pack("armory-pack", "Armory Pack", PackViewModelTests.Version("1.0.1+b.2", PackViewModelTests.Pin("KSArmory", "0.8.44")))));
        var editor = harness.ViewModel.ListingEditor;
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        harness.ViewModel.WindowServices = new FakeWindowServices();
        await harness.ViewModel.OpenListingAsync();

        await editor.MakeNextVersionAsync("armory-pack");

        Assert.Equal("1.0.2", editor.PackVersion);
        Assert.Equal(harness.Localization.FormatListingNextVersion("armory-pack", "1.0.1+b.2"), editor.NextVersionText);

        editor.PackVersion = "1.0.2+b.1";
        editor.Forums = "https://forums.ahwoo.com/threads/armory-pack.42/";
        Assert.True(editor.CanOpenPullRequest, string.Join("\n", editor.Errors));
        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Equal("1.0.3", editor.PackVersion);
        Assert.StartsWith("https://github.com/KSAModding/content-index/new/main?filename=packs/armory-pack/1.0.3.toml", Assert.Single(opened), StringComparison.Ordinal);
        Assert.StartsWith(harness.Localization.FormatListingPackVersionTaken("1.0.2+b.1", "1.0.3"), editor.OutputMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenPullRequest_NextVersionThatMainCannotAnswerFor_OpensNothing()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        var down = false;
        var serve = MainBranch(main);
        using var harness = await ViewModelHarness.CreateAsync(
            respond: request => down && request.RequestUri!.AbsoluteUri.StartsWith(RawPacks, StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : serve(request),
            editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        await harness.ViewModel.OpenListingAsync();
        await editor.MakeNextVersionAsync(Pack117Id);
        down = true;

        await editor.OpenPullRequestCommand.ExecuteAsync(null);

        Assert.Empty(opened);
        Assert.StartsWith(harness.Localization.FormatListingPackCheckFailed("1.0.2", string.Empty), editor.OutputMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NextVersion_VersionOrReleaseTimeNotAfterTheListedOnes_IsAnError()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var editor = harness.ViewModel.ListingEditor;
        var localization = harness.Localization;
        await harness.ViewModel.OpenListingAsync();
        await editor.MakeNextVersionAsync(Pack117Id);

        editor.PackVersion = "1.0.1";
        editor.ReleasedAt = "2026-09-01T12:00:00Z";

        Assert.Contains(editor.Errors, issue => issue.Location == "version" && issue.Message == localization.FormatListingPackVersionNotHigher("1.0.1", "1.0.1"));
        Assert.Contains(editor.Errors, issue => issue.Location == "released_at" && issue.Message == localization.FormatListingPackReleasedAtNotLater("2026-09-01T12:00:00Z", "2026-09-01T12:00:00Z", "1.0.1"));
        Assert.False(editor.CanOpenPullRequest);
    }

    [Theory]
    [InlineData(ReleaseChannel.Stable)]
    [InlineData(ReleaseChannel.Testing)]
    [InlineData(ReleaseChannel.Dev)]
    public async Task PackMembers_NewerStableReleaseIsMarked_ANewerTestingReleaseIsNot_WhateverTheChannel(ReleaseChannel channel)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: snapshot => Testing(snapshot, "MeasureTools", "1.1.10"));
        var editor = harness.ViewModel.ListingEditor;
        harness.ViewModel.SelectedReleaseChannel = harness.ViewModel.OptionFor(channel);
        await harness.ViewModel.OpenListingAsync();

        editor.Load(new ListingDraft
        {
            Type = ListingDraft.ModPackType,
            Id = "my-pack",
            Version = "1.0.0",
            Mods = [new ListingPackMember("AdvancedFlightComputer", "0.7.4"), new ListingPackMember("MeasureTools", "1.1.9")],
        });
        var (flight, measure) = (editor.Members[0], editor.Members[1]);

        Assert.Equal("0.7.5", flight.Newer?.Version);
        Assert.Equal(harness.Localization.FormatPackMemberNewer("0.7.5"), flight.NewerText);
        Assert.False(measure.HasNewer);

        flight.UseNewerCommand.Execute(null);

        Assert.Equal("0.7.5", flight.Selected?.Version);
        Assert.False(flight.HasNewer);
        Assert.Equal("0.7.5", editor.Draft.Mods[0].Version);
    }

    [Fact]
    public async Task CopyForumList_CopiesTheLinesOfTheMembersOfTheDraft()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var editor = harness.ViewModel.ListingEditor;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        await harness.ViewModel.OpenListingAsync();
        FillPack(editor);

        await editor.CopyForumListCommand.ExecuteAsync(null);

        var lines = await ModPackForumList.WriteAsync([new ModPackEntry("AdvancedFlightComputer", ModVersion.Parse("0.7.5"))], harness.ViewModel.Services!.ContentIndex);
        Assert.Equal(Assert.Single(lines), window.CopiedText);
        Assert.StartsWith($"{editor.Members[0].Name} 0.7.5 - Author: ", window.CopiedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MakeNextPackVersion_OnThePackPage_OpensTheListingPageWithThatPack()
    {
        var main = new Dictionary<string, string> { [$"{Pack117Id}/1.0.1.toml"] = Pack117 };
        using var harness = await ViewModelHarness.CreateAsync(respond: MainBranch(main), editSnapshot: PackViewModelTests.WithPacks(Pack117Entry()));
        var viewModel = harness.ViewModel;
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.ShowDiscoverModpacksCommand.Execute(null);
        await viewModel.OpenPackAsync(Assert.Single(viewModel.DiscoverPacks));

        await viewModel.MakeNextPackVersionCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowListing);
        Assert.False(viewModel.CurrentWindowPack);
        Assert.True(viewModel.ListingEditor.IsFormStep);
        Assert.True(viewModel.ListingEditor.IsNextVersion);
        Assert.Equal("1.0.2", viewModel.ListingEditor.PackVersion);
    }

    private const string Pack117Id = "beiks-flight-planning-essentials-pack";

    private const string RawPacks = "https://raw.githubusercontent.com/KSAModding/content-index/main/packs/";

    /// <summary>Version 1.0.1 of the pack of content-index #117, with a shorter description.</summary>
    private const string Pack117 = """"
        spec_version = 1
        id = "beiks-flight-planning-essentials-pack"
        type = "modpack"
        name = "Flight Planning Essentials"
        authors = ["Beik"]
        version = "1.0.1"
        released_at = "2026-09-23T20:00:00Z"
        abstract = "This is a small pack of three mods for mission planning: one for looking up bodies in the solar system, one for delta-v budgets and transfer windows, and one for executing maneuvers."
        description = """
        Included mods:

        Compendium, DeltaVMap and Advanced Flight Computer.
        """

        license = "MIT"
        tags = ["user-interface"]

        [links]
        forums = "https://forums.ahwoo.com/forums/kitten-space-agency/mod-releases/flight-planning-essentials.1281/"
        repository = "https://github.com/renancamm/ksa-beiks-flight-planning-essentials-pack/"

        [compatibility]
        game_min = "2026.9.10.5438"

        [images.icon]
        url = "https://raw.githubusercontent.com/renancamm/ksa-beiks-skycharts-modpack/refs/heads/main/listing/pack-icon.png"
        sha256 = "66114162c33d8ad7c43ab5521bb62b174e865e199385051410980eff7d82f802"
        width = 500
        height = 500
        size = 237977

        [[mods]]
        id = "DeltaVMap"
        version = "1.2.6"

        [[mods]]
        id = "AdvancedFlightComputer"
        version = "0.8.0"

        [[mods]]
        id = "Compendium"
        version = "0.9.13"
        """";

    /// <summary>The snapshot entry of that pack, with its versions 1.0.0 and 1.0.1.</summary>
    private static string Pack117Entry()
    {
        var pins = new[] { PackViewModelTests.Pin("DeltaVMap", "1.2.6"), PackViewModelTests.Pin("AdvancedFlightComputer", "0.8.0"), PackViewModelTests.Pin("Compendium", "0.9.13") };
        return PackViewModelTests.Pack(Pack117Id, "Flight Planning Essentials", PackViewModelTests.Version("1.0.0", pins), PackViewModelTests.Version("1.0.1", pins));
    }

    /// <summary>A pack version file as content-index holds it.</summary>
    private static string PackToml(string id, string version, params (string Id, string Version)[] pins) => $$"""
        spec_version = 1
        id = "{{id}}"
        type = "modpack"
        name = "Armory Pack"
        authors = ["Maxi"]
        version = "{{version}}"
        released_at = "2026-09-01T12:00:00Z"
        abstract = "Armory Pack abstract."
        license = "MIT"

        [links]
        forums = "https://forums.example.com/{{id}}"

        [compatibility]
        game_min = "2026.8.19.5261"
        {{string.Concat(pins.Select(pin => $"\n[[mods]]\nid = \"{pin.Id}\"\nversion = \"{pin.Version}\"\n"))}}
        """;

    /// <summary>Serves the pack files of main from <paramref name="files"/>, and not found for every other pack path.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage?> MainBranch(Dictionary<string, string> files) => request =>
    {
        var url = request.RequestUri!.AbsoluteUri;
        if (!url.StartsWith(RawPacks, StringComparison.Ordinal))
            return null;

        return files.TryGetValue(url[RawPacks.Length..], out var text)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    };

    private static Func<string, string, string> Retracted(Func<string, string, string> version, string reason) => (id, name) =>
    {
        var entry = JsonNode.Parse(version(id, name))!;
        entry["index_status"] = new JsonObject { ["state"] = "retracted", ["since"] = "2026-09-24T10:00:00Z", ["reason"] = reason };
        return entry.ToJsonString();
    };

    /// <summary>Marks one release of the snapshot as testing.</summary>
    private static string Testing(string json, string id, string version)
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == id)!;
        listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!["release_status"] = "testing";
        return root.ToJsonString();
    }

    private static async Task<(ListingEditor Editor, List<string> Opened, FakeWindowServices Window)> ValidNewListingAsync(ViewModelHarness harness)
    {
        var opened = new List<string>();
        harness.ViewModel.OpenWithSystem = opened.Add;
        var window = new FakeWindowServices();
        harness.ViewModel.WindowServices = window;
        var editor = harness.ViewModel.ListingEditor;
        await harness.ViewModel.OpenListingAsync();
        editor.StartEmptyCommand.Execute(null);
        editor.Id = "MyMod";
        editor.Name = "My Mod";
        editor.Authors = "Maxi";
        editor.Abstract = "Does a thing.";
        editor.License = "MIT";
        editor.Forums = "https://forums.ahwoo.com/threads/my-mod.42/";
        editor.ReleasesGitHub = "owner/MyMod";
        return (editor, opened, window);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage?> Serve() => request => request.RequestUri!.AbsoluteUri switch
    {
        "https://api.github.com/repos/owner/KSA-MyMod" => Json(Repository),
        "https://api.github.com/repos/owner/KSA-MyMod/releases?per_page=100" => Json(Releases),
        "https://github.com/owner/KSA-MyMod/releases/download/v1.0.0/MyMod.zip" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Archive()) },
        "https://forums.ahwoo.com/threads/my-mod.42/" => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<h1 class=\"p-title-value\"><span class=\"label label--orange\" dir=\"auto\">Gameplay</span>My Mod</h1>"),
        },
        _ => null,
    };

    private static string WithGameplayPrefix(string json) =>
        """{ "tags": { "spec_version": 1, "mod": [{ "tag": "gameplay", "name": "Gameplay", "meaning": "Mechanics.", "forum_prefix": "Gameplay" }, { "tag": "library", "name": "Library", "meaning": "Code." }] }, """
        + json.TrimStart()[1..];

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static byte[] Archive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in new[] { ("MyMod/mod.toml", "name = \"MyMod\"\n"), ("MyMod/MyMod.dll", "binary") })
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] Png(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        foreach (var (kind, body) in new[] { ("IHDR", header), ("IDAT", new byte[] { 0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 }), ("IEND", Array.Empty<byte>()) })
        {
            var length = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
            bytes.AddRange(length);
            bytes.AddRange(Encoding.ASCII.GetBytes(kind));
            bytes.AddRange(body);
            bytes.AddRange(new byte[4]);
        }

        return [.. bytes];
    }

    private sealed class FakeWindowServices : IWindowServices
    {
        public string? CopiedText { get; private set; }

        public PickedBinaryFile? ImageToOpen { get; set; }

        public Task<string?> SaveTextFileAsync(string title, string suggestedFileName, string fileTypeName, string text) => Task.FromResult<string?>(suggestedFileName);

        public Task<PickedTextFile?> OpenTextFileAsync(string title, string fileTypeName) => Task.FromResult<PickedTextFile?>(null);

        public Task<PickedBinaryFile?> OpenImageFileAsync(string title, string fileTypeName, long maxBytes) => Task.FromResult(ImageToOpen);

        public Task CopyTextAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }
}
