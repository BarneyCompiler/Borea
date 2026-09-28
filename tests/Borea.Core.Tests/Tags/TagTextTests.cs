using System.Globalization;
using Borea.Core.Tags;

namespace Borea.Core.Tests.Tags;

public sealed class TagTextTests
{
    [Theory]
    [InlineData("Physics", "physics")]
    [InlineData("Space Station", "space-station")]
    [InlineData("  RCS ", "rcs")]
    [InlineData("-Big_ _Engines-", "big-engines")]
    [InlineData("x2-engine", "x2-engine")]
    [InlineData("User\tInterface", "user-interface")]
    public void Normalize_TypedEntry_GivesTheStoredForm(string entry, string tag)
    {
        Assert.Equal(tag, TagText.Normalize(entry));
        Assert.True(TagText.IsValid(TagText.Normalize(entry)));
    }

    [Theory]
    [InlineData("C++")]
    [InlineData("Space - Station")]
    [InlineData("---")]
    [InlineData("Rock&Roll")]
    public void Normalize_EntryThatCannotBeATag_IsNotValid(string entry)
    {
        Assert.False(TagText.IsValid(TagText.Normalize(entry)));
    }

    [Fact]
    public void Normalize_TurkishCulture_LowersTheCapitalIWithoutADotlessI()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Equal("identity", TagText.Normalize("IDENTITY"));
            Assert.Equal("Identity", TagText.Display("identity"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("weapons", "Weapons")]
    [InlineData("space-station", "Space Station")]
    [InlineData("x2-engine", "X2 Engine")]
    [InlineData("2d", "2d")]
    public void Display_StoredTag_ShowsWordsWithACapitalFirstLetter(string tag, string shown)
    {
        Assert.Equal(shown, TagText.Display(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Physics")]
    [InlineData("space_station")]
    [InlineData("space--station")]
    [InlineData("physics\n")]
    public void IsValid_TextNotInTheStoredForm_IsFalse(string? tag)
    {
        Assert.False(TagText.IsValid(tag));
    }
}
