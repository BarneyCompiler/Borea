using Borea.Core.Index;
using Borea.Core.Listings;
using Borea.Core.ModPacks;
using Borea.Core.Mods;

namespace Borea.Core.Tests.Listings;

public sealed class ListingPackVersionsTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.2")]
    [InlineData("1.1.0-beta.2", "1.1.0")]
    [InlineData("2.0.0", "2.0.1")]
    public void Raise_GivesTheNextPatchOrTheReleaseOfAPreRelease(string version, string raised)
    {
        Assert.Equal(raised, ListingPackVersions.Raise(ModVersion.Parse(version)).ToString());
    }

    [Theory]
    [InlineData("1.0.1+b.2", "1.0.1+b.2")]
    [InlineData(null, "1.0.1")]
    public void FileVersion_IsTheVersionAsTheDocumentWritesIt_OrTheParsedOneWhenThatIsUnknown(string? text, string file)
    {
        var metadata = new ModPackMetadata(1, "armory-pack", "test", "Armory Pack", ["Maxi"], "Armory Pack abstract.", "MIT",
            new Dictionary<string, string> { ["forums"] = "https://forums.example.com/armory-pack" }, "2026.8.19.5261", ModVersion.Parse("1.0.1+b.2"),
            DateTimeOffset.UnixEpoch, [new ModPackEntry("KSArmory", ModVersion.Parse("0.8.44"))]);

        Assert.Equal(file, ListingPackVersions.FileVersion(new ContentIndexPackVersion(metadata, null) { VersionText = text }));
    }

    [Fact]
    public void Raise_PatchThatCannotGrow_GivesNull()
    {
        Assert.Null(ListingPackVersions.Raise(new ModVersion(1, 0, int.MaxValue)));
    }

    [Fact]
    public async Task FreeAsync_ProposedVersionThatExists_IsRaisedUntilOneIsFree()
    {
        var asked = new List<string>();
        var taken = new HashSet<string> { "1.0.2", "1.0.3" };

        var free = await ListingPackVersions.FreeAsync(ModVersion.Parse("1.0.2"), (version, _) =>
        {
            asked.Add(version.ToString());
            return Task.FromResult(taken.Contains(version.ToString()));
        });

        Assert.Equal("1.0.4", free.ToString());
        Assert.Equal(["1.0.2", "1.0.3", "1.0.4"], asked);
    }

    [Fact]
    public async Task FreeAsync_EveryTriedVersionTaken_GivesNull()
    {
        var asked = 0;

        var free = await ListingPackVersions.FreeAsync(ModVersion.Parse("1.0.0"), (_, _) =>
        {
            asked++;
            return Task.FromResult(true);
        });

        Assert.Null(free);
        Assert.Equal(ListingPackVersions.MaxTries, asked);
    }

    [Theory]
    [InlineData("Removed at the request of the author of Compendium.", "Compendium", true)]
    [InlineData("compendium asked to leave", "Compendium", true)]
    [InlineData("CompendiumPlus asked to leave", "Compendium", false)]
    [InlineData("Compendium.Extra asked to leave", "Compendium", false)]
    [InlineData("My-Compendium asked to leave", "Compendium", false)]
    [InlineData("The archive was replaced.", "Compendium", false)]
    [InlineData(null, "Compendium", false)]
    public void Names_FindsTheIdOnlyAsAWholeWord(string? reason, string id, bool names)
    {
        Assert.Equal(names, ListingPackVersions.Names(reason, id));
    }
}
