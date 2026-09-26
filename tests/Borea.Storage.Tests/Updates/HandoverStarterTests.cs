using Borea.Core.Updates;
using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class HandoverStarterTests
{
    private static readonly string Program = Path.Combine(Path.GetTempPath(), "Borea", "borea.exe");

    [Fact]
    public void StartInfo_FromTheApp_GivesTheNewBuildNoConsoleWindow()
    {
        var arguments = new SelfUpdateHandover(Program + ".old", 42, "A1B2C3").ToArguments();

        var startInfo = HandoverStarter.StartInfo(Program, arguments);

        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(Program, startInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(Program), startInfo.WorkingDirectory);
        Assert.Equal(arguments, startInfo.ArgumentList);
    }

    [Fact]
    public void StartInfo_FromACommand_KeepsTheTerminal()
    {
        var arguments = new SelfUpdateHandover(Program + ".old", 42, "A1B2C3", FromCommandLine: true).ToArguments();

        var startInfo = HandoverStarter.StartInfo(Program, arguments);

        Assert.False(startInfo.CreateNoWindow);
        Assert.Equal(arguments, startInfo.ArgumentList);
    }

    [Fact]
    public void StartInfo_NoHandover_KeepsTheConsole()
    {
        Assert.False(HandoverStarter.StartInfo(Program, ["--help"]).CreateNoWindow);
    }
}
