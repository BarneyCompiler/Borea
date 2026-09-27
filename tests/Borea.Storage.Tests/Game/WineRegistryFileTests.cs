using Borea.Storage.Game;

namespace Borea.Storage.Tests.Game;

public sealed class WineRegistryFileTests : IDisposable
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    internal static string Fixture => Path.Combine(AppContext.BaseDirectory, "Game", "Fixtures", "wine", "system.reg");

    [Fact]
    public void ReadSubKeys_ReadsTheDirectChildrenOfTheKeyOnly()
    {
        var keys = WineRegistryFile.ReadSubKeys(Fixture, UninstallKey);

        Assert.Equal(
            new[] { "Some Other App", "Unmapped StarMap", "{A6C53918-1F1B-4C7B-AF34-63C696A3E822}_is1", "{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1" },
            keys.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReadSubKeys_UndoesTheEscapesWineWrites()
    {
        var keys = WineRegistryFile.ReadSubKeys(Fixture, UninstallKey);

        var game = keys["{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1"];
        Assert.Equal(@"C:\Program Files\Kitten Space Agency\", game["InstallLocation"]);
        Assert.Equal("RocketWerkz \u00e9dition", game["Publisher"]);
        Assert.Equal("Line one\nline two\ttab", game["Comments"]);

        var loader = keys["{A6C53918-1F1B-4C7B-AF34-63C696A3E822}_is1"];
        Assert.Equal("C:\\Users\\J\u00fcrgen\\StarMap\\", loader["InstallLocation"]);
        Assert.Equal("Klaas \"KlaasWhite\" White", loader["Publisher"]);
    }

    [Fact]
    public void ReadSubKeys_LeavesOutValuesThatAreNotStrings()
    {
        var game = WineRegistryFile.ReadSubKeys(Fixture, UninstallKey)["{BA65F5AF-CD20-48A4-A957-410CB9660FF7}_is1"];

        Assert.Equal(new[] { "Comments", "DisplayName", "InstallLocation", "Publisher" }, game.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ReadSubKeys_ComparesKeyAndValueNamesWithoutCase()
    {
        var keys = WineRegistryFile.ReadSubKeys(Fixture, UninstallKey.ToLowerInvariant());

        Assert.Equal("Kitten Space Agency", keys["{ba65f5af-cd20-48a4-a957-410cb9660ff7}_IS1"]["displayname"]);
    }

    [Fact]
    public void ReadSubKeys_FileThatIsMissingOrNotAWineRegistry_HasNoKeys()
    {
        Directory.CreateDirectory(_tempRoot);
        var other = Path.Combine(_tempRoot, "system.reg");
        File.WriteAllText(other, string.Join('\n',
            "WINE REGISTRY Version 1",
            @"[Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Game] 1758900000",
            @"""InstallLocation""=""C:\\Game""",
            ""));

        Assert.Empty(WineRegistryFile.ReadSubKeys(other, UninstallKey));
        Assert.Empty(WineRegistryFile.ReadSubKeys(Path.Combine(_tempRoot, "missing.reg"), UninstallKey));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
