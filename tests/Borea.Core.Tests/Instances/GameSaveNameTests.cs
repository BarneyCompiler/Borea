using Borea.Core.Instances;

namespace Borea.Core.Tests.Instances;

public sealed class GameSaveNameTests
{
    [Theory]
    [InlineData("Orbit test 2", "Orbit test 2")]
    [InlineData("Mun-Lander_v2", "Mun-Lander_v2")]
    [InlineData("  Orbit   test  ", "Orbit test")]
    [InlineData("Orbit/test.1", "Orbittest1")]
    [InlineData("con", "con_")]
    [InlineData("COM1", "COM1_")]
    [InlineData("./!", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Sanitize_KeepsWhatTheGameKeeps(string? name, string expected)
    {
        Assert.Equal(expected, GameSaveName.Sanitize(name));
    }

    [Fact]
    public void Sanitize_KeepsLettersOutsideAscii()
    {
        var name = "Fl" + (char)0x00FC + "gel";

        Assert.Equal(name, GameSaveName.Sanitize(name));
        Assert.True(GameSaveName.IsValid(name));
    }

    [Fact]
    public void Sanitize_CutsAtTheMaximumLength()
    {
        Assert.Equal(new string('a', GameSaveName.MaxLength), GameSaveName.Sanitize(new string('a', GameSaveName.MaxLength + 10)));
    }

    [Theory]
    [InlineData("Orbit test 2", true)]
    [InlineData(" Orbit", false)]
    [InlineData("Orbit ", false)]
    [InlineData("Orbit  test", false)]
    [InlineData("Orbit.test", false)]
    [InlineData("NUL", false)]
    [InlineData("NUL_", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_TrueOnlyForANameTheGameKeepsAsItIs(string? name, bool expected)
    {
        Assert.Equal(expected, GameSaveName.IsValid(name));
    }

    [Fact]
    public void IsValid_FalseAboveTheMaximumLength()
    {
        Assert.True(GameSaveName.IsValid(new string('a', GameSaveName.MaxLength)));
        Assert.False(GameSaveName.IsValid(new string('a', GameSaveName.MaxLength + 1)));
    }
}
