using Borea.Core.Mods;

namespace Borea.Core.ModLoaders;

/// <summary>
/// Reads the game path named by a loader's authored configuration contract.
/// </summary>
public interface ILoaderConfigurationReader
{
    /// <summary>
    /// Returns the configured game path without changing the loader's file.
    /// Null means that no path is declared, the file is absent, or the key is
    /// absent. A Windows path is mapped to the host through the Wine prefix of
    /// <paramref name="gameDirectory"/>, or else of the loader directory, and
    /// stays as it is when neither is in a prefix.
    /// </summary>
    /// <param name="gameDirectory">The game directory Borea manages, or null when it knows none.</param>
    Task<string?> ReadConfiguredGamePathAsync(
        ModMetadata loader,
        string loaderDirectory,
        string? gameDirectory = null,
        CancellationToken cancellationToken = default);
}
