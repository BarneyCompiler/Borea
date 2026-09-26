using System.Diagnostics;
using System.Text.Json;
using Borea.Core.Launch;
using Borea.Storage.Launch;

// "host <directory>" starts a child through ProcessStarter and prints its id.
// "child <arguments>" records what it received, writes more than a pipe holds to both streams,
// then creates a "wrote" file and runs until a stop file appears.
// "<arguments> restart" restarts itself the way the game's Restart button does, with its own
// executable, no arguments and no streams of its own, then exits with 0. StarMap's own restart
// only adds --restarted and its folder as the working directory, so in both cases only the
// variable still names the instance.
// Without arguments it is that restart. It records what it received in the folder that
// BOREA_PROBE_INSTANCE names, and after a "go" file appears there it writes more than a pipe holds,
// creates "restarted-wrote" and runs until a stop file appears.
return args switch
{
    ["host", var directory] => Host(directory),
    ["child", ..] => Child(args[1..]),
    [.., "restart"] => Restart(),
    [] => Restarted(),
    _ => 2,
};

static int Host(string directory)
{
    var plan = new LaunchPlan(
        Environment.ProcessPath!,
        new[] { Path.Combine(AppContext.BaseDirectory, "LaunchProbeFixture.dll"), "child" },
        directory,
        new Dictionary<string, string>());

    using var process = new ProcessStarter().Start(plan);
    Console.WriteLine($"Process id: {process.Id}");
    return 0;
}

static int Child(string[] arguments)
{
    var record = new ChildRecord(arguments, Environment.CurrentDirectory, Environment.GetEnvironmentVariable("BOREA_PROBE"), Environment.ProcessId);
    WriteAtomically("record.json", JsonSerializer.Serialize(record));

    WriteMoreThanAPipeHolds();
    File.WriteAllText("wrote", string.Empty);

    WaitForStop(Environment.CurrentDirectory);
    return 0;
}

static int Restart()
{
    // Run through the dotnet host, its own executable is the host, which needs the assembly.
    var startInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LaunchProbeFixture.dll"));

    using var restarted = Process.Start(startInfo);
    return 0;
}

static int Restarted()
{
    var instance = Environment.GetEnvironmentVariable("BOREA_PROBE_INSTANCE");
    if (string.IsNullOrEmpty(instance))
        return 3;

    var record = new ChildRecord(Array.Empty<string>(), Environment.CurrentDirectory, instance, Environment.ProcessId);
    WriteAtomically(Path.Combine(instance, "restarted.json"), JsonSerializer.Serialize(record));
    Console.WriteLine($"Restarted as {Environment.ProcessId}");

    if (!WaitFor(Path.Combine(instance, "go")))
        return 4;

    WriteMoreThanAPipeHolds();
    File.WriteAllText(Path.Combine(instance, "restarted-wrote"), string.Empty);

    WaitForStop(instance);
    return 0;
}

static void WriteMoreThanAPipeHolds()
{
    var line = new string('x', 100);
    for (var i = 0; i < 2000; i++)
    {
        Console.Out.WriteLine(line);
        Console.Error.WriteLine(line);
    }

    Console.Out.Flush();
    Console.Error.Flush();
}

static void WaitForStop(string directory) => WaitFor(Path.Combine(directory, "stop"));

// A guard for a test run that ended without writing the file, as long as the tests' Patience.
static bool WaitFor(string path)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);
    while (!File.Exists(path) && DateTime.UtcNow < deadline)
        Thread.Sleep(50);

    return File.Exists(path);
}

// A reader never sees a half written file.
static void WriteAtomically(string path, string contents)
{
    var temporary = path + ".tmp";
    File.WriteAllText(temporary, contents);
    File.Move(temporary, path, overwrite: true);
}

internal sealed record ChildRecord(string[] Arguments, string WorkingDirectory, string? Variable, int ProcessId);
