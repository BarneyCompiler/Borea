using Borea.Core.Index;
using Borea.Core.Listings;

namespace Borea.Core.Tests.Listings;

public sealed class ListingPackOwnerTests
{
    // packs/beiks-flight-planning-essentials-pack/owner.json of content-index, as the steward merged it.
    private const string Merged = "{\n  \"github_login\": \"renancamm\",\n  \"github_id\": 31055336\n}\n";

    [Fact]
    public void ToJson_WritesTheLayoutOfTheOwnerRecordsInContentIndex()
    {
        Assert.Equal(Merged, new ListingPackOwner("renancamm", 31055336).ToJson());
        Assert.Equal("packs/my-pack/owner.json", ListingPackOwner.PathOf("my-pack"));
    }

    [Fact]
    public void Parse_OwnerRecord_GivesTheLoginAndTheAccountId()
    {
        Assert.Equal(new ListingPackOwner("renancamm", 31055336), ListingPackOwner.Parse(Merged));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "github_login": "renancamm" }""")]
    [InlineData("""{ "github_login": "renancamm", "github_id": "31055336" }""")]
    [InlineData("""{ "github_login": "renancamm", "github_id": 0 }""")]
    [InlineData("""{ "github_login": "", "github_id": 31055336 }""")]
    public void Parse_NoOwnerRecord_GivesNull(string? text)
    {
        Assert.Null(ListingPackOwner.Parse(text));
    }

    [Theory]
    [InlineData("My-Pack", null, "My-Pack")]
    [InlineData(null, "MY-PACK", "MY-PACK")]
    [InlineData("other", "other-pack", null)]
    public void HolderOf_ComparesIdsWithoutLetterCase(string? listing, string? pack, string? expected)
    {
        var snapshot = new ContentIndexSnapshot(
            1,
            listing is null ? [] : [new ContentIndexListing(listing, null, [], null)],
            pack is null ? [] : [new ContentIndexPack(pack, [], null)],
            null,
            []);

        Assert.Equal(expected, ListingPackOwner.HolderOf(snapshot, "my-pack"));
    }
}
