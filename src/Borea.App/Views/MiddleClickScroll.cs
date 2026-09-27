using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Borea.App.Views;

/// <summary>
/// Scrolls a scroll viewer while the middle mouse button is held, the way a browser
/// does: the farther the pointer is from where the press started, the faster the
/// content scrolls toward it. App.axaml turns it on for every scroll viewer.
/// </summary>
public static class MiddleClickScroll
{
    /// <summary>How far the pointer can move from the start of the press before the content moves.</summary>
    internal const double DeadZone = 12;

    /// <summary>The scroll speed in pixels per second for each pixel past the dead zone.</summary>
    internal const double SpeedPerPixel = 10;

    internal const double MaxSpeed = 10_000;

    /// <summary>The longest time one frame moves the content for, so a stalled frame does not jump.</summary>
    private static readonly TimeSpan LongestFrame = TimeSpan.FromMilliseconds(100);

    /// <summary>The cursors are made once and kept, because the window can still show one after the scroll ended.</summary>
    private static readonly Dictionary<StandardCursorType, Cursor> Cursors = [];

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("IsEnabled", typeof(MiddleClickScroll));

    static MiddleClickScroll() =>
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, args) => Follow(viewer, args.GetNewValue<bool>()));

    public static bool GetIsEnabled(ScrollViewer viewer) => viewer.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(IsEnabledProperty, value);

    /// <summary>The speed in pixels per second along one axis, for the distance of the pointer from the start of the press.</summary>
    internal static double Speed(double distance)
    {
        var past = Math.Abs(distance) - DeadZone;
        return past <= 0 ? 0 : Math.CopySign(Math.Min(past * SpeedPerPixel, MaxSpeed), distance);
    }

    private static Cursor CursorOf(StandardCursorType type)
    {
        if (!Cursors.TryGetValue(type, out var cursor))
            Cursors[type] = cursor = new Cursor(type);
        return cursor;
    }

    private static void Follow(ScrollViewer viewer, bool enabled)
    {
        viewer.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
        if (enabled)
            viewer.AddHandler(InputElement.PointerPressedEvent, OnPressed);
    }

    /// <summary>
    /// Runs as the press bubbles up, so the innermost scroll viewer that can scroll
    /// takes it, and a control under the pointer that uses the middle button itself
    /// handles the press before it gets here.
    /// </summary>
    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not ScrollViewer viewer
            || args.GetCurrentPoint(viewer).Properties.PointerUpdateKind != PointerUpdateKind.MiddleButtonPressed
            || IsOnLink(args.Source, viewer)
            || TopLevel.GetTopLevel(viewer) is not { } topLevel)
        {
            return;
        }

        var horizontal = viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled && viewer.ScrollBarMaximum.X > 0;
        var vertical = viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled && viewer.ScrollBarMaximum.Y > 0;
        if ((horizontal || vertical) && Session.TryStart(viewer, topLevel, args, horizontal, vertical))
            args.Handled = true;
    }

    /// <summary>A middle press on a link does what a link does with it, as in a browser, and scrolls nothing.</summary>
    private static bool IsOnLink(object? source, ScrollViewer viewer)
    {
        for (var current = source as Visual; current is not null && current != viewer; current = current.GetVisualParent())
        {
            if (current is HyperlinkButton
                || current is Button button && button.Classes.Contains("link")
                || current is Control control && AutomationProperties.GetControlTypeOverride(control) == AutomationControlType.Hyperlink)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One held middle press. It holds the pointer capture and ends on the release, on another button, on Escape and when the capture goes elsewhere.</summary>
    private sealed class Session
    {
        private readonly ScrollViewer _viewer;
        private readonly TopLevel _topLevel;
        private readonly IPointer _pointer;
        private readonly Point _origin;
        private readonly bool _horizontal;
        private readonly bool _vertical;
        private Point _position;
        private TimeSpan? _lastFrame;
        private IDisposable? _cursorValue;
        private bool _ended;

        private Session(ScrollViewer viewer, TopLevel topLevel, PointerPressedEventArgs args, bool horizontal, bool vertical)
        {
            _viewer = viewer;
            _topLevel = topLevel;
            _pointer = args.Pointer;
            _origin = _position = args.GetPosition(viewer);
            _horizontal = horizontal;
            _vertical = vertical;
        }

        public static bool TryStart(ScrollViewer viewer, TopLevel topLevel, PointerPressedEventArgs args, bool horizontal, bool vertical)
        {
            args.Pointer.Capture(viewer);
            if (args.Pointer.Captured != viewer)
                return false;

            new Session(viewer, topLevel, args, horizontal, vertical).Start();
            return true;
        }

        private void Start()
        {
            _viewer.PointerMoved += OnMoved;
            _viewer.PointerReleased += OnReleased;
            _viewer.PointerCaptureLost += OnCaptureLost;
            _topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            // the captured element sets the cursor, and the animation priority wins over a cursor the page sets
            var cursor = _horizontal && _vertical ? StandardCursorType.SizeAll
                : _vertical ? StandardCursorType.SizeNorthSouth
                : StandardCursorType.SizeWestEast;
            _cursorValue = _viewer.SetValue(InputElement.CursorProperty, CursorOf(cursor), BindingPriority.Animation);
            _topLevel.RequestAnimationFrame(OnFrame);
        }

        private void End()
        {
            if (_ended)
                return;

            _ended = true;
            _viewer.PointerMoved -= OnMoved;
            _viewer.PointerReleased -= OnReleased;
            _viewer.PointerCaptureLost -= OnCaptureLost;
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _cursorValue?.Dispose();
            if (_pointer.Captured == _viewer)
                _pointer.Capture(null);
        }

        /// <summary>
        /// Avalonia reports a button that goes down or up while another one is held as
        /// a move. So a move that changes a button is the release of the middle button
        /// or the press of another one, and either ends the scroll. Another button does
        /// nothing else, because the content under the pointer moved since the press.
        /// </summary>
        private void OnMoved(object? sender, PointerEventArgs args)
        {
            var point = args.GetCurrentPoint(_viewer);
            _position = point.Position;
            if (point.Properties.PointerUpdateKind == PointerUpdateKind.Other)
                return;

            End();
            args.Handled = true;
        }

        private void OnReleased(object? sender, PointerReleasedEventArgs args)
        {
            End();
            args.Handled = true;
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args) => End();

        private void OnKeyDown(object? sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape)
                return;

            End();
            args.Handled = true;
        }

        private void OnFrame(TimeSpan now)
        {
            if (_ended)
                return;

            // a capture that moved to a control inside the viewer raises no capture lost here
            if (_pointer.Captured != _viewer)
            {
                End();
                return;
            }

            var seconds = _lastFrame is { } last ? Math.Min((now - last).TotalSeconds, LongestFrame.TotalSeconds) : 0;
            _lastFrame = now;
            var distance = _position - _origin;
            var offset = _viewer.Offset;
            var maximum = _viewer.ScrollBarMaximum;
            var next = new Vector(
                _horizontal ? Math.Clamp(offset.X + Speed(distance.X) * seconds, 0, maximum.X) : offset.X,
                _vertical ? Math.Clamp(offset.Y + Speed(distance.Y) * seconds, 0, maximum.Y) : offset.Y);
            if (next != offset)
                _viewer.SetCurrentValue(ScrollViewer.OffsetProperty, next);

            _topLevel.RequestAnimationFrame(OnFrame);
        }
    }
}
