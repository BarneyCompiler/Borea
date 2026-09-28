using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;
using Borea.App.Views.Pages;

namespace Borea.App.Tests.Views;

/// <summary>
/// A click beside a text field takes its focus, so its caret goes away and the keys
/// no longer reach it, and every click on a control keeps its own job (#676).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class TextFieldFocusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClickBesideTheSearchField_TakesItsFocus_AlsoWhenTheWindowIsActivatedAgain(bool onAHeading)
    {
        using var harness = await OpenDiscoverAsync();

        var (before, after, again) = await OnMainWindowAsync(harness, window =>
        {
            var field = SearchField(window);
            ClickCenter(window, field);
            var before = field.IsFocused;

            if (onAHeading)
                ClickCenter(window, Page(window).GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == harness.Localization.DiscoverHideInstalled));
            else
                Click(window, BelowTheRows(window));
            var after = field.IsFocused;

            window.Activate();
            Dispatcher.UIThread.RunJobs();
            return (before, after, field.IsFocused);
        });

        Assert.True(before);
        Assert.False(after);
        Assert.False(again);
    }

    [Fact]
    public async Task ClickOnAButton_AfterAClickIntoTheSearchField_RunsTheButtonAndTakesTheFocus()
    {
        using var harness = await OpenDiscoverAsync();
        var viewModel = harness.ViewModel;

        var (fieldFocused, buttonFocused) = await OnMainWindowAsync(harness, window =>
        {
            var field = SearchField(window);
            ClickCenter(window, field);
            var home = window.GetVisualDescendants().OfType<Button>().Single(button => AutomationProperties.GetName(button) == harness.Localization.NavigationHome);
            ClickCenter(window, home);
            return (field.IsFocused, home.IsFocused);
        });

        Assert.True(viewModel.CurrentWindowHome);
        Assert.False(fieldFocused);
        Assert.True(buttonFocused);
    }

    [Fact]
    public async Task ClickAndDragInsideTheSearchField_KeepItsFocusAndItsCaret()
    {
        using var harness = await OpenDiscoverAsync();
        harness.ViewModel.SearchText = "Flight";

        var (lost, focused, caret, selected) = await OnMainWindowAsync(harness, window =>
        {
            var field = SearchField(window);
            var lost = 0;
            ClickCenter(window, field);
            field.LostFocus += (_, _) => lost++;

            var end = EndOfTheText(window, field);
            Click(window, end);
            var caret = field.CaretIndex;
            var start = new Point(end.X - 30, end.Y);
            window.MouseDown(end, MouseButton.Left);
            window.MouseMove(start, RawInputModifiers.LeftMouseButton);
            window.MouseUp(start, MouseButton.Left);
            return (lost, field.IsFocused, caret, field.SelectedText);
        });

        Assert.Equal(0, lost);
        Assert.True(focused);
        Assert.Equal("Flight".Length, caret);
        Assert.False(string.IsNullOrEmpty(selected));
    }

    [Fact]
    public async Task KeysAfterAClickBesideTheSearchField_DoNotChangeItsText_AndTabStartsAtTheFirstControl()
    {
        using var harness = await OpenDiscoverAsync();
        var viewModel = harness.ViewModel;
        viewModel.SearchText = "Flight";

        var (typed, erased, tabbedTo, first) = await OnMainWindowAsync(harness, window =>
        {
            var field = SearchField(window);
            ClickCenter(window, field);
            Click(window, BelowTheRows(window));

            // the text is read after each key, because a letter and a Backspace that both reach the field cancel each other out
            window.KeyTextInput("x");
            var typed = field.Text;
            Press(window, PhysicalKey.Backspace);
            var erased = field.Text;
            Press(window, PhysicalKey.Tab);
            return (typed, erased, window.FocusManager.GetFocusedElement(), window.FindControl<Button>("TasksButton"));
        });

        Assert.Equal("Flight", typed);
        Assert.Equal("Flight", erased);
        Assert.Equal("Flight", viewModel.SearchText);
        Assert.NotNull(first);
        Assert.Same(first, tabbedTo);
    }

    [Fact]
    public async Task ClickOnTheScrollBar_KeepsTheFocusOfTheSearchField()
    {
        using var harness = await ViewModelHarness.CreateAsync(editSnapshot: json => ViewModelHarness.WithCopies(json, "KSArmory", 60));
        harness.ViewModel.SetMainWindowDiscover();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();

        var focused = await OnMainWindowAsync(harness, window =>
        {
            var field = SearchField(window);
            ClickCenter(window, field);
            var bar = Page(window).GetVisualDescendants().OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Vertical && bar.IsEffectivelyVisible);
            Click(window, bar, new Point(bar.Bounds.Width / 2, bar.Bounds.Height - 40));
            return field.IsFocused;
        });

        Assert.True(focused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickBesideATextFieldInAModal_TakesItsFocus_AndTabStaysInTheModal(bool backwards)
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (before, after, typed, tabbedTo, expected) = await OnMainWindowAsync(harness, window =>
        {
            viewModel.BeginCreateInstanceCommand.Execute(null);
            window.UpdateLayout();
            var modal = window.GetVisualDescendants().OfType<InstanceNameModal>().Single();
            var field = modal.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsEffectivelyVisible);
            ClickCenter(window, field);
            var before = field.IsFocused;

            ClickCenter(window, modal.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == harness.Localization.ModalNameLabel));
            var after = field.IsFocused;
            window.KeyTextInput("x");
            var typed = field.Text;

            Press(window, PhysicalKey.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
            var buttons = modal.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
            return (before, after, typed, window.FocusManager.GetFocusedElement(), backwards ? buttons.Last() : buttons.First());
        });

        Assert.True(before);
        Assert.False(after);
        Assert.Equal(string.Empty, typed ?? string.Empty);
        Assert.Equal(string.Empty, viewModel.ModalInstanceName);
        Assert.Same(expected, tabbedTo);
    }

    [Fact]
    public async Task ClickBesideATextFieldInAModal_ThenEscape_ClosesTheModal()
    {
        using var harness = await ViewModelHarness.CreateAsync();
        var viewModel = harness.ViewModel;

        var (away, open) = await OnMainWindowAsync(harness, window =>
        {
            viewModel.BeginCreateInstanceCommand.Execute(null);
            window.UpdateLayout();
            var modal = window.GetVisualDescendants().OfType<InstanceNameModal>().Single();
            var field = modal.GetVisualDescendants().OfType<TextBox>().Single(box => box.IsEffectivelyVisible);
            ClickCenter(window, field);
            ClickCenter(window, modal.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == harness.Localization.ModalNameLabel));
            var away = !field.IsFocused;

            Press(window, PhysicalKey.Escape, RawInputModifiers.None);
            return (away, viewModel.IsNameModalOpen);
        });

        Assert.True(away);
        Assert.False(open);
    }

    private static async Task<ViewModelHarness> OpenDiscoverAsync()
    {
        var harness = await ViewModelHarness.CreateAsync();
        harness.ViewModel.SetMainWindowDiscover();
        await harness.ViewModel.EnsureDiscoverLoadedAsync();
        return harness;
    }

    private static Task<T> OnMainWindowAsync<T>(ViewModelHarness harness, Func<Window, T> body) =>
        HeadlessApp.RunAsync(harness, () =>
        {
            var window = new MainWindow { DataContext = harness.ViewModel };
            window.Show();
            try
            {
                // the window takes its first activation from the dispatcher, and that sets the focus
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                return Task.FromResult(body(window));
            }
            finally
            {
                window.Close();
            }
        });

    private static DiscoverPage Page(Window window) => window.GetVisualDescendants().OfType<DiscoverPage>().Single();

    private static TextBox SearchField(Window window) =>
        Page(window).GetVisualDescendants().OfType<TextBox>().Single(box => box.Classes.Contains("search"));

    /// <summary>A point of the list column below its last row, where nothing can take the focus.</summary>
    private static Point BelowTheRows(Window window)
    {
        var scroller = Page(window).GetVisualDescendants().OfType<ScrollViewer>().First();
        var corner = scroller.TranslatePoint(default, window)!.Value;
        return new Point(corner.X + scroller.Bounds.Width / 2, corner.Y + scroller.Bounds.Height - 60);
    }

    /// <summary>A point inside the field just after the last letter of its text.</summary>
    private static Point EndOfTheText(Window window, TextBox field)
    {
        var presenter = field.GetVisualDescendants().OfType<TextPresenter>().Single();
        var corner = presenter.TranslatePoint(default, window)!.Value;
        return new Point(corner.X + presenter.TextLayout.WidthIncludingTrailingWhitespace + 20, corner.Y + presenter.Bounds.Height / 2);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Click(Window window, Visual target, Point inTarget) => Click(window, target.TranslatePoint(inTarget, window)!.Value);

    private static void ClickCenter(Window window, Visual target) =>
        Click(window, target, new Point(target.Bounds.Width / 2, target.Bounds.Height / 2));
}
