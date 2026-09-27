using System.Text;
using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class ReleaseJsonTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "release-files");

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static TheoryData<string> ReleaseFiles => [.. Directory.EnumerateFiles(Root, "*.json", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(Root, path)).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheFixture_HoldsEveryReleaseFile()
    {
        Assert.Equal(73, ReleaseFiles.Count);
    }

    [Theory]
    [MemberData(nameof(ReleaseFiles))]
    public void ReadAndWrite_KeepsEveryReleaseFileByteForByte(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, file));

        var written = ReleaseJson.Write(ReleaseJson.Parse(Strict.GetString(bytes)));

        Assert.Equal(bytes, Strict.GetBytes(written));
    }

    /// <summary>The expected text is what json.dumps(json.loads(source), indent=2, ensure_ascii=False) + "\n" gives in Python 3.13.</summary>
    [Fact]
    public void Write_EscapesAndWritesNumbersAsPython()
    {
        const string Source = "{\"text\": \"\\u0001\\u001f\\\"\\\\\\b\\f\\n\\r\\t\\u007f\\u2028<>&'+\\u00e9\\ud83d\\ude00\", \"empty\": [], \"nothing\": {}, \"numbers\": [1, -0, 1.0, 1e16, 1E15, 1.5E-7, 0.0001, 100.5e2, -0.0, 123456789012345678901234567890, 0.1, 5e-324, 1.7976931348623157e308, 2.5e-5], \"none\": null, \"yes\": true, \"no\": false, \"nested\": {\"list\": [{\"a\": 1}]}}";
        const string Expected = "{\n  \"text\": \"\\u0001\\u001f\\\"\\\\\\b\\f\\n\\r\\t\U0000007F\U00002028<>&'+\U000000E9\U0001F600\",\n  \"empty\": [],\n  \"nothing\": {},\n  \"numbers\": [\n    1,\n    0,\n    1.0,\n    1e+16,\n    1000000000000000.0,\n    1.5e-07,\n    0.0001,\n    10050.0,\n    -0.0,\n    123456789012345678901234567890,\n    0.1,\n    5e-324,\n    1.7976931348623157e+308,\n    2.5e-05\n  ],\n  \"none\": null,\n  \"yes\": true,\n  \"no\": false,\n  \"nested\": {\n    \"list\": [\n      {\n        \"a\": 1\n      }\n    ]\n  }\n}\n";

        Assert.Equal(Expected, ReleaseJson.Write(ReleaseJson.Parse(Source)));
    }

    [Theory]
    [InlineData("{\"a\": 1, \"a\": 2}")]
    [InlineData("{\"a\": NaN}")]
    [InlineData("{\"a\": 1,}")]
    public void Parse_TextThatNoStamperWrites_IsRefused(string text)
    {
        Assert.Throws<FormatException>(() => ReleaseJson.Parse(text));
    }
}
