using Borea.Core.Licenses;

namespace Borea.Core.Tests.Licenses;

public sealed class SpdxExpressionTests
{
    [Fact]
    public void Parse_NamesEverySpellingOfALicenseOnceInTheOrderOfTheExpression()
    {
        var expression = SpdxExpression.Parse("(MIT OR Apache-2.0) AND mit AND MIT AND GPL-2.0-only with Classpath-exception-2.0")!;

        Assert.Equal(
            [new SpdxLicense("MIT", null), new SpdxLicense("Apache-2.0", null), new SpdxLicense("mit", null), new SpdxLicense("GPL-2.0-only", "Classpath-exception-2.0")],
            expression.Licenses);
        Assert.Equal("GPL-2.0-only WITH Classpath-exception-2.0", expression.Licenses[3].Name);
    }

    [Fact]
    public void Parse_ParenthesesAsDeepAsTheLimit_Parse()
    {
        var text = new string('(', SpdxExpression.MaxDepth) + "MIT AND Apache-2.0" + new string(')', SpdxExpression.MaxDepth);

        Assert.Equal([new SpdxLicense("MIT", null), new SpdxLicense("Apache-2.0", null)], SpdxExpression.Parse(text)!.Licenses);
    }

    [Fact]
    public void Parse_MoreGroupsSideBySideThanTheLimit_Parse()
    {
        var text = string.Join(" AND ", Enumerable.Repeat("(MIT OR Apache-2.0)", SpdxExpression.MaxDepth + 1));

        Assert.NotNull(SpdxExpression.Parse(text));
    }

    [Theory]
    [InlineData(SpdxExpression.MaxDepth + 1)]
    [InlineData(10000)]
    public void Parse_ParenthesesDeeperThanTheLimit_IsNullWithoutACrash(int depth)
    {
        Assert.Null(SpdxExpression.Parse(new string('(', depth) + "MIT" + new string(')', depth)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MIT AND")]
    [InlineData("MIT OR OR Apache-2.0")]
    [InlineData("(MIT OR Apache-2.0")]
    [InlineData("(MIT) WITH Classpath-exception-2.0")]
    [InlineData("MIT WITH")]
    [InlineData("MIT, Apache-2.0")]
    [InlineData("MIT Apache-2.0")]
    public void Parse_TextOutsideTheGrammar_IsNull(string text)
    {
        Assert.Null(SpdxExpression.Parse(text));
    }
}
