using System.Text.Json.Nodes;
using Borea.App.ViewModels;
using Borea.Composition;
using Borea.Core.Instances;
using Borea.Core.Listings;
using Borea.Core.ModLoaders;
using Borea.Core.Mods;

namespace Borea.App.Tests.ViewModels;

public sealed class ListingPackFromInstanceTests
{
    [Fact]
    public async Task MakePack_TwoIndexModsStarMapAndAManualMod_PinsTheIndexModsAndNamesStarMapAndTheManualMod()
    {
        using var harness = await ViewModelHarness.CreateAsync(SetUpStarMap);
        var instance = await InstalledContent.AddAsync(harness, "KSArmory", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: false, ownership: ModInstallOwnership.Borea, version: "0.7.5", into: instance);
        await AddManualModAsync(harness, instance, "LocalOnly");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        var localization = harness.Localization;

        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);

        var editor = viewModel.ListingEditor;
        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(editor.IsFormStep);
        Assert.True(editor.IsPack);
        Assert.True(editor.IsNew);
        Assert.Equal("Main", editor.Name);
        Assert.Equal("1.0.0", editor.PackVersion);
        Assert.Equal([new ListingPackMember("KSArmory", "0.8.44"), new ListingPackMember("AdvancedFlightComputer", "0.7.5")], editor.Draft.Mods);
        Assert.Equal("2026.9.4.5400", editor.GameMin);
        Assert.Null(editor.GameMinProposal);
        Assert.Equal(localization.FormatListingLeftOut("Main"), editor.LeftOutTitle);
        Assert.True(editor.HasLeftOut);
        Assert.Equal([localization.FormatListingLeftOutModLoader("StarMap", "0.4.6"), localization.FormatListingLeftOutNotInstalledByBorea("LocalOnly")], editor.LeftOut);
    }

    [Fact]
    public async Task MakePack_ManualFolderDeletedAfterAScan_IsNotNamed_AndALoaderWithoutAnInstallationIsNamedWithoutAVersion()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await AddManualModAsync(harness, instance, "Gone");
        await harness.Services.ForeignModAdopter.ScanAsync(instance.InstanceId);
        Directory.Delete(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), "Gone"), recursive: true);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);

        Assert.Equal(["AdvancedFlightComputer"], viewModel.ListingEditor.Draft.Mods.Select(member => member.Id));
        Assert.Equal([harness.Localization.FormatListingLeftOutModLoaderUnknownVersion("StarMap")], viewModel.ListingEditor.LeftOut);
    }

    [Fact]
    public async Task MakePack_DisabledIndexMod_IsLeftOutAndNamed_UntilTheAuthorAddsIt()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false, ownership: ModInstallOwnership.Borea, version: "1.1.9", into: instance);
        await harness.Services.ModState.SetInactiveAsync(instance.InstanceId, "MeasureTools");
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);

        var editor = viewModel.ListingEditor;
        Assert.Equal(["AdvancedFlightComputer"], editor.Draft.Mods.Select(member => member.Id));
        var starMap = harness.Localization.FormatListingLeftOutModLoaderUnknownVersion("StarMap");
        Assert.Equal([harness.Localization.FormatListingLeftOutDisabled("MeasureTools", "1.1.9"), starMap], editor.LeftOut);

        editor.MemberQuery = "measure";
        editor.AddMemberCommand.Execute(null);

        Assert.Equal(["AdvancedFlightComputer", "MeasureTools"], editor.Draft.Mods.Select(member => member.Id));
        Assert.Equal([starMap], editor.LeftOut);
    }

    [Fact]
    public async Task MakePack_InstalledReleaseYankedInTheSnapshot_IsLeftOutAndNamed_InTheDisplayLanguage()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => Yank(json, "MeasureTools", "1.1.10"));
        var instance = await InstalledContent.AddAsync(harness, "AdvancedFlightComputer", activate: true, ownership: ModInstallOwnership.Borea);
        await InstalledContent.AddAsync(harness, "MeasureTools", activate: false, ownership: ModInstallOwnership.Borea, version: "1.1.10", into: instance);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);
        var editor = viewModel.ListingEditor;
        var english = editor.LeftOut[0];

        harness.Localization.TrySetCulture("de");

        Assert.Equal(["AdvancedFlightComputer"], editor.Draft.Mods.Select(member => member.Id));
        Assert.Equal(
            [harness.Localization.FormatListingLeftOutYanked("MeasureTools", "1.1.10"), harness.Localization.FormatListingLeftOutModLoaderUnknownVersion("StarMap")],
            editor.LeftOut);
        Assert.NotEqual(english, editor.LeftOut[0]);
        Assert.Equal(harness.Localization.FormatListingLeftOut("Main"), editor.LeftOutTitle);
    }

    [Fact]
    public async Task MakePack_NoContentIndex_OpensNoPackAndSaysWhy()
    {
        using var harness = await ViewModelHarness.CreateAsync(indexOffline: true);
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);

        Assert.True(viewModel.CurrentWindowListing);
        Assert.True(viewModel.ListingEditor.IsStartStep);
        Assert.Equal(harness.Localization.FormatToastMakePackFailed("Main"), viewModel.Toasts.Items[^1].Message);
        Assert.Equal(harness.Localization.ToastMakePackNoIndex, viewModel.Toasts.Items[^1].Detail);
    }

    [Fact]
    public async Task StartPack_AfterAPackFromAnInstance_NamesNoLeftOutMod()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var instance = await InstalledContent.AddAsync(harness, "StarMap", activate: true, ownership: ModInstallOwnership.Borea);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().MakePackCommand.ExecuteAsync(null);
        var editor = viewModel.ListingEditor;
        Assert.True(editor.HasLeftOut);

        editor.StartOverCommand.Execute(null);
        editor.StartPackCommand.Execute(null);

        Assert.Empty(editor.LeftOut);
        Assert.Null(editor.LeftOutTitle);
    }

    /// <summary>Copies a mod folder into the instance by hand, the way an author does it, so no scan has seen it yet.</summary>
    private static async Task AddManualModAsync(ViewModelHarness harness, Instance instance, string folderName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceModsFolder(instance.InstanceId), folderName));
        await File.WriteAllTextAsync(Path.Combine(folder.FullName, "mod.toml"), $"name = \"{folderName}\"");
    }

    /// <summary>StarMap installs through the Game settings and not into an instance, so Borea records it in the settings.</summary>
    private static Task SetUpStarMap(BoreaServices services)
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(services.Paths.GetBoreaSettingsPath())!, "Loaders", "StarMap")).FullName;
        return services.SettingsRepository.SaveAsync(services.Settings.WithLoaderInstallation(
            "StarMap",
            new LoaderInstallation(folder, ModVersion.Parse("0.4.6"), rawVersion: null, isAdopted: false)));
    }

    private static string Yank(string json, string id, string version)
    {
        var root = JsonNode.Parse(json)!;
        var listing = root["listings"]!.AsArray().Single(node => (string?)node!["id"] == id)!;
        listing["releases"]!.AsArray().Single(node => (string?)node!["version"] == version)!["yanked"] = true;
        return root.ToJsonString();
    }
}
