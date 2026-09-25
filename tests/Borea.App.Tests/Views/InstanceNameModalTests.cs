using Avalonia.Controls;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class InstanceNameModalTests
{
    [Fact]
    public async Task Explanation_ShowsWhenTheModalCreatesAndNotWhenItRenames()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        await viewModel.LoadAsync();
        var explanation = harness.Localization.ModalInstanceExplanation;

        var shown = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new InstanceNameModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = modal };
            window.Show();

            viewModel.BeginCreateInstanceCommand.Execute(null);
            window.UpdateLayout();
            var creating = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            viewModel.BeginImportSharedProfileCommand.Execute(null);
            window.UpdateLayout();
            var importing = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            viewModel.Instances.Single().BeginRenameCommand.Execute(null);
            window.UpdateLayout();
            var renaming = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            window.Close();
            return Task.FromResult((creating, importing, renaming));
        });

        Assert.Contains(explanation, shown.creating);
        Assert.Contains(explanation, shown.importing);
        Assert.Contains(harness.Localization.ModalRenameInstanceTitle, shown.renaming);
        Assert.DoesNotContain(explanation, shown.renaming);
    }

    [Fact]
    public async Task RenamingASave_ShowsTheNameRulesAndNotTheInstanceExplanation()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var orbit = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit")).FullName;
        File.WriteAllText(Path.Combine(orbit, "meta.toml"), "name = \"Orbit\"\n");
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);

        var shown = await HeadlessApp.RunAsync(harness, () =>
        {
            var modal = new InstanceNameModal { DataContext = viewModel };
            var window = new Window { Width = 1280, Height = 832, Content = modal };
            window.Show();

            viewModel.SavesSection.Items.Single().BeginRenameCommand.Execute(null);
            window.UpdateLayout();
            var renaming = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            viewModel.Instances.Single().BeginRenameCommand.Execute(null);
            window.UpdateLayout();
            var renamingInstance = Texts(modal);
            viewModel.CancelNameModalCommand.Execute(null);

            window.Close();
            return Task.FromResult((renaming, renamingInstance));
        });

        Assert.Contains(harness.Localization.GameSaveRenameSaveTitle, shown.renaming);
        Assert.Contains(harness.Localization.GameSaveNameRules, shown.renaming);
        Assert.DoesNotContain(harness.Localization.ModalInstanceExplanation, shown.renaming);
        Assert.DoesNotContain(harness.Localization.GameSaveNameRules, shown.renamingInstance);
    }

    [Fact]
    public async Task SaveRowMenu_OffersRename()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var instance = (await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value)).Instance;
        var orbit = Directory.CreateDirectory(Path.Combine(harness.Services.Paths.GetInstanceSavesFolder(instance.InstanceId), "Orbit")).FullName;
        File.WriteAllText(Path.Combine(orbit, "meta.toml"), "name = \"Orbit\"\n");
        await viewModel.LoadAsync();
        await viewModel.Instances.Single().OpenCommand.ExecuteAsync(null);
        var row = viewModel.SavesSection.Items.Single();

        var menu = await HeadlessApp.RunAsync(harness, () =>
        {
            var page = new InstancePage();
            var window = new Window { Width = 1280, Height = 900, DataContext = viewModel, Content = page };
            window.Show();
            window.UpdateLayout();
            var button = page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.DataContext == row && button.Flyout is MenuFlyout);
            button.Flyout!.ShowAt(button);
            window.UpdateLayout();
            var items = ((MenuFlyout)button.Flyout).Items.OfType<MenuItem>().Select(item => (item.Header as string, item.Command)).ToList();
            button.Flyout.Hide();
            window.Close();
            return Task.FromResult(items);
        });

        Assert.Contains((harness.Localization.LibraryRename, row.BeginRenameCommand), menu);
    }

    private static List<string?> Texts(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToList();
}
