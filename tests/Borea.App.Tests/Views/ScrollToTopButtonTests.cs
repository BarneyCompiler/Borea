using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class ScrollToTopButtonTests
{
    [Fact]
    public async Task ShowsOnlyPastOneViewport_AndAClickTakesTheListToTheTopAndHidesIt()
    {
        var seen = await HeadlessApp.RunAsync(() =>
        {
            var (window, scroller, button, first) = ShowLongList();
            var atTop = button.IsVisible;
            scroller.Offset = new Vector(0, scroller.Viewport.Height);
            var oneViewport = button.IsVisible;
            scroller.Offset = new Vector(0, scroller.Viewport.Height + 1);
            var pastOneViewport = button.IsVisible;

            window.UpdateLayout();
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left);
            window.MouseUp(center, MouseButton.Left);
            var result = (atTop, oneViewport, pastOneViewport, AfterClick: button.IsVisible, Offset: scroller.Offset.Y, FirstFocused: first.IsFocused);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.False(seen.atTop);
        Assert.False(seen.oneViewport);
        Assert.True(seen.pastOneViewport);
        Assert.False(seen.AfterClick);
        Assert.Equal(0, seen.Offset);
        Assert.False(seen.FirstFocused);
    }

    [Fact]
    public async Task EnterOnTheFocusedButton_GoesOnFromTheTopOfTheList()
    {
        var seen = await HeadlessApp.RunAsync(() =>
        {
            var (window, scroller, button, first) = ShowLongList();
            scroller.Offset = new Vector(0, 2 * scroller.Viewport.Height);
            button.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            var result = (Offset: scroller.Offset.Y, button.IsVisible, FirstFocused: first.IsFocused);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal(0, seen.Offset);
        Assert.False(seen.IsVisible);
        Assert.True(seen.FirstFocused);
    }

    [Fact]
    public async Task TheWheelOnTheShownButton_ScrollsTheList()
    {
        var seen = await HeadlessApp.RunAsync(() =>
        {
            var (window, scroller, button, _) = ShowLongList();
            scroller.Offset = new Vector(0, 3 * scroller.Viewport.Height);
            window.UpdateLayout();
            var before = scroller.Offset.Y;
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(center, new Vector(0, -1));
            var down = scroller.Offset.Y;
            window.MouseWheel(center, new Vector(0, 2));
            var result = (before, down, up: scroller.Offset.Y);
            window.Close();
            return Task.FromResult(result);
        });

        Assert.Equal(seen.before + 50, seen.down);
        Assert.Equal(seen.before - 50, seen.up);
    }

    private static (Window Window, ScrollViewer Scroller, ScrollToTopButton Button, Button First) ShowLongList()
    {
        var first = new Button { Content = "First" };
        var scroller = new ScrollViewer { Content = new StackPanel { Children = { first, new Border { Height = 3000 } } } };
        var button = new ScrollToTopButton { Target = scroller, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Content = "Top" };
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { scroller, button } } };
        window.Show();
        window.UpdateLayout();
        return (window, scroller, button, first);
    }
}
