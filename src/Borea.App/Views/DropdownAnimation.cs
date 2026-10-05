using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Borea.App.Views;

public static class DropdownAnimation
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(120);
    internal const double Travel = 6;
    internal const double Shrink = 0.96;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>("IsEnabled", typeof(DropdownAnimation));
    private static readonly AttachedProperty<bool> IsHoldingFlyoutProperty =
        AvaloniaProperty.RegisterAttached<PopupFlyoutBase, bool>("IsHolding", typeof(DropdownAnimation));
    private static readonly AttachedProperty<int> MoveProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Move", typeof(DropdownAnimation));

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;
        IsEnabledProperty.Changed.AddClassHandler<ComboBox>((box, args) => OnIsEnabledChanged(box, args.GetNewValue<bool>()));
        FlyoutBase.AttachedFlyoutProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.GetNewValue<FlyoutBase>() is PopupFlyoutBase flyout)
            {
                flyout.Opened -= OnFlyoutOpened;
                flyout.Closing -= OnFlyoutClosing;
                flyout.Opened += OnFlyoutOpened;
                flyout.Closing += OnFlyoutClosing;
            }
        });
    }

    public static bool GetIsEnabled(ComboBox box) => box.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ComboBox box, bool value) => box.SetValue(IsEnabledProperty, value);

    private static void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is PopupFlyoutBase flyout)
            Move(flyout.Popup, flyout.Placement, opening: true);
    }

    private static void OnFlyoutClosing(object? sender, CancelEventArgs e)
    {
        if (sender is not PopupFlyoutBase flyout || flyout.GetValue(IsHoldingFlyoutProperty))
            return;

        e.Cancel = true;
        flyout.SetValue(IsHoldingFlyoutProperty, true);
        Move(flyout.Popup, flyout.Placement, opening: false, onDone: () =>
        {
            flyout.Hide();
            flyout.SetValue(IsHoldingFlyoutProperty, false);
        });
    }

    private static void OnIsEnabledChanged(ComboBox box, bool enabled)
    {
        box.DropDownOpened -= OnDropDownOpened;
        if (enabled)
            box.DropDownOpened += OnDropDownOpened;
    }

    private static void OnDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox box)
            Move(PopupOf(box), PlacementMode.Bottom, opening: true);
    }

    private static Popup? PopupOf(Control owner) => Popped(owner);

    private static Popup? Popped(Visual visual)
    {
        if (visual is Popup popup)
            return popup;

        foreach (var child in visual.GetVisualChildren())
        {
            if (Popped(child) is { } found)
                return found;
        }

        return null;
    }

    internal static (Vector Travel, RelativePoint Origin) Edge(PlacementMode placement) => placement switch
    {
        PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight
            => (new Vector(0, -Travel), new RelativePoint(0.5, 1, RelativeUnit.Relative)),
        PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom
            => (new Vector(-Travel, 0), new RelativePoint(1, 0.5, RelativeUnit.Relative)),
        PlacementMode.Right or PlacementMode.RightEdgeAlignedTop or PlacementMode.RightEdgeAlignedBottom
            => (new Vector(Travel, 0), new RelativePoint(0, 0.5, RelativeUnit.Relative)),
        _ => (new Vector(0, Travel), new RelativePoint(0.5, 0, RelativeUnit.Relative)),
    };

    private static void Move(Popup? popup, PlacementMode placement, bool opening, Action? onDone = null)
    {
        if (popup?.Child is not { } panel || TopLevel.GetTopLevel(panel) is not { } topLevel)
        {
            onDone?.Invoke();
            return;
        }

        var (travel, origin) = Edge(placement);
        var away = new Look(travel, Shrink, 0);
        var resting = new Look(Vector.Zero, 1, 1);

        var from = panel.RenderTransform is TransformGroup
            ? Looked(panel, opening ? away : resting)
            : opening ? away : resting;
        var to = opening ? resting : away;

        var shifts = new TranslateTransform { X = from.Travel.X, Y = from.Travel.Y };
        var grows = new ScaleTransform { ScaleX = from.Shrink, ScaleY = from.Shrink };
        var transforms = new TransformGroup();
        transforms.Children.Add(grows);
        transforms.Children.Add(shifts);
        panel.RenderTransformOrigin = origin;
        panel.RenderTransform = transforms;
        panel.Opacity = from.Opacity;

        var mine = panel.GetValue(MoveProperty) + 1;
        panel.SetValue(MoveProperty, mine);
        var started = TimeSpan.Zero;
        var first = true;

        void OnFrame(TimeSpan now)
        {
            if (panel.GetValue(MoveProperty) != mine)
                return;

            if (first)
            {
                first = false;
                started = now;
            }

            var progress = Math.Clamp((now - started) / Duration, 0, 1);
            var eased = Eased(progress);
            panel.Opacity = from.Opacity + (to.Opacity - from.Opacity) * eased;
            shifts.X = from.Travel.X + (to.Travel.X - from.Travel.X) * eased;
            shifts.Y = from.Travel.Y + (to.Travel.Y - from.Travel.Y) * eased;
            grows.ScaleX = grows.ScaleY = from.Shrink + (to.Shrink - from.Shrink) * eased;

            if (progress < 1)
            {
                topLevel.RequestAnimationFrame(OnFrame);
                return;
            }

            if (opening)
            {
                panel.Opacity = 1;
                panel.RenderTransform = null;
                panel.RenderTransformOrigin = default;
            }

            onDone?.Invoke();
        }

        topLevel.RequestAnimationFrame(OnFrame);
    }

    private static double Eased(double progress) => progress * progress * (3 - 2 * progress);

    private static Look Looked(Control panel, Look resting)
    {
        if (panel.RenderTransform is not TransformGroup transforms)
            return resting;

        var travel = resting.Travel;
        var shrink = resting.Shrink;
        if (transforms.Children.OfType<TranslateTransform>().FirstOrDefault() is { } shifts)
            travel = new Vector(shifts.X, shifts.Y);
        if (transforms.Children.OfType<ScaleTransform>().FirstOrDefault() is { } grows)
            shrink = grows.ScaleX;

        return new Look(travel, shrink, panel.Opacity);
    }

    private readonly record struct Look(Vector Travel, double Shrink, double Opacity);
}
