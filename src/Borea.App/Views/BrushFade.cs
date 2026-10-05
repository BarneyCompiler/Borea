using System;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Borea.App.Views;

public class BrushFade : InterpolatingTransitionBase<IBrush?>
{
    protected override IBrush? Interpolate(double progress, IBrush? fromValue, IBrush? toValue)
    {
        if (progress >= 1)
            return toValue;
        if (fromValue is not ISolidColorBrush from || toValue is not ISolidColorBrush to)
            return progress < 0.5 ? fromValue : toValue;

        var was = from.Color;
        var toBe = to.Color;
        var wasAlpha = was.A / 255.0;
        var toAlpha = toBe.A / 255.0;
        var alpha = wasAlpha + (toAlpha - wasAlpha) * progress;
        if (alpha <= 0)
            return new ImmutableSolidColorBrush(Color.FromArgb(0, was.R, was.G, was.B));

        return new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(alpha * 255, 0, 255),
            (byte)Math.Clamp(Mix(was.R, wasAlpha, toBe.R, toAlpha, progress, alpha), 0, 255),
            (byte)Math.Clamp(Mix(was.G, wasAlpha, toBe.G, toAlpha, progress, alpha), 0, 255),
            (byte)Math.Clamp(Mix(was.B, wasAlpha, toBe.B, toAlpha, progress, alpha), 0, 255)));
    }

    private static double Mix(byte was, double wasAlpha, byte toBe, double toAlpha, double progress, double alpha)
    {
        var carried = was * wasAlpha;
        var mixed = carried + (toBe * toAlpha - carried) * progress;
        return mixed / alpha;
    }
}
