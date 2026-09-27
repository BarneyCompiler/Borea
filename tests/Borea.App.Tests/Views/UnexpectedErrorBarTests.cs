using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class UnexpectedErrorBarTests
{
    [Fact]
    public async Task Actions_CopyTheDetails_OpenTheReport_AndCloseTheBar()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;
        var clipboard = new UnexpectedErrorTests.FakeWindowServices();
        viewModel.WindowServices = clipboard;
        var opened = new List<string>();
        viewModel.OpenWithSystem = opened.Add;
        viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));

        await HeadlessApp.RunAsync(harness, () =>
        {
            var bar = new UnexpectedErrorBar();
            var window = new Window { Width = 800, Height = 300, DataContext = viewModel, Content = bar };
            window.Show();
            try
            {
                bar.UpdateLayout();
                // the click finds the button only in a rendered frame
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var buttons = bar.GetVisualDescendants().OfType<Button>().ToList();
                Click(window, buttons.Single(button => Equals(button.Content, harness.Localization.UnexpectedErrorCopyDetails)));
                Click(window, buttons.Single(button => Equals(button.Content, harness.Localization.UnexpectedErrorReport)));
                Click(window, buttons.Single(button => button.Command == viewModel.DismissUnexpectedErrorCommand));
                return Task.FromResult(true);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("InvalidOperationException: The instance is gone.", clipboard.CopiedText);
        Assert.StartsWith(MainViewModel.ReportBugUrl + "?title=", Assert.Single(opened));
        Assert.Null(viewModel.UnexpectedError);
    }

    [Fact]
    public async Task MainWindow_ShowsTheBar_OnlyWhileThereIsAnError()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (bars, visibleBefore, visibleWithError, visibleAfterDismiss) = await HeadlessApp.RunAsync(harness, () =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                var found = window.GetVisualDescendants().OfType<UnexpectedErrorBar>().ToList();
                var bar = found.Single();
                var before = bar.IsVisible;
                viewModel.ShowUnexpectedError(new InvalidOperationException("The instance is gone."));
                Dispatcher.UIThread.RunJobs();
                var withError = bar.IsVisible;
                viewModel.DismissUnexpectedErrorCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                return Task.FromResult((found.Count, before, withError, bar.IsVisible));
            }
            finally
            {
                window.Close();
            }
        });

        // the window must use this control, because the tests of this class do not see an inline copy of the bar
        Assert.Equal(1, bars);
        Assert.False(visibleBefore);
        Assert.True(visibleWithError);
        Assert.False(visibleAfterDismiss);
    }

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
