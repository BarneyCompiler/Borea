using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class SearchFieldTests
{
    // The layout box of a line is centered already, so the test measures the drawn letters, which are what a reader sees.
    [Theory]
    [InlineData("")]
    [InlineData("KSA")]
    public async Task SearchField_DrawsItsTextAndItsIconInTheMiddle(string typed)
    {
        var (field, text, icon) = await HeadlessApp.RunAsync(async () =>
        {
            var magnifier = new Avalonia.Controls.Shapes.Path { Classes = { "icon", "size-16" }, Margin = new Thickness(10, 0, 0, 0) };
            // a placeholder without descenders, so its ink runs from the cap height to the baseline
            var box = new TextBox { Classes = { "field", "search" }, PlaceholderText = "Search mods", Text = typed, Width = 280, InnerLeftContent = magnifier };
            var window = new Window { Width = 400, Height = 120, Content = new StackPanel { Margin = new Thickness(20), Children = { box } } };
            magnifier.Data = (Geometry)window.FindResource("Icon.Search")!;
            window.Show();
            window.UpdateLayout();
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!;
            using var buffer = frame.Lock();
            var top = box.TranslatePoint(default, window)!.Value;
            var iconLeft = magnifier.TranslatePoint(default, window)!.Value.X;
            var rows = ((int)top.Y + 3, (int)(top.Y + box.Bounds.Height) - 3);
            var background = Pixel(buffer, (int)(top.X + box.Bounds.Width - 20), (int)(top.Y + box.Bounds.Height / 2));
            (double Field, double Text, double Icon) result = (
                top.Y + box.Bounds.Height / 2,
                InkMiddle(buffer, background, (int)(iconLeft + magnifier.Bounds.Width + 4), (int)(top.X + box.Bounds.Width - 30), rows),
                InkMiddle(buffer, background, (int)iconLeft, (int)(iconLeft + magnifier.Bounds.Width), rows));
            window.Close();
            return result;
        });

        Assert.InRange(text - field, -1, 1);
        Assert.InRange(icon - field, -1, 1);
    }

    private static double InkMiddle(ILockedFramebuffer buffer, Color background, int left, int right, (int Top, int Bottom) rows)
    {
        var inked = Enumerable.Range(rows.Top, rows.Bottom - rows.Top + 1)
            .Where(y => Enumerable.Range(left, right - left).Any(x => Distance(Pixel(buffer, x, y), background) > 60))
            .ToList();
        Assert.NotEmpty(inked);
        return (inked[0] + inked[^1] + 1) / 2.0;
    }

    private static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static Color Pixel(ILockedFramebuffer buffer, int x, int y)
    {
        var offset = y * buffer.RowBytes + x * 4;
        return Color.FromRgb(Marshal.ReadByte(buffer.Address, offset), Marshal.ReadByte(buffer.Address, offset + 1), Marshal.ReadByte(buffer.Address, offset + 2));
    }
}
