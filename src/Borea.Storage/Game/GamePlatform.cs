using Borea.Core.Game;
using Borea.Core.Paths;
using Borea.Storage.Launch;

namespace Borea.Storage.Game;

/// <summary>
/// IGamePlatform over the configured game folder. It reads the folder on each
/// call, so a changed game folder counts at once.
/// </summary>
public sealed class GamePlatform : IGamePlatform
{
    private readonly IGamePathProvider _pathProvider;
    private readonly OsPlatform _host;

    public GamePlatform(IGamePathProvider pathProvider)
        : this(pathProvider, SharedProfileLauncher.CurrentPlatform() ?? OsPlatform.MacOs)
    {
    }

    /// <param name="host">The system Borea runs on.</param>
    public GamePlatform(IGamePathProvider pathProvider, OsPlatform host)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _host = host;
    }

    public OsPlatform Current => Of(_host, _pathProvider.GetGameDirectoryPath()) ?? _host;

    /// <summary>The platform of the game build in <paramref name="gameDirectory"/> on <paramref name="host"/>.</summary>
    internal static OsPlatform? Of(OsPlatform? host, string? gameDirectory) =>
        WindowsBuildOnHost.IsIn(host, gameDirectory) ? OsPlatform.Windows : host;
}
