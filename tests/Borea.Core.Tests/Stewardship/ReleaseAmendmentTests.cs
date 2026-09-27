using Borea.Core.Stewardship;

namespace Borea.Core.Tests.Stewardship;

public sealed class ReleaseAmendmentTests
{
    private static readonly string Releases = Path.Combine(AppContext.BaseDirectory, "Stewardship", "Fixtures", "release-files", "AdvancedFlightComputer");

    private static readonly IReadOnlyList<string> Stamped = [.. Directory.EnumerateFiles(Releases, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>()];

    private static readonly IReadOnlyList<string> GameVersions = ["2026.8.19.5261", "2026.9.4.5400", "2026.9.7.5402", "2026.9.22.5482"];

    [Fact]
    public void Select_UpTo_TakesEveryReleaseAtOrBelowBySemVerPrecedence_NewestFirst()
    {
        var selected = ReleaseAmendment.Select(Stamped, ReleaseSelection.UpTo("0.8.2-nightly.20260925"));

        Assert.Equal(["0.8.2-nightly.20260925", "0.8.1", "0.8.0", "0.7.5", "0.7.4", "0.7.3", "0.7.2"], selected);
    }

    [Fact]
    public void Select_SeveralVersions_KeepsTheirOrderAndReadsThemAsAnAuthorWritesThem()
    {
        Assert.Equal(["0.8.1", "0.7.2", "0.8.0"], ReleaseAmendment.Select(Stamped, ReleaseSelection.Of("0.8.1", " v0.7.2", "0.8")));
    }

    [Fact]
    public void Select_All_TakesEveryRelease_NewestFirst()
    {
        var selected = ReleaseAmendment.Select(Stamped, ReleaseSelection.All);

        Assert.Equal(["0.8.2-nightly.20260926", "0.8.2-nightly.20260925", "0.8.1", "0.8.0", "0.7.5", "0.7.4", "0.7.3", "0.7.2"], selected);
    }

    [Theory]
    [InlineData("0.9.0", ReleaseAmendmentRefusal.UnknownRelease)]
    [InlineData("latest", ReleaseAmendmentRefusal.InvalidChange)]
    public void Select_AVersionWithoutARelease_IsRefused(string version, ReleaseAmendmentRefusal refusal)
    {
        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Select(Stamped, ReleaseSelection.Of("0.8.1", version)));

        Assert.Equal(refusal, refused.Refusal);
    }

    [Fact]
    public void Select_UpToBelowEveryRelease_IsRefused()
    {
        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Select(Stamped, ReleaseSelection.UpTo("0.7.2-beta")));

        Assert.Equal(ReleaseAmendmentRefusal.UnknownRelease, refused.Refusal);
    }

    [Fact]
    public void Select_AFileNameThatIsNoVersion_IsRefused()
    {
        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Select([.. Stamped, "notes"], ReleaseSelection.All));

        Assert.Equal(ReleaseAmendmentRefusal.NotStamperFile, refused.Refusal);
    }

    [Fact]
    public void Apply_AStewardsYankOfSeveralReleases_AddsOnlyTheYankToEachFile()
    {
        const string Reason = "The archive deletes <saves> & 'mods'.";
        var amendment = ReleaseAmendment.Create(new ReleaseChange { Yank = true, YankReason = Reason }, GameVersions, DateTimeOffset.UtcNow);

        var amended = ReleaseAmendment.Select(Stamped, ReleaseSelection.UpTo("0.7.5"))
            .Select(version => (Text: File.ReadAllText(Path.Combine(Releases, version + ".json")), Path: ReleaseAmendment.PathOf("AdvancedFlightComputer", version)))
            .Select(file => (file.Text, Amended: amendment.Apply(file.Path, file.Text, ReleaseAmender.Steward)))
            .ToList();

        Assert.Equal(4, amended.Count);
        Assert.All(amended, file =>
        {
            Assert.EndsWith("\n}\n", file.Text, StringComparison.Ordinal);
            Assert.Equal(file.Text[..^3] + ",\n  \"yanked\": true,\n  \"yanked_reason\": \"The archive deletes <saves> & 'mods'.\"\n}\n", file.Amended?.Text);
            Assert.False(file.Amended!.Widens);
        });
    }

    [Fact]
    public void Create_AGameMaxMonth_ResolvesToItsLastBuildOnlyOnceTheMonthIsOverInUtc()
    {
        var change = new ReleaseChange { GameMax = "2026.9" };

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Create(change, GameVersions, new DateTimeOffset(2026, 10, 1, 0, 30, 0, TimeSpan.FromHours(1))));
        var amendment = ReleaseAmendment.Create(change, GameVersions, new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.FromHours(-2)));

        Assert.Equal(ReleaseAmendmentRefusal.MonthNotOver, refused.Refusal);
        var text = File.ReadAllText(Path.Combine(Releases, "0.8.1.json"));
        var amended = amendment.Apply(ReleaseAmendment.PathOf("AdvancedFlightComputer", "0.8.1"), text, ReleaseAmender.Steward);
        Assert.Contains("\n  \"game_min_revision\": 5482,\n  \"game_max\": \"2026.9.22.5482\",\n  \"game_max_revision\": 5482,\n", amended?.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AGameMinMonth_ResolvesToItsFirstBuild()
    {
        var amendment = ReleaseAmendment.Create(new ReleaseChange { GameMin = "2026.9" }, GameVersions, DateTimeOffset.UtcNow);
        var text = File.ReadAllText(Path.Combine(Releases, "0.8.1.json"));

        var amended = amendment.Apply(ReleaseAmendment.PathOf("AdvancedFlightComputer", "0.8.1"), text, ReleaseAmender.Owner);

        Assert.Contains("\n  \"game_min\": \"2026.9.4.5400\",\n  \"game_min_revision\": 5400,\n", amended?.Text, StringComparison.Ordinal);
        Assert.True(amended!.Widens);
    }

    /// <summary>
    /// Digits that an input method can type instead of ASCII digits. 0xFF10 is the fullwidth zero and 0x0660 the Arabic-Indic zero,
    /// and each block holds the ten digits in order.
    /// </summary>
    [Theory]
    [InlineData("2026.9", 0xFF10, false)]
    [InlineData("2026.8", 0xFF10, true)]
    [InlineData("2026.9", 0x0660, false)]
    [InlineData("2026.9.22.5482", 0xFF10, true)]
    public void Create_AGameBoundWithDigitsThatAreNotAscii_IsRefusedAsAnInvalidChange(string bound, int zero, bool asMax)
    {
        var typed = string.Concat(bound.Select(character => char.IsAsciiDigit(character) ? (char)(zero + character - '0') : character));
        var change = asMax ? new ReleaseChange { GameMax = typed } : new ReleaseChange { GameMin = typed };

        var refused = Assert.Throws<ReleaseAmendmentRefusedException>(() => ReleaseAmendment.Create(change, GameVersions, new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero)));

        Assert.Equal(ReleaseAmendmentRefusal.InvalidChange, refused.Refusal);
        Assert.Contains("is neither a game version nor a month", refused.Message, StringComparison.Ordinal);
    }
}
