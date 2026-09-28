using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;
using Borea.Core.Instances;

namespace Borea.App.Tests.Views;

/// <summary>
/// The app style makes the scrollbar of every scroll viewer expand as soon as the
/// pointer is over it and shrink as soon as the pointer leaves it.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ScrollBarTimingTests
{
    [Fact]
    public async Task ScrollBarsOfDiscoverAndTheInstancePage_GetTheTimingOfTheAppStyle()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        await harness.Services.Instances.CreateAsync("Main", InstanceSource.Custom.Value);
        var viewModel = harness.ViewModel;
        await viewModel.LoadAsync();

        var bars = await HeadlessApp.RunAsync(harness, async () =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                var found = new List<(string Page, TimeSpan Show, TimeSpan Hide, bool AutoHide)>();
                foreach (var (page, open) in new (string, Func<Task>)[]
                {
                    ("Discover", async () =>
                    {
                        viewModel.SetMainWindowDiscover();
                        await viewModel.EnsureDiscoverLoadedAsync();
                    }),
                    ("Instance", () => viewModel.Instances.Single().OpenCommand.ExecuteAsync(null)),
                })
                {
                    await open();
                    window.UpdateLayout();
                    var shown = page == "Instance"
                        ? window.GetVisualDescendants().OfType<InstancePage>().Single(instance => instance.IsEffectivelyVisible)
                        : (Visual)window.GetVisualDescendants().OfType<DiscoverPage>().Single(discover => discover.IsEffectivelyVisible);
                    found.AddRange(shown.GetVisualDescendants().OfType<ScrollViewer>()
                        .Where(viewer => viewer.IsEffectivelyVisible)
                        .SelectMany(ScrollBarsOf)
                        .Select(bar => (page, bar.ShowDelay, bar.HideDelay, bar.AllowAutoHide)));
                }

                return found;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(bars, bar => bar.Page == "Discover");
        Assert.Contains(bars, bar => bar.Page == "Instance");
        Assert.All(bars, bar => Assert.Equal((bar.Page, TimeSpan.Zero, TimeSpan.Zero, true), bar));
    }

    [Fact]
    public async Task PointerOverTheScrollBar_ExpandsIt_AndLeavingIt_ShrinksIt_AtOnce()
    {
        var (before, over, left) = await HeadlessApp.RunAsync(async () =>
        {
            var viewer = new ScrollViewer { Content = new Border { Height = 2000 } };
            var window = new Window { Width = 400, Height = 300, Content = viewer };
            window.Show();
            try
            {
                window.UpdateLayout();
                var bar = ScrollBarsOf(viewer).Single(scrollBar => scrollBar.Orientation == Orientation.Vertical);
                var before = bar.IsExpanded;

                var middle = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value;
                window.MouseMove(middle);
                await JobsAsync();
                var over = bar.IsExpanded;

                window.MouseMove(new Point(20, 20));
                await JobsAsync();
                return (before, over, bar.IsExpanded);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.False(before);
        Assert.True(over);
        Assert.False(left);
    }

    [Fact]
    public async Task ScrollViewerThatSetsItsOwnTiming_KeepsIt()
    {
        var own = TimeSpan.FromSeconds(1);
        var bars = await HeadlessApp.RunAsync(() =>
        {
            var viewer = new ScrollViewer
            {
                Content = new Border { Height = 2000 },
                Styles =
                {
                    new Style(selector => selector.OfType<ScrollViewer>().Template().OfType<ScrollBar>())
                    {
                        Setters =
                        {
                            new Setter(ScrollBar.ShowDelayProperty, own),
                            new Setter(ScrollBar.HideDelayProperty, own),
                        },
                    },
                },
            };
            var window = new Window { Width = 400, Height = 300, Content = viewer };
            window.Show();
            try
            {
                window.UpdateLayout();
                return Task.FromResult(ScrollBarsOf(viewer).Select(bar => (bar.ShowDelay, bar.HideDelay)).ToList());
            }
            finally
            {
                window.Close();
            }
        });

        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.Equal((own, own), bar));
    }

    private static IEnumerable<ScrollBar> ScrollBarsOf(ScrollViewer viewer)
        => viewer.GetVisualDescendants().OfType<ScrollBar>().Where(bar => bar.TemplatedParent == viewer);

    /// <summary>
    /// Runs the queued work a few times, far less long than the half second the
    /// scrollbar waits without the app style.
    /// </summary>
    private static async Task JobsAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
