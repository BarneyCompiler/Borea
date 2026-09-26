using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class GameCrashReportTests
{
    /// <summary>The end of the tail Brutal.Monitor kept when RealAtmospheres 1.0.3 crashed the game on a system with a decimal comma.</summary>
    private static readonly string[] RealAtmospheresCrash =
    [
        "[Brutal.Monitor] the most recent 37 log lines - full history in KittenSpaceAgency.260926-122536.43512.log",
        "System.InvalidOperationException: There is an error in XML document (4364, 39).",
        " ---> System.FormatException: The input string '0,389' was not in a correct format.",
        "   at System.Number.ThrowFormatException[TChar](ReadOnlySpan`1 value)",
        "   at KSA.XmlLoader.Deserialize[T](String filePath)",
        "loaded system 'Test'",
        "Unhandled exception System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.",
        " ---> System.NullReferenceException: Object reference not set to an instance of an object.",
        "   at KSA.ConfigOnStartPopup.SetVehicles()",
        "   at KSA.Program.Main(String[] inArgs)",
        "   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)",
        "   --- End of inner exception stack trace ---",
        "   at StarMap.GameSurveyer.RunGame()",
        "   at StarMap.Program.<Main>(String[] args).",
        "Last chance dump: Failed Write dump failed - HRESULT: 0x80004005..",
    ];

    [Fact]
    public void ExceptionText_ExceptionWrappedByTheLoader_IsTheInnermostException()
    {
        Assert.Equal("System.NullReferenceException: Object reference not set to an instance of an object.", GameCrashReport.ExceptionText(RealAtmospheresCrash));
    }

    [Fact]
    public void ExceptionText_RunLogLineAndSeveralInnerExceptions_IsTheInnermostException()
    {
        string[] log =
        [
            "12:25:43.983 ERROR Unhandled exception System.AggregateException: One or more errors occurred.",
            " ---> System.InvalidOperationException: The vehicle is gone.",
            " ---> System.Collections.Generic.KeyNotFoundException: The given key 'Hunter' was not present in the dictionary.",
            "   at KSA.Vehicle.Find(String id)",
        ];

        Assert.Equal("System.Collections.Generic.KeyNotFoundException: The given key 'Hunter' was not present in the dictionary.", GameCrashReport.ExceptionText(log));
    }

    [Fact]
    public void ExceptionText_OneException_IsItsFirstLine()
    {
        Assert.Equal(
            "System.DivideByZeroException: Attempted to divide by zero.",
            GameCrashReport.ExceptionText(["Unhandled exception System.DivideByZeroException: Attempted to divide by zero.", "   at KSA.Program.Main(String[] inArgs)."]));
    }

    [Fact]
    public void ExceptionText_TailWithoutAnUnhandledException_IsNull()
    {
        Assert.Null(GameCrashReport.ExceptionText(RealAtmospheresCrash[..6]));
        Assert.Null(GameCrashReport.ExceptionText([]));
    }

    [Fact]
    public void AssemblyNames_StackOfTheGameAndTheRuntime_NamesNone()
    {
        Assert.Empty(GameCrashReport.AssemblyNames(RealAtmospheresCrash));
    }

    [Fact]
    public void AssemblyNames_UnhandledException_NamesItsAssembliesThenItsFrameNamespacesInnermostFirst()
    {
        string[] log =
        [
            "   at EarlierMod.Settings.Read()",
            "Unhandled exception System.TypeLoadException: Could not load type 'KSA.Stage' from assembly 'OldTools, Version=1.0.0.0'.",
            "   at KSArmory.RoundFollowable.DrawAxes()",
            "   at KSA.Vehicle.OnFrame()",
            "   at ModMenu.Hooks.Frame()",
            "   at ksarmory.Other.Call()",
        ];

        Assert.Equal(["OldTools", "KSArmory", "ModMenu"], GameCrashReport.AssemblyNames(log));
    }
}
