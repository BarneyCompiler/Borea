using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class TagChipRowTests
{
    [Fact]
    public async Task UpToSixTagsThatFit_AllShow_WithoutTheCountChip()
    {
        var seen = await ShowAsync(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], width: 2000);

        Assert.Equal(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], seen.Shown);
        Assert.Null(seen.More);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task MoreThanSixTags_ShowSix_AndTheCountChipNamesTheRest()
    {
        var seen = await ShowAsync(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools", "Visual", "Library"], width: 2000);

        Assert.Equal(["Parts", "Gameplay", "Audio", "Weapons", "Physics", "Tools"], seen.Shown);
        Assert.Equal("+2", seen.More);
        Assert.Equal("Visual, Library", seen.MoreTip);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task TagsThatDoNotFit_GiveWayToTheCountChip_AndNoChipIsCut()
    {
        string[] tags = ["Parts", "Gameplay", "User Interface", "Audio", "Weapons", "Physics"];
        var seen = await ShowAsync(tags, width: 220);

        Assert.InRange(seen.Shown.Count, 1, 5);
        Assert.Equal(tags[..seen.Shown.Count], seen.Shown);
        Assert.Equal($"+{tags.Length - seen.Shown.Count}", seen.More);
        Assert.Equal(string.Join(", ", tags[seen.Shown.Count..]), seen.MoreTip);
        Assert.True(seen.InsideRow);
    }

    [Fact]
    public async Task NoTags_TakeNoHeight()
    {
        var seen = await ShowAsync([], width: 400);

        Assert.Empty(seen.Shown);
        Assert.Null(seen.More);
        Assert.Equal(0, seen.Height);
    }

    /// <summary>Shows the row alone in a window of the width and reads the chips that have room, and whether each lies whole inside the row.</summary>
    private static Task<(List<string> Shown, string? More, string? MoreTip, bool InsideRow, double Height)> ShowAsync(string[] tags, double width) =>
        HeadlessApp.RunAsync(() =>
        {
            var row = new TagChipRow { Tags = tags, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = width, Height = 200, Content = row };
            window.Show();
            window.UpdateLayout();

            var withRoom = row.Children.OfType<Border>().Where(chip => chip.Bounds.Width > 0).ToList();
            var shown = withRoom.Where(chip => chip != row.MoreChip).Select(chip => ((TextBlock)chip.Child!).Text!).ToList();
            var more = withRoom.Contains(row.MoreChip) ? ((TextBlock)row.MoreChip.Child!).Text : null;
            var tip = more is null ? null : ToolTip.GetTip(row.MoreChip) as string;
            var inside = withRoom.All(chip => chip.Bounds.Right <= row.Bounds.Width + 0.5 && chip.Bounds.X >= 0);
            var result = (shown, more, tip, inside, row.Bounds.Height);
            window.Close();
            return Task.FromResult(result);
        });
}
