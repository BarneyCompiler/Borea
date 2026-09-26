using Borea.App.Links;
using Borea.Core.Logging;

namespace Borea.App.Tests.Links;

public sealed class LinkHandlerTests
{
    [Theory]
    [InlineData(@"C:\Games\Borea\borea.exe", true, @"C:\Games\Borea\borea.exe")]
    [InlineData("/opt/Borea/borea", true, "/opt/Borea/borea")]
    // With forward slashes every OS finds the file name, as a real ProcessPath uses its own separator.
    [InlineData("C:/Program Files/dotnet/dotnet.exe", true, null)]
    [InlineData("/usr/share/dotnet/dotnet", true, null)]
    [InlineData(@"C:\src\Borea\bin\Release\net10.0\Borea.App.exe", false, null)]
    [InlineData(null, true, null)]
    public void PublishedExecutable_OnlyASingleFileBoreaProgram(string? processPath, bool isSingleFile, string? expected)
    {
        Assert.Equal(expected, LinkHandler.PublishedExecutable(processPath, isSingleFile));
    }

    [Fact]
    public void ForThisProcess_TestHost_RegistersNothing()
    {
        Assert.Null(LinkHandler.ForThisProcess());
    }

    [Fact]
    public void Apply_On_RegistersAndLogsAChange()
    {
        var registrar = new FakeRegistrar { Changes = true };
        var log = new RecordingLog();

        new LinkHandler(registrar, "/opt/Borea/borea").Apply(enabled: true, log);
        registrar.Changes = false;
        new LinkHandler(registrar, "/opt/Borea/borea").Apply(enabled: true, log);

        Assert.Equal(["register /opt/Borea/borea", "register /opt/Borea/borea"], registrar.Calls);
        Assert.Contains("Registered /opt/Borea/borea", Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Off_Unregisters()
    {
        var registrar = new FakeRegistrar { Changes = true };
        var log = new RecordingLog();

        new LinkHandler(registrar, "/opt/Borea/borea").Apply(enabled: false, log);

        Assert.Equal(["unregister /opt/Borea/borea"], registrar.Calls);
        Assert.Contains("Removed", Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Failure_IsLoggedAndSwallowed()
    {
        var log = new RecordingLog();

        new LinkHandler(new FakeRegistrar { Failure = new UnauthorizedAccessException("denied") }, "/opt/Borea/borea").Apply(enabled: true, log);

        Assert.Contains("could not register", Assert.Single(log.Lines), StringComparison.Ordinal);
        Assert.IsType<UnauthorizedAccessException>(Assert.Single(log.Exceptions));
    }

    [Fact]
    public void AddLauncher_WritesTheEntryForThisExecutable_AndLogsAChange()
    {
        var written = new List<string>();
        var changes = true;
        var log = new RecordingLog();
        var handler = new LinkHandler(new FakeRegistrar(), "/opt/Borea/borea", path =>
        {
            written.Add(path);
            return changes;
        });

        handler.AddLauncher(log);
        changes = false;
        handler.AddLauncher(log);

        Assert.Equal(["/opt/Borea/borea", "/opt/Borea/borea"], written);
        Assert.Contains("Added /opt/Borea/borea to the application menu", Assert.Single(log.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void AddLauncher_Failure_IsLoggedAndSwallowed()
    {
        var log = new RecordingLog();

        new LinkHandler(new FakeRegistrar(), "/opt/Borea/borea", _ => throw new IOException("disk full")).AddLauncher(log);

        Assert.Contains("could not add itself to the application menu", Assert.Single(log.Lines), StringComparison.Ordinal);
        Assert.IsType<IOException>(Assert.Single(log.Exceptions));
    }

    [Fact]
    public void AddLauncher_WithoutALauncher_DoesNothing()
    {
        var log = new RecordingLog();

        new LinkHandler(new FakeRegistrar(), @"C:\Games\Borea\borea.exe").AddLauncher(log);

        Assert.Empty(log.Lines);
    }
}

internal sealed class FakeRegistrar : ILinkRegistrar
{
    public List<string> Calls { get; } = [];

    public bool Changes { get; set; }

    public Exception? Failure { get; set; }

    public bool Register(string executablePath) => Record("register " + executablePath);

    public bool Unregister(string executablePath) => Record("unregister " + executablePath);

    private bool Record(string call)
    {
        Calls.Add(call);
        return Failure is null ? Changes : throw Failure;
    }
}

internal sealed class RecordingLog : IBoreaLog
{
    public List<string> Lines { get; } = [];

    public List<Exception> Exceptions { get; } = [];

    public string CurrentFilePath => string.Empty;

    public void Write(string message) => Lines.Add(message);

    public void Write(string message, Exception exception)
    {
        Lines.Add(message);
        Exceptions.Add(exception);
    }

    public IReadOnlyList<string> ReadRecentLines(int maxLines) => Lines;
}
