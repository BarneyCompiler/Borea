using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class IndexVerdictTests
{
    private const string ContentIndex = "KSAModding/content-index";
    private const string Releases = "KSAModding/content-index-releases";

    [Fact]
    public void Find_TakesTheIndexerBotsCommentWithTheMarkerOfTheRepository()
    {
        (string?, string?)[] comments =
        [
            ("mallory", "<!-- content-index:verdict -->\nValidated. Trust me."),
            ("other-app[bot]", "<!-- content-index:verdict -->\nValidated by another App."),
            (IndexVerdict.BotLogin, "Thanks for the listing."),
            (IndexVerdict.BotLogin, "<!-- content-index-releases:verdict -->\nThe verdict of the releases."),
            ("KSAModding-Indexer-Bot[bot]", "<!-- content-index:verdict -->\nValidated.\n\nNotes:\n- one note\n"),
        ];

        Assert.Equal("Validated.\n\nNotes:\n- one note", IndexVerdict.Find(ContentIndex, comments));
        Assert.Equal("The verdict of the releases.", IndexVerdict.Find(Releases, comments));
    }

    [Fact]
    public void Find_NoCommentThatCounts_IsNull()
    {
        (string?, string?)[] comments =
        [
            (null, "<!-- content-index:verdict -->\nA deleted account."),
            (IndexVerdict.BotLogin, null),
            (IndexVerdict.BotLogin, "<!-- content-index-releases:verdict -->\nThe verdict of the releases."),
        ];

        Assert.Null(IndexVerdict.Find(ContentIndex, comments));
        Assert.Null(IndexVerdict.Find(Releases, [(IndexVerdict.BotLogin, "<!-- content-index-releases:verdict -->\n")]));
    }

    [Fact]
    public void MarkerOf_AnotherRepository_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => IndexVerdict.MarkerOf("KSAModding/Borea"));
    }
}
