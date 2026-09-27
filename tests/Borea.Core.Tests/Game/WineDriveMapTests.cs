using Borea.Core.Game;

namespace Borea.Core.Tests.Game;

public sealed class WineDriveMapTests
{
    private const string Prefix = "/Applications/Kitten Space Agency.app/Contents/SharedSupport/prefix";

    private static WineDriveMap DefaultDrives(StringComparison comparison = StringComparison.OrdinalIgnoreCase) => new(
        new Dictionary<char, string> { ['c'] = Prefix + "/drive_c", ['z'] = "/" },
        comparison);

    [Theory]
    [InlineData(Prefix + "/drive_c/Program Files/Kitten Space Agency", @"C:\Program Files\Kitten Space Agency")]
    [InlineData(Prefix + "/drive_c", @"C:\")]
    [InlineData(
        "/Users/a/Library/Application Support/Borea/Instances/0f8e2c1a-5b7d-4e3f-9a61-2d4c8b0e7f13",
        @"Z:\Users\a\Library\Application Support\Borea\Instances\0f8e2c1a-5b7d-4e3f-9a61-2d4c8b0e7f13")]
    [InlineData("/", @"Z:\")]
    public void DefaultDrives_MapBothWays(string host, string windows)
    {
        var drives = DefaultDrives();

        Assert.Equal(windows, drives.ToWindows(host));
        Assert.Equal(host, drives.ToHost(windows));
    }

    [Fact]
    public void ToWindows_TakesTheDeepestDriveThatHoldsThePath()
    {
        var drives = new WineDriveMap(
            new Dictionary<char, string> { ['d'] = "/Volumes/Games", ['z'] = "/" },
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(@"D:\KSA", drives.ToWindows("/Volumes/Games/KSA"));
        Assert.Equal(@"Z:\Volumes\Other", drives.ToWindows("/Volumes/Other"));
    }

    [Fact]
    public void ToWindows_DeeperDriveWithAHigherLetter_TakesTheDeeperDrive()
    {
        var drives = new WineDriveMap(
            new Dictionary<char, string> { ['d'] = "/", ['g'] = "/Volumes/Games" },
            StringComparison.Ordinal);

        Assert.Equal(@"G:\KSA", drives.ToWindows("/Volumes/Games/KSA"));
    }

    [Fact]
    public void ToWindows_TwoDrivesOnOneFolder_TakesTheLowerLetter()
    {
        var drives = new WineDriveMap(
            new Dictionary<char, string> { ['y'] = "/Volumes/Games", ['e'] = "/Volumes/Games/", ['z'] = "/" },
            StringComparison.Ordinal);

        Assert.Equal(@"E:\KSA", drives.ToWindows("/Volumes/Games/KSA"));
    }

    [Theory]
    [InlineData(Prefix + @"/drive_c/Games/K\SA")]
    [InlineData(Prefix + "/drive_c/Games/KSA: Deluxe")]
    [InlineData(Prefix + "/drive_c/Games/KSA?")]
    public void ToWindows_PartWithACharacterWindowsDoesNotAllow_IsNull(string host)
    {
        Assert.Null(DefaultDrives().ToWindows(host));
    }

    [Fact]
    public void ToWindows_NoDriveHoldsThePath_IsNull()
    {
        var drives = new WineDriveMap(new Dictionary<char, string> { ['c'] = Prefix + "/drive_c" }, StringComparison.Ordinal);

        Assert.Null(drives.ToWindows("/Users/a/Library/Application Support/Borea"));
        Assert.Null(drives.ToWindows("relative/path"));
    }

    [Fact]
    public void ToWindows_ComparesTheHostPathByTheHostRule()
    {
        var upper = "/APPLICATIONS/Kitten Space Agency.app/Contents/SharedSupport/prefix/drive_c/KSA";

        Assert.Equal(@"C:\KSA", DefaultDrives(StringComparison.OrdinalIgnoreCase).ToWindows(upper));
        Assert.Equal(@"Z:\APPLICATIONS\Kitten Space Agency.app\Contents\SharedSupport\prefix\drive_c\KSA", DefaultDrives(StringComparison.Ordinal).ToWindows(upper));
    }

    [Theory]
    [InlineData(@"C:\a\..\..\b")]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\?\C:\Program Files\KSA")]
    [InlineData(@"Program Files\KSA")]
    [InlineData(@"C:Program Files\KSA")]
    [InlineData(@"D:\KSA")]
    [InlineData(@"C:\KSA\file.txt:stream")]
    [InlineData("")]
    public void ToHost_PathThatNamesNoFolderOnADrive_IsNull(string windows)
    {
        Assert.Null(DefaultDrives().ToHost(windows));
    }

    [Fact]
    public void ToHost_AcceptsForwardSlashesLowerCaseLettersAndATrailingSeparator()
    {
        Assert.Equal(Prefix + "/drive_c/Program Files/KSA", DefaultDrives().ToHost(@"c:/Program Files\.\KSA\"));
    }

    [Fact]
    public void WindowsHost_MapsBothWays()
    {
        var drives = new WineDriveMap(new Dictionary<char, string> { ['C'] = @"D:\Temp\prefix\drive_c" }, StringComparison.OrdinalIgnoreCase, '\\');

        Assert.Equal(@"C:\Program Files\KSA", drives.ToWindows(@"D:\Temp\prefix\drive_c\Program Files\KSA"));
        Assert.Equal(@"D:\Temp\prefix\drive_c\Program Files\KSA", drives.ToHost(@"C:\Program Files\KSA"));
        Assert.Equal(@"D:\Temp\prefix\drive_c", drives.Drives['c']);
    }

    [Theory]
    [InlineData(@"C:\KSA", true)]
    [InlineData("c:/KSA", true)]
    [InlineData("C:", false)]
    [InlineData("/Users/a", false)]
    [InlineData(@"\\server\share", false)]
    [InlineData(null, false)]
    public void IsDrivePath_NeedsALetterAColonAndASeparator(string? path, bool expected)
    {
        Assert.Equal(expected, WineDriveMap.IsDrivePath(path));
    }

    [Fact]
    public void Constructor_RootThatIsNotAbsolute_Throws()
    {
        Assert.Throws<ArgumentException>(() => new WineDriveMap(new Dictionary<char, string> { ['c'] = "drive_c" }, StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => new WineDriveMap(new Dictionary<char, string> { ['1'] = "/" }, StringComparison.Ordinal));
    }
}
