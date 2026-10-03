using Borea.Core.Licenses;

namespace Borea.Core.Tests.Licenses;

public sealed class LicenseFilterTests
{
    [Fact]
    public void Options_AreTheLicensesOfTheExpressionsEachOnce_TheMostNamedFirst()
    {
        var options = LicenseFilter.Options(["MIT", "MIT AND CC-BY-SA-4.0", "GPL-3.0-or-later", "mit OR Apache-2.0", "gpl-3.0-or-later", "MIT AND CC-BY-NC-SA-3.0 AND CC-BY-3.0"]);

        Assert.Equal(["MIT", "GPL-3.0-or-later", "Apache-2.0", "CC-BY-3.0", "CC-BY-NC-SA-3.0", "CC-BY-SA-4.0"], options);
    }

    [Fact]
    public void Options_ALicenseWithAnExceptionOrALicenseRef_IsOneOption()
    {
        var options = LicenseFilter.Options(["GPL-2.0-only WITH Classpath-exception-2.0", "LicenseRef-MyModLicense AND MIT", "GPL-2.0-only"]);

        Assert.Equal(["GPL-2.0-only", "GPL-2.0-only WITH Classpath-exception-2.0", "LicenseRef-MyModLicense", "MIT"], options);
    }

    [Fact]
    public void Options_ALicenseWrittenTwiceInOneExpression_CountsOnce()
    {
        Assert.Equal(["Zlib", "MIT"], LicenseFilter.Options(["MIT AND mit", "Zlib", "Zlib"]));
    }

    [Fact]
    public void Options_ParenthesesDeeperThanTheLimit_AreOneOptionWithoutACrash()
    {
        var text = new string('(', 10000) + "MIT" + new string(')', 10000);

        Assert.Equal([text], LicenseFilter.Options([text]));
    }

    [Fact]
    public void Options_TextThatDoesNotParse_IsOneOption()
    {
        Assert.Equal(["MIT", "Public domain, ask me"], LicenseFilter.Options(["Public domain, ask me", "MIT", "MIT", "  "]));
    }

    [Theory]
    [InlineData("MIT", true)]
    [InlineData("MIT OR Apache-2.0", true)]
    [InlineData("MIT AND CC-BY-SA-4.0", true)]
    [InlineData("GPL-3.0-only", false)]
    public void Matches_OnlyMitChosen_MatchesEveryExpressionThatNamesMit(string expression, bool matches)
    {
        Assert.Equal(matches, LicenseFilter.Matches(expression, ["MIT"]));
    }

    [Fact]
    public void Matches_ChosenIds_IgnoreCase()
    {
        Assert.True(LicenseFilter.Matches("MIT AND CC-BY-SA-4.0", ["cc-by-sa-4.0"]));
    }

    [Fact]
    public void Matches_ALicenseWithAnException_OnlyWhenThatOptionIsChosen()
    {
        const string expression = "GPL-2.0-only WITH Classpath-exception-2.0";

        Assert.False(LicenseFilter.Matches(expression, ["GPL-2.0-only"]));
        Assert.True(LicenseFilter.Matches(expression, ["GPL-2.0-only WITH Classpath-exception-2.0"]));
        Assert.True(LicenseFilter.Matches("gpl-2.0-only with Classpath-exception-2.0", ["GPL-2.0-only WITH Classpath-exception-2.0"]));
        Assert.False(LicenseFilter.Matches("GPL-2.0-only", ["GPL-2.0-only WITH Classpath-exception-2.0"]));
    }

    [Fact]
    public void Matches_TwoChosen_MatchesWhatNamesEitherOne()
    {
        string[] chosen = ["MIT", "GPL-3.0-or-later"];

        Assert.True(LicenseFilter.Matches("MIT", chosen));
        Assert.True(LicenseFilter.Matches("GPL-3.0-or-later", chosen));
        Assert.False(LicenseFilter.Matches("Apache-2.0", chosen));
    }

    [Theory]
    [InlineData("MIT AND CC-BY-SA-4.0")]
    [InlineData("Public domain, ask me")]
    [InlineData("")]
    public void Matches_NoneChosen_MatchesEveryExpression(string expression)
    {
        Assert.True(LicenseFilter.Matches(expression, []));
    }

    [Fact]
    public void Matches_TextThatDoesNotParse_WhenThatTextIsChosen()
    {
        Assert.True(LicenseFilter.Matches(" Public domain, ask me ", ["Public domain, ask me"]));
        Assert.False(LicenseFilter.Matches("Public domain, ask me", ["MIT"]));
    }
}
