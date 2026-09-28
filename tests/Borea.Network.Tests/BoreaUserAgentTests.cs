namespace Borea.Network.Tests;

public sealed class BoreaUserAgentTests
{
    [Theory]
    [InlineData("0.3.0", "Borea/0.3.0 (+https://github.com/KSAModding/Borea)")]
    [InlineData("0.3.0-beta.1", "Borea/0.3.0-beta.1 (+https://github.com/KSAModding/Borea)")]
    public void Apply_ValidVersion_NamesTheVersionAndTheContactLink(string version, string expected)
    {
        using var client = new HttpClient();

        BoreaUserAgent.Apply(client, version);

        Assert.Equal(expected, client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1 0")]
    [InlineData("1.0/2")]
    public void Apply_VersionIsNotAToken_DropsTheVersionAndKeepsTheContactLink(string version)
    {
        using var client = new HttpClient();

        BoreaUserAgent.Apply(client, version);

        Assert.Equal("Borea (+https://github.com/KSAModding/Borea)", client.DefaultRequestHeaders.UserAgent.ToString());
    }
}
