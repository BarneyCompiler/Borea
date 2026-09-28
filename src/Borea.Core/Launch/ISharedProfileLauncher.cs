namespace Borea.Core.Launch;

/// <summary>
/// Starts the game itself, without a mod loader and without an instance. The
/// game takes no instance path on its own, so it uses the shared profile under
/// My Games/Kitten Space Agency. This is a separate operation from
/// <see cref="ILauncher"/>, which starts an instance and never falls back to
/// the shared profile.
/// </summary>
public interface ISharedProfileLauncher
{
    /// <summary>
    /// Starts the game's executable from the game directory with
    /// <paramref name="arguments"/>. Borea writes nothing to the shared profile.
    /// For a Windows build in a Wine wrapper on macOS, it starts the launcher
    /// of the wrapper with the executable and refuses arguments, because the
    /// wrapper passes none. It keeps a record of that process until it exits,
    /// because the wrapper runs one game at a time, and of no other process.
    /// </summary>
    SharedProfileLaunchResult Launch(IReadOnlyList<string>? arguments = null);
}
