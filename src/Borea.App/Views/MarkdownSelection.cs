using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>
/// Lets a selection go across the texts of a <see cref="MarkdownView"/>, through paragraphs, headings, lists, tables and code blocks.
/// A text selects only inside itself and keeps the pointer while the button is down, so the view follows the drag: it selects
/// the rest of the text the drag started in, every text between, and the text under the pointer up to the pointer.
/// A copy then takes the selected part of each text, with a blank line between them.
/// </summary>
internal sealed class MarkdownSelection
{
    private const string BlockBreak = "\n\n";

    private readonly MarkdownView _view;

    /// <summary>The text the drag started in, and where in it.</summary>
    private SelectableTextBlock? _anchor;

    private int _anchorIndex;

    public MarkdownSelection(MarkdownView view)
    {
        _view = view;

        // a press beside the text, such as after the end of a line, reaches the view only when it has a background
        view.Background ??= Brushes.Transparent;
        view.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Bubble, handledEventsToo: true);
        view.AddHandler(InputElement.PointerMovedEvent, Moved, RoutingStrategies.Bubble, handledEventsToo: true);
        view.AddHandler(InputElement.PointerReleasedEvent, (_, _) => _anchor = null, RoutingStrategies.Bubble, handledEventsToo: true);
        view.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        view.AddHandler(SelectableTextBlock.CopyingToClipboardEvent, Copying);
        view.AddHandler(InputElement.LostFocusEvent, LostFocus);
    }

    /// <summary>
    /// A press on a text reaches the view after the text handled it, so a single click without Shift has already put the caret where
    /// the drag starts. A press beside the text that nothing else handled puts the caret into the closest text. A double click,
    /// a triple click and a Shift click select inside one text as before.
    /// </summary>
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed)
            return;

        var texts = Texts();
        var pressed = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<SelectableTextBlock>().FirstOrDefault();
        var single = e.ClickCount == 1 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (pressed is null && single && !e.Handled && texts.Count > 0)
        {
            var (position, index) = Hit(texts, e.GetPosition(_view));
            pressed = texts[position];
            Select(pressed, index, index);
            pressed.Focus(NavigationMethod.Pointer);
        }

        foreach (var text in texts.Where(text => text != pressed))
            text.ClearSelection();

        _anchor = single ? pressed : null;
        _anchorIndex = pressed?.SelectionStart ?? 0;
    }

    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_anchor is null || !e.GetCurrentPoint(_view).Properties.IsLeftButtonPressed)
            return;

        var texts = Texts();
        var from = texts.IndexOf(_anchor);
        if (from < 0)
        {
            _anchor = null;
            return;
        }

        var (to, index) = Hit(texts, e.GetPosition(_view));
        for (var position = 0; position < texts.Count; position++)
        {
            var text = texts[position];
            if (position == from)
            {
                Select(text, _anchorIndex, to == from ? index : to > from ? Length(text) : 0);
            }
            else if (position == to)
            {
                Select(text, to > from ? 0 : Length(text), index);
            }
            else if (Math.Min(from, to) < position && position < Math.Max(from, to))
            {
                Select(text, 0, Length(text));
            }
            else
            {
                text.ClearSelection();
            }
        }
    }

    /// <summary>
    /// Copy takes every selected text, also when the focused text has no part of the selection. Select all selects the whole view
    /// when the focus is in one of its texts.
    /// </summary>
    private void KeyDown(object? sender, KeyEventArgs e)
    {
        var keys = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        if (keys is null || e.Source is not SelectableTextBlock)
            return;

        if (keys.Copy.Any(gesture => gesture.Matches(e)) && Selected() is { Count: > 0 } selected)
        {
            e.Handled = true;
            _ = CopyAsync(selected);
        }
        else if (keys.SelectAll.Any(gesture => gesture.Matches(e)))
        {
            e.Handled = true;
            foreach (var text in Texts())
                Select(text, 0, Length(text));
        }
    }

    /// <summary>The Copy item of the context menu of a text copies the whole selection, not only that text's part.</summary>
    private void Copying(object? sender, RoutedEventArgs e)
    {
        if (Selected() is not { Count: > 1 } selected)
            return;

        e.Handled = true;
        _ = CopyAsync(selected);
    }

    /// <summary>
    /// A text that loses the focus clears its own selection, unless its context menu took the focus. The other texts of the
    /// selection never had the focus, so the view clears them when the focus leaves the view.
    /// </summary>
    private void LostFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is not SelectableTextBlock text || text.ContextFlyout?.IsOpen == true || text.ContextMenu?.IsOpen == true)
            return;

        if (e.NewFocusedElement is Visual focused && _view.IsVisualAncestorOf(focused))
            return;

        foreach (var other in Texts())
            other.ClearSelection();
    }

    private async Task CopyAsync(List<string> selected)
    {
        if (TopLevel.GetTopLevel(_view)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(string.Join(BlockBreak, selected));
    }

    /// <summary>The texts of the view in the order they are read, so a list item comes before the paragraph after its list.</summary>
    private List<SelectableTextBlock> Texts() => _view.GetVisualDescendants().OfType<SelectableTextBlock>().Where(text => text.IsEffectivelyVisible).ToList();

    private List<string> Selected() => Texts().Select(text => text.SelectedText).Where(selected => selected.Length > 0).ToList();

    /// <summary>
    /// The text under the point and the place in it. A point outside every text, such as the gap between two blocks or the marker
    /// of a list item, belongs to the last text that starts above it: at its end when the point is below it, and beside its line
    /// otherwise. A point above every text is the start of the first.
    /// </summary>
    private (int Text, int Index) Hit(List<SelectableTextBlock> texts, Point point)
    {
        // a text whose selection changed drops its layout until it is measured again, and without a layout it finds no place
        _view.UpdateLayout();
        for (var position = 0; position < texts.Count; position++)
        {
            if (Bounds(texts[position]).Contains(point))
                return (position, Position(texts[position], point));
        }

        for (var position = texts.Count - 1; position >= 0; position--)
        {
            var bounds = Bounds(texts[position]);
            if (bounds.Top <= point.Y)
                return (position, point.Y >= bounds.Bottom ? Length(texts[position]) : Position(texts[position], point));
        }

        return (0, 0);
    }

    private Rect Bounds(SelectableTextBlock text) =>
        text.TranslatePoint(default, _view) is { } origin ? new Rect(origin, text.Bounds.Size) : default;

    /// <summary>The place in the text closest to the point, found as the text finds the place of its own drag.</summary>
    private int Position(SelectableTextBlock text, Point point)
    {
        var inText = _view.TranslatePoint(point, text) ?? default;
        var inLayout = inText - new Point(text.Padding.Left, text.Padding.Top);
        return text.TextLayout.HitTestPoint(inLayout).TextPosition;
    }

    private static int Length(SelectableTextBlock text) =>
        (text.Inlines is { Count: > 0 } inlines ? inlines.Text : text.Text)?.Length ?? 0;

    private static void Select(SelectableTextBlock text, int start, int end)
    {
        text.SetCurrentValue(SelectableTextBlock.SelectionStartProperty, start);
        text.SetCurrentValue(SelectableTextBlock.SelectionEndProperty, end);
    }
}
