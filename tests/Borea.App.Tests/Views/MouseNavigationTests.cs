using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using CommunityToolkit.Mvvm.Input;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class MouseNavigationTests
{
    [Fact]
    public async Task BackButton_OnAModFromDiscover_GoesBackToDiscoverWithItsFilters()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;

        await OnMainWindowAsync(harness, window => PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand));

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.False(viewModel.CurrentWindowContent);
        Assert.Equal("Flight", viewModel.SearchText);
        Assert.Contains(viewModel.DiscoverItems, item => item.ModId == "AdvancedFlightComputer");
    }

    [Theory]
    [InlineData(2.0, true)]
    [InlineData(0.5, false)]
    public async Task BackButton_OnAModFromAScrolledList_KeepsThePositionAndShowsBackToTopPastOneViewport(double viewports, bool shown)
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ViewModelHarness.WithCopies(json, "KSArmory", 60));
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();
        await viewModel.EnsureDiscoverLoadedAsync();

        var (before, after, visible) = await OnMainWindowAsync(harness, async window =>
        {
            var page = window.GetVisualDescendants().OfType<DiscoverPage>().Single();
            var scroller = page.GetVisualDescendants().OfType<ScrollViewer>().First();
            var button = page.GetVisualDescendants().OfType<ScrollToTopButton>().Single();
            scroller.Offset = new Vector(0, viewports * scroller.Viewport.Height);
            window.UpdateLayout();
            var before = scroller.Offset.Y;

            await viewModel.OpenContentCommand.ExecuteAsync(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer"));
            window.UpdateLayout();
            Assert.True(viewModel.CurrentWindowContent);
            await PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand);
            window.UpdateLayout();
            return (before, scroller.Offset.Y, button.IsEffectivelyVisible);
        });

        Assert.True(viewModel.CurrentWindowDiscover);
        Assert.True(before > 0, "The list did not scroll, so the test proves nothing.");
        Assert.Equal(before, after);
        Assert.Equal(shown, visible);
    }

    [Fact]
    public async Task BackThenForward_ReturnsToTheSameModPage()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;
        var mod = viewModel.SelectedContent;

        await OnMainWindowAsync(harness, async window =>
        {
            await PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand);
            await PressAsync(window, MouseButton.XButton2, viewModel.GoForwardCommand);
        });

        Assert.True(viewModel.CurrentWindowContent);
        Assert.Same(mod, viewModel.SelectedContent);
    }

    [Fact]
    public async Task OpenModal_IgnoresBothButtons()
    {
        using var harness = await OpenModFromDiscoverAsync();
        var viewModel = harness.ViewModel;

        var (stayedOnMod, stayedOnDiscover) = await OnMainWindowAsync(harness, async window =>
        {
            viewModel.SetMainWindowSettings();
            await PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand);
            var onMod = viewModel.CurrentWindowContent;

            viewModel.CloseSettingsCommand.Execute(null);
            await viewModel.GoBackCommand.ExecuteAsync(null);
            viewModel.SetMainWindowSettings();
            await PressAsync(window, MouseButton.XButton2, viewModel.GoForwardCommand);
            return (onMod, viewModel.CurrentWindowDiscover);
        });

        Assert.True(stayedOnMod);
        Assert.True(stayedOnDiscover);
    }

    [Fact]
    public async Task BackButton_OnAPageWithoutTheBackArrow_StaysThere()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        await OnMainWindowAsync(harness, window => PressAsync(window, MouseButton.XButton1, viewModel.GoBackCommand));

        Assert.True(viewModel.CurrentWindowHome);
    }

    private static async Task<ViewModelHarness> OpenModFromDiscoverAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        viewModel.SetMainWindowDiscover();
        await viewModel.EnsureDiscoverLoadedAsync();
        viewModel.SearchText = "Flight";
        await viewModel.OpenContentCommand.ExecuteAsync(viewModel.DiscoverItems.Single(item => item.ModId == "AdvancedFlightComputer"));
        Assert.True(viewModel.CurrentWindowContent);
        return harness;
    }

    private static Task OnMainWindowAsync(ViewModelHarness harness, Func<Window, Task> body) =>
        OnMainWindowAsync(harness, async window =>
        {
            await body(window);
            return true;
        });

    private static Task<T> OnMainWindowAsync<T>(ViewModelHarness harness, Func<Window, Task<T>> body) =>
        HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = harness.ViewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                return await body(window);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>Presses a mouse button in the middle of the window and waits for what the command it may start does.</summary>
    private static Task PressAsync(Window window, MouseButton button, IAsyncRelayCommand command)
    {
        var center = new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);
        window.MouseDown(center, button);
        window.MouseUp(center, button);
        return command.ExecutionTask ?? Task.CompletedTask;
    }
}
