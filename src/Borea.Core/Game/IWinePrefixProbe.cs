namespace Borea.Core.Game;

/// <summary>
/// Recognizes a folder inside a Wine prefix from the files Wine creates in
/// every prefix. It only reads.
/// </summary>
public interface IWinePrefixProbe
{
    /// <summary>
    /// The Wine prefix that holds <paramref name="directory"/>, or null when
    /// none does or the host is not Linux or macOS.
    /// </summary>
    WineInstall? Find(string directory);

    /// <summary>
    /// The path a program in the prefix of <paramref name="install"/> uses for
    /// <paramref name="hostPath"/>, with the links of the host path resolved
    /// first, or null when no drive of the prefix holds it.
    /// </summary>
    string? ToWindowsPath(WineInstall install, string hostPath);
}
