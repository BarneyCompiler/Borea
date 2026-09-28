using Borea.Core.Game;
using Borea.Storage.Game;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Game;

public sealed class GamePlatformTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;

    public GamePlatformTests()
    {
        _paths = new TestGamePathProvider(_tempRoot);
    }

    private void PlaceGame(params string[] files)
    {
        var game = Directory.CreateDirectory(_paths.GetGameDirectoryPath()!).FullName;
        foreach (var file in files)
            File.WriteAllBytes(Path.Combine(game, file), []);
    }

    [Theory]
    [InlineData(OsPlatform.MacOs)]
    [InlineData(OsPlatform.Linux)]
    public void Current_WindowsBuildOnLinuxOrMacOs_IsWindows(OsPlatform host)
    {
        WineFixtures.Prefix(_tempRoot);
        PlaceGame("KSA.exe");

        Assert.Equal(OsPlatform.Windows, new GamePlatform(_paths, host).Current);
    }

    [Theory]
    [InlineData(OsPlatform.Linux, new[] { "KSA", "KSA.exe" })]
    [InlineData(OsPlatform.Linux, new[] { "KSA" })]
    [InlineData(OsPlatform.MacOs, new string[0])]
    [InlineData(OsPlatform.Windows, new[] { "KSA.exe" })]
    public void Current_BuildTheHostRuns_IsTheHost(OsPlatform host, string[] files)
    {
        PlaceGame(files);

        Assert.Equal(host, new GamePlatform(_paths, host).Current);
    }

    [Fact]
    public void Current_NoGameDirectory_IsTheHost()
    {
        var paths = new TestGamePathProvider(_tempRoot, hasGameDirectory: false);

        Assert.Equal(OsPlatform.MacOs, new GamePlatform(paths, OsPlatform.MacOs).Current);
    }

    [Fact]
    public void Current_GameFolderChanges_CountsAtOnce()
    {
        var platform = new GamePlatform(_paths, OsPlatform.MacOs);
        PlaceGame();
        Assert.Equal(OsPlatform.MacOs, platform.Current);

        PlaceGame("KSA.exe");

        Assert.Equal(OsPlatform.Windows, platform.Current);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
