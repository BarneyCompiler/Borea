using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class DropdownAnimationTests
{
    [Fact]
    public async Task AMenuFlyout_FadesInFromItsClosedLook_AndFadesOutBeforeItGoes()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            flyout.ShowAt(button);
            var panel = PanelOf(flyout);
            var onArrival = (Opacity: panel.Opacity, Transform: panel.RenderTransform, Open: flyout.IsOpen);

            await HeadlessApp.FramesAsync();
            var arrived = (Opacity: panel.Opacity, Transform: panel.RenderTransform);

            flyout.Hide();
            var onLeaving = (Opacity: panel.Opacity, StillOpen: flyout.IsOpen);
            await HeadlessApp.FramesAsync();
            var result = (onArrival, arrived, onLeaving, Gone: flyout.IsOpen);
            window.Close();
            return result;
        });

        Assert.True(seen.onArrival.Open);
        Assert.Equal(0, seen.onArrival.Opacity);
        Assert.NotNull(seen.onArrival.Transform);
        Assert.Equal(1, seen.arrived.Opacity);
        Assert.Null(seen.arrived.Transform);
        Assert.Equal(1, seen.onLeaving.Opacity);
        Assert.True(seen.onLeaving.StillOpen, "A flyout must stay on screen while it fades out.");
        Assert.False(seen.Gone);
    }

    [Fact]
    public async Task AClosedFlyout_LeavesItsPanelWhereTheNextOpenStartsFrom()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            flyout.ShowAt(button);
            await HeadlessApp.FramesAsync();
            flyout.Hide();
            await HeadlessApp.FramesAsync();
            flyout.ShowAt(button);
            var reopened = PanelOf(flyout).Opacity;
            window.Close();
            return (reopened);
        });

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task AComboBox_FadesItsPanelIn_AndClosesWithoutBeingPutBackUpToFadeItOut()
    {
        var seen = await HeadlessApp.RunAsync(async () =>
        {
            var (window, box) = ShowComboBox();
            var panel = PanelOf(box);
            box.IsDropDownOpen = true;
            var onArrival = (Opacity: panel.Opacity, Transform: panel.RenderTransform, Open: box.IsDropDownOpen);
            await HeadlessApp.FramesAsync();
            var arrived = (Opacity: panel.Opacity, Transform: panel.RenderTransform);

            box.IsDropDownOpen = false;
            var putBackUp = 0;
            for (var frame = 0; frame < 12; frame++)
            {
                if (box.IsDropDownOpen)
                    putBackUp++;
                await HeadlessApp.FramesAsync(1);
            }

            var result = (onArrival, arrived, PutBackUp: putBackUp, Closed: box.IsDropDownOpen);
            window.Close();
            return result;
        });

        Assert.True(seen.onArrival.Open);
        Assert.Equal(0, seen.onArrival.Opacity);
        Assert.NotNull(seen.onArrival.Transform);
        Assert.Equal(1, seen.arrived.Opacity);
        Assert.Null(seen.arrived.Transform);
        Assert.False(seen.Closed);
        Assert.Equal(0, seen.PutBackUp);
    }

    [Fact]
    public async Task ADropdown_CarriesRoundedCorners_AndClipsToThem()
    {
        var (menu, combo) = await HeadlessApp.RunAsync(async () =>
        {
            var (window, button, flyout) = ShowButtonWithMenu();
            flyout.ShowAt(button);
            var presenter = (MenuFlyoutPresenter)PanelOf(flyout);
            var menu = (Corner: presenter.CornerRadius,
                Clipped: presenter.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "LayoutRoot").ClipToBounds);
            flyout.Hide();
            window.Close();

            var (comboWindow, box) = ShowComboBox();
            box.IsDropDownOpen = true;
            var border = PanelOf(box);
            var combo = (Corner: border.CornerRadius, Clipped: border.ClipToBounds);
            box.IsDropDownOpen = false;
            comboWindow.Close();
            return ((menu, combo));
        });

        Assert.Equal(new CornerRadius(12), menu.Corner);
        Assert.True(menu.Clipped);
        Assert.Equal(new CornerRadius(12), combo.Corner);
        Assert.True(combo.Clipped);
    }

    [Theory]
    [InlineData(PlacementMode.Top)]
    [InlineData(PlacementMode.Bottom)]
    [InlineData(PlacementMode.Left)]
    [InlineData(PlacementMode.Right)]
    public async Task WhereAFlyoutOpens_TellsItWhichWayItSlidesIn(PlacementMode placement)
    {
        var edge = DropdownAnimation.Edge(placement);

        Assert.Equal(placement is PlacementMode.Bottom, edge.Travel.Y > 0);
        Assert.Equal(placement is PlacementMode.Top, edge.Travel.Y < 0);
        Assert.Equal(placement is PlacementMode.Right, edge.Travel.X > 0);
        Assert.Equal(placement is PlacementMode.Left, edge.Travel.X < 0);
        Assert.Equal(DropdownAnimation.Travel, Math.Max(Math.Abs(edge.Travel.X), Math.Abs(edge.Travel.Y)));
    }

    private static (Window Window, Button Button, MenuFlyout Flyout) ShowButtonWithMenu()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuItem { Header = "First" });
        flyout.Items.Add(new MenuItem { Header = "Second" });
        var button = new Button { Classes = { "dropdown" }, Content = "Open", Width = 100, Height = 30 };
        Flyout.SetAttachedFlyout(button, flyout);
        var window = new Window { Width = 400, Height = 300, Content = button };
        window.Show();
        window.UpdateLayout();
        return (window, button, flyout);
    }

    private static (Window Window, ComboBox Box) ShowComboBox()
    {
        var box = new ComboBox { Width = 120, Height = 30 };
        var window = new Window { Width = 400, Height = 300, Content = box };
        window.Show();
        window.UpdateLayout();
        return (window, box);
    }

    private static Control PanelOf(PopupFlyoutBase flyout) => flyout.Popup.Child!;
    private static Border PanelOf(ComboBox box) =>
        (Border)box.GetVisualDescendants().OfType<Popup>().Single().Child!;
}
