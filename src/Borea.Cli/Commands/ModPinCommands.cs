using System.CommandLine;
using Borea.Core.Mods;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea pin</c> and <c>borea unpin</c>: whether updates may change the version of a mod.
/// The pin is part of the instance record, so the App and the CLI keep the same pins.
/// </summary>
internal static class ModPinCommands
{
    private const string ModIdDescription = "The id of a mod that Borea installed.";

    public static Command BuildPin(Func<CancellationToken, Task<CliServices>> services)
        => Build(services, "pin", "Keep a mod at its version in an instance. Updates leave it alone, and a plan that needs another version of it stops.", pinned: true);

    public static Command BuildUnpin(Func<CancellationToken, Task<CliServices>> services)
        => Build(services, "unpin", "Let updates change the version of a pinned mod again.", pinned: false);

    private static Command Build(Func<CancellationToken, Task<CliServices>> services, string name, string description, bool pinned)
    {
        var modId = ArgumentRules.Text("mod-id", ModIdDescription);
        var instance = ArgumentRules.Instance();
        var command = new Command(name, description);
        command.Arguments.Add(modId);
        command.Options.Add(instance);

        command.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var id = parseResult.GetRequiredValue(modId);
            var target = await InstanceLookup.ResolveTargetAsync(cli.Instances, parseResult.GetValue(instance)).ConfigureAwait(false);
            if (!target.Mods.Any(mod => ModIds.Equals(mod.ModId, id)))
                throw new InvalidOperationException($"Mod '{id}' is not installed in '{target.Name}'.");

            var (changed, mod) = await cli.Instances.UpdateAsync(
                target.InstanceId,
                current => (current.SetPinned(id, pinned), current.Mods.First(item => ModIds.Equals(item.ModId, id))),
                ct).ConfigureAwait(false);

            output.WriteLine((pinned, changed) switch
            {
                (true, true) => $"Pinned {mod.ModId} at {mod.Version} in '{target.Name}'. Updates leave it at this version.",
                (true, false) => $"{mod.ModId} is pinned at {mod.Version} in '{target.Name}' already.",
                (false, true) => $"Unpinned {mod.ModId} in '{target.Name}'. Updates can change its version again.",
                (false, false) => $"{mod.ModId} is not pinned in '{target.Name}'.",
            });
            return ExitCodes.Done;
        }));

        return command;
    }
}
