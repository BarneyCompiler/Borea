using Borea.Core.Instances;
using Borea.Storage.Instances;

namespace Borea.Cli.Tests;

public sealed class GameSaveCommandTests : IDisposable
{
    private readonly CliHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task RenameSave_WritesTheNameAndRenamesTheFolder()
    {
        var instanceId = await CreateInstanceAsync();
        var orbit = AddItem(_host.Paths.GetInstanceSavesFolder(instanceId), "Orbit", "Orbit");

        var result = await _host.RunAsync("instance", "rename-save", "Alpha", "Orbit", "Mun landing");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Renamed the save 'Orbit' of 'Alpha' to 'Mun landing'.", result.Output);
        Assert.False(Directory.Exists(orbit));
        var entry = Assert.Single(await new FileGameSaveStore(_host.Paths).ListAsync(instanceId, GameSaveKind.Save));
        Assert.Equal("Mun landing", entry.Name);
        Assert.Equal("Mun landing", entry.FolderName);
    }

    [Fact]
    public async Task RenameVehicle_ByItsFolderName_RenamesTheVehicle()
    {
        var instanceId = await CreateInstanceAsync();
        AddItem(_host.Paths.GetInstanceVehiclesFolder(instanceId), "rocket-1", "Rocket");

        var result = await _host.RunAsync("instance", "rename-vehicle", "Alpha", "rocket-1", "Rocket 2");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Renamed the vehicle 'Rocket' of 'Alpha' to 'Rocket 2'.", result.Output);
        Assert.Equal("Rocket 2", Assert.Single(await new FileGameSaveStore(_host.Paths).ListAsync(instanceId, GameSaveKind.Vehicle)).FolderName);
    }

    [Fact]
    public async Task RenameSave_FolderNameOfOneAndNameOfAnother_TakesTheFolder()
    {
        var instanceId = await CreateInstanceAsync();
        var saves = _host.Paths.GetInstanceSavesFolder(instanceId);
        AddItem(saves, "Orbit", "Moon");
        var other = AddItem(saves, "orbit-2", "Orbit");

        var result = await _host.RunAsync("instance", "rename-save", "Alpha", "Orbit", "Mun");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Renamed the save 'Moon' of 'Alpha' to 'Mun'.", result.Output);
        Assert.True(Directory.Exists(other));
    }

    [Theory]
    [InlineData("Mun/landing", "Try 'Munlanding'.")]
    [InlineData("...", "at least one letter or digit")]
    [InlineData("moon", "already has a save named 'moon'")]
    public async Task RenameSave_NameTheGameRefusesOrThatIsTaken_FailsAndLeavesTheFolder(string name, string message)
    {
        var instanceId = await CreateInstanceAsync();
        var saves = _host.Paths.GetInstanceSavesFolder(instanceId);
        var orbit = AddItem(saves, "Orbit", "Orbit");
        AddItem(saves, "Moon", "Moon");

        var result = await _host.RunAsync("instance", "rename-save", "Alpha", "Orbit", name);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error);
        Assert.Equal("name = \"Orbit\"\n", File.ReadAllText(Path.Combine(orbit, "meta.toml")));
    }

    [Fact]
    public async Task RenameSave_GameRunning_FailsAndLeavesTheFolder()
    {
        var instanceId = await CreateInstanceAsync();
        var orbit = AddItem(_host.Paths.GetInstanceSavesFolder(instanceId), "Orbit", "Orbit");
        _host.GameRunning = true;

        var result = await _host.RunAsync("instance", "rename-save", "Alpha", "Orbit", "Mun");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Close the game before you rename a save.", result.Error);
        Assert.Equal("name = \"Orbit\"\n", File.ReadAllText(Path.Combine(orbit, "meta.toml")));
    }

    [Fact]
    public async Task RenameVehicle_UnknownVehicle_Fails()
    {
        var instanceId = await CreateInstanceAsync();
        AddItem(_host.Paths.GetInstanceSavesFolder(instanceId), "Orbit", "Orbit");

        var result = await _host.RunAsync("instance", "rename-vehicle", "Alpha", "Orbit", "Mun");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("'Alpha' has no vehicle 'Orbit'.", result.Error);
    }

    [Fact]
    public async Task RenameSave_TwoSavesOfThatName_AsksForTheFolder()
    {
        var instanceId = await CreateInstanceAsync();
        var saves = _host.Paths.GetInstanceSavesFolder(instanceId);
        AddItem(saves, "orbit-1", "Orbit");
        AddItem(saves, "orbit-2", "Orbit");

        var result = await _host.RunAsync("instance", "rename-save", "Alpha", "Orbit", "Mun");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Name it by its folder: orbit-1, orbit-2.", result.Error);
    }

    private async Task<Guid> CreateInstanceAsync()
    {
        var created = await _host.RunAsync("instance", "create", "Alpha", "--json");
        return Guid.Parse(created.Json.GetProperty("id").GetString()!);
    }

    private static string AddItem(string kindFolder, string folderName, string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(kindFolder, folderName)).FullName;
        File.WriteAllText(Path.Combine(folder, "meta.toml"), $"name = \"{name}\"\n");
        File.WriteAllBytes(Path.Combine(folder, "universe.xml"), new byte[10]);
        return folder;
    }
}
