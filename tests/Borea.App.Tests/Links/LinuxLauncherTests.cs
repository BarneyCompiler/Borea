using Borea.App.Links;

namespace Borea.App.Tests.Links;

public sealed class LinuxLauncherTests : IDisposable
{
    private const string Executable = "/opt/Borea/borea";

    private readonly string _dataHome = Path.Combine(Path.GetTempPath(), "BoreaLauncher_" + Guid.NewGuid());
    private string? _defaultHandler;

    public void Dispose()
    {
        if (Directory.Exists(_dataHome))
            Directory.Delete(_dataHome, recursive: true);
    }

    private LinuxLauncher Launcher() => new(_dataHome, () => [1, 2, 3]);

    private LinuxLinkRegistrar Registrar() => new(_dataHome, () => [1, 2, 3], arguments =>
    {
        if (arguments[0] == "default")
            _defaultHandler = arguments[1];
        return arguments[0] == "query" ? (_defaultHandler ?? string.Empty) + "\n" : string.Empty;
    });

    [Fact]
    public void Write_PutsTheAppInTheMenu_WithoutATerminal()
    {
        var launcher = Launcher();

        Assert.True(launcher.Write(Executable));

        var lines = File.ReadAllText(launcher.DesktopFilePath).Split('\n');
        Assert.Equal(Path.Combine(_dataHome, "applications", "borea-app.desktop"), launcher.DesktopFilePath);
        Assert.Equal("[Desktop Entry]", lines[0]);
        Assert.Contains("Type=Application", lines);
        Assert.Contains("Name=Borea", lines);
        Assert.Contains("Exec=\"/opt/Borea/borea\"", lines);
        Assert.Contains("TryExec=/opt/Borea/borea", lines);
        Assert.Contains("Terminal=false", lines);
        Assert.Contains("StartupWMClass=Borea.App", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("NoDisplay=", StringComparison.Ordinal) || line.StartsWith("MimeType=", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(LinuxDesktopEntry.IconPath(_dataHome)));
    }

    [Fact]
    public void Write_Again_WritesNothing()
    {
        var launcher = Launcher();
        launcher.Write(Executable);
        var written = File.GetLastWriteTimeUtc(launcher.DesktopFilePath);

        Assert.False(launcher.Write(Executable));

        Assert.Equal(written, File.GetLastWriteTimeUtc(launcher.DesktopFilePath));
    }

    [Fact]
    public void Write_OtherExecutable_RewritesTheEntry()
    {
        var launcher = Launcher();
        launcher.Write(Executable);

        Assert.True(launcher.Write("/home/me/100% Borea/borea"));

        var entry = File.ReadAllText(launcher.DesktopFilePath);
        Assert.Contains("Exec=\"/home/me/100%% Borea/borea\"\n", entry, StringComparison.Ordinal);
        Assert.Contains("TryExec=/home/me/100% Borea/borea\n", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_PathWithANewline_IsRefused_AndWritesNothing()
    {
        var launcher = Launcher();

        Assert.Throws<ArgumentException>(() => launcher.Write("/home/me/a\nb/borea"));

        Assert.False(Directory.Exists(_dataHome));
    }

    [Fact]
    public void HandlerEntry_KeepsWorking_NextToTheLauncher()
    {
        var launcher = Launcher();
        var registrar = Registrar();
        launcher.Write(Executable);

        Assert.True(registrar.Register(Executable));

        Assert.Equal(LinuxLinkRegistrar.DesktopFileName, _defaultHandler);
        var handler = File.ReadAllText(registrar.DesktopFilePath);
        Assert.Contains("Exec=\"/opt/Borea/borea\" %u\n", handler, StringComparison.Ordinal);
        Assert.Contains("MimeType=x-scheme-handler/borea;\n", handler, StringComparison.Ordinal);
        Assert.Contains("NoDisplay=true\n", handler, StringComparison.Ordinal);
        Assert.False(launcher.Write(Executable));

        Assert.True(registrar.Unregister(Executable));

        Assert.False(File.Exists(registrar.DesktopFilePath));
        Assert.True(File.Exists(launcher.DesktopFilePath));
        Assert.True(File.Exists(registrar.IconPath));
    }
}
