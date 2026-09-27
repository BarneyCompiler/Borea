using Borea.App.ViewModels;
using Borea.Core.Index;

namespace Borea.App.Tests.ViewModels;

public sealed class IdleIconBytesTests
{
    [Fact]
    public void Put_PastTheCapacity_GivesBackTheBytesOfTheLeastRecentIcons()
    {
        var owner = new MainViewModel { IdleIcons = new IdleIconBytes(2 * 100) };
        var (oldest, middle, newest) = (Loaded(owner, 1), Loaded(owner, 2), Loaded(owner, 3));

        Assert.Equal((2, 200L), (owner.IdleIcons.Count, owner.IdleIcons.Size));
        Assert.Equal((false, true, true), (oldest.IsLoaded, middle.IsLoaded, newest.IsLoaded));
    }

    [Fact]
    public void Put_AnIconAgain_MakesItTheMostRecent()
    {
        var owner = new MainViewModel { IdleIcons = new IdleIconBytes(2 * 100) };
        var (oldest, middle) = (Loaded(owner, 1), Loaded(owner, 2));

        owner.IdleIcons.Put(oldest, 100);
        var newest = Loaded(owner, 3);

        Assert.Equal((true, false, true), (oldest.IsLoaded, middle.IsLoaded, newest.IsLoaded));
    }

    [Fact]
    public void AddView_KeepsTheBytesOfAnIconThatAViewShows()
    {
        var owner = new MainViewModel { IdleIcons = new IdleIconBytes(100) };
        var shown = Loaded(owner, 1);
        shown.AddView();

        var idle = Loaded(owner, 2);
        Loaded(owner, 3);

        Assert.Equal((true, false, 1, 100L), (shown.IsLoaded, idle.IsLoaded, owner.IdleIcons.Count, owner.IdleIcons.Size));
    }

    [Fact]
    public void RemoveView_OfTheLastView_MakesTheIconIdle()
    {
        var owner = new MainViewModel { IdleIcons = new IdleIconBytes(0) };
        var icon = new ListingImage(owner, Icon(1));
        icon.AddView();
        icon.AddView();
        icon.Bytes = new byte[100];

        icon.RemoveView();
        var withOneView = icon.IsLoaded;
        icon.RemoveView();

        Assert.Equal((true, false), (withOneView, icon.IsLoaded));
    }

    [Fact]
    public void DescriptionImage_ThatNoViewShows_KeepsItsBytes()
    {
        var owner = new MainViewModel { IdleIcons = new IdleIconBytes(0) };
        var image = new ListingImage(owner, new DescriptionImage("shot", "https://images.example/shot.png", new string('A', 64), 1600, 900, 400_000)) { Bytes = new byte[100] };

        Assert.True(image.IsLoaded);
        Assert.Equal(0, owner.IdleIcons.Count);
    }

    private static ListingImage Loaded(MainViewModel owner, int number) => new(owner, Icon(number)) { Bytes = new byte[100] };

    private static IconImage Icon(int number) => new($"https://images.example/{number}.png", new string('A', 64), 256, 256, 100);
}
