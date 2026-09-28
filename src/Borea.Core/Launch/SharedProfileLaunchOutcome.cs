namespace Borea.Core.Launch;

public enum SharedProfileLaunchOutcome
{
    /// <summary>The game process is running.</summary>
    Started = 0,

    /// <summary>Borea does not know the name of the game's executable on this platform.</summary>
    UnknownExecutable = 1,

    /// <summary>No game directory is configured.</summary>
    NoGameDirectory = 2,

    /// <summary>The executable is not in the game directory.</summary>
    ExecutableMissing = 3,

    /// <summary>The operating system refused to start the process.</summary>
    StartFailed = 4,

    /// <summary>The game folder holds the Windows build, which Borea cannot start on this system.</summary>
    WindowsBuild = 5,

    /// <summary>The launch has arguments, and the Wine wrapper that starts the game passes none.</summary>
    WrapperArguments = 6,

    /// <summary>The game's executable is on no drive of the Wine prefix.</summary>
    PathOutsidePrefix = 7,

    /// <summary>A game that Borea started through the same Wine wrapper is still running.</summary>
    WrapperBusy = 8,
}
