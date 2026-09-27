using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Borea.App.Views;
using Borea.Core.Index;

namespace Borea.App.Tests.Views;

[Collection(HeadlessCollection.Name)]
public sealed class BitmapShelfTests
{
    [Fact]
    public async Task Take_HandsTheBitmapOverOnce()
    {
        var taken = await HeadlessApp.Session.Dispatch(() =>
        {
            var shelf = new BitmapShelf(1024 * 1024);
            var icon = Icon('a');
            var bitmap = Bitmap(8);
            shelf.Put(icon, bitmap);

            return (First: ReferenceEquals(bitmap, shelf.Take(icon)), SecondEmpty: shelf.Take(icon) is null, shelf.Count, shelf.Size);
        }, CancellationToken.None);

        Assert.Equal((true, true, 0, 0L), taken);
    }

    [Fact]
    public async Task Put_EqualRecordBuiltAgain_KeepsTheWiderBitmap()
    {
        var kept = await HeadlessApp.Session.Dispatch(() =>
        {
            var shelf = new BitmapShelf(1024 * 1024);
            var wide = Bitmap(16);
            shelf.Put(Icon('a'), wide);
            shelf.Put(Icon('A'), Bitmap(8));

            return (shelf.Count, Wider: ReferenceEquals(wide, shelf.Take(Icon('a'))));
        }, CancellationToken.None);

        Assert.Equal((1, true), kept);
    }

    [Theory]
    [InlineData(512, 256, 1000)]
    [InlineData(256, 512, 1000)]
    [InlineData(256, 256, 999)]
    public async Task Take_RecordWithTheSameDigestAndOtherFacts_FindsNoBitmap(int width, int height, long sizeBytes)
    {
        var found = await HeadlessApp.Session.Dispatch(() =>
        {
            var shelf = new BitmapShelf(1024 * 1024);
            shelf.Put(Icon('a'), Bitmap(8));

            return (Other: shelf.Take(Icon('a', width, height, sizeBytes)) is not null, Own: shelf.Take(Icon('a')) is not null);
        }, CancellationToken.None);

        Assert.Equal((false, true), found);
    }

    [Fact]
    public async Task Put_PastTheCapacity_DropsTheLeastRecentBitmaps()
    {
        var left = await HeadlessApp.Session.Dispatch(() =>
        {
            // each entry is 8 x 8 pixels of 4 bytes, so two fit
            var shelf = new BitmapShelf(2 * 256);
            var oldest = Icon('1');
            var middle = Icon('2');
            var newest = Icon('3');
            shelf.Put(oldest, Bitmap(8));
            shelf.Put(middle, Bitmap(8));
            shelf.Put(newest, Bitmap(8));

            return (shelf.Count, shelf.Size, Oldest: shelf.Take(oldest) is null, Middle: shelf.Take(middle) is not null, Newest: shelf.Take(newest) is not null);
        }, CancellationToken.None);

        Assert.Equal((2, 2 * 256L, true, true, true), left);
    }

    private static IconImage Icon(char digit, int width = 256, int height = 256, long sizeBytes = 1000) =>
        new("https://images.example/icon.png", new string(digit, 64), width, height, sizeBytes);

    private static WriteableBitmap Bitmap(int side) => new(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Rgba8888);
}
