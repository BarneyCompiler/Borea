using System.CommandLine;
using Borea.Core.Instances;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance rename-save</c> and <c>rename-vehicle</c>: the saves and
/// vehicles in the folder of an instance.
/// </summary>
internal static class GameSaveCommands
{
    public static Command BuildRenameSave(Func<CancellationToken, Task<CliServices>> services) => BuildRename(services, GameSaveKind.Save);

    public static Command BuildRenameVehicle(Func<CancellationToken, Task<CliServices>> services) => BuildRename(services, GameSaveKind.Vehicle);

    private static Command BuildRename(Func<CancellationToken, Task<CliServices>> services, GameSaveKind kind)
    {
        var noun = BackupCommands.KindName(kind)!;
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var item = ArgumentRules.Text(noun, $"The name the game shows for the {noun}, or the name of its folder.");
        var newName = ArgumentRules.Text("new-name", $"The new name: letters, digits, single spaces, - and _, at most {GameSaveName.MaxLength} characters.");
        var rename = new Command($"rename-{noun}", $"Give a {noun} a new name. Borea writes the name into its meta.toml and renames its folder to match. Close the game first.");
        rename.Arguments.Add(instance);
        rename.Arguments.Add(item);
        rename.Arguments.Add(newName);

        rename.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var entry = await FindAsync(cli, target, kind, parseResult.GetRequiredValue(item), ct).ConfigureAwait(false);

            // the game reads the list once at start and writes its next save under the name it read
            if (cli.IsGameProcessRunning())
                throw new InvalidOperationException($"Close the game before you rename a {noun}.");

            var name = parseResult.GetRequiredValue(newName);
            switch (await cli.GameSaves.RenameAsync(target.InstanceId, entry, name, ct).ConfigureAwait(false))
            {
                case GameSaveRenameOutcome.InvalidName:
                    var sanitized = GameSaveName.Sanitize(name);
                    throw new InvalidOperationException(sanitized.Length == 0
                        ? $"The game does not accept '{name}' as a name. A name needs at least one letter or digit."
                        : $"The game does not accept '{name}' as a name. It allows letters, digits, single spaces, - and _, at most {GameSaveName.MaxLength} characters. Try '{sanitized}'.");
                case GameSaveRenameOutcome.NameTaken:
                    throw new InvalidOperationException($"'{target.Name}' already has a {noun} named '{name}', and the game would show only one of the two.");
            }

            output.WriteLine($"Renamed the {noun} '{entry.Name}' of '{target.Name}' to '{name}'.");
            return ExitCodes.Done;
        }));

        return rename;
    }

    /// <summary>A folder name first, because it is unique, then the name the game shows, then either in any letter case.</summary>
    private static async Task<GameSaveEntry> FindAsync(CliServices cli, Instance instance, GameSaveKind kind, string text, CancellationToken cancellationToken)
    {
        var entries = await cli.GameSaves.ListAsync(instance.InstanceId, kind, cancellationToken).ConfigureAwait(false);
        if (entries.FirstOrDefault(entry => string.Equals(entry.FolderName, text, StringComparison.Ordinal)) is { } byFolder)
            return byFolder;

        var named = entries.Where(entry => string.Equals(entry.Name, text, StringComparison.Ordinal)).ToList();
        if (named.Count == 0)
        {
            named = entries.Where(entry => string.Equals(entry.Name, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FolderName, text, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var noun = BackupCommands.KindName(kind);
        return named.Count switch
        {
            1 => named[0],
            0 => throw new InvalidOperationException($"'{instance.Name}' has no {noun} '{text}'."),
            _ => throw new InvalidOperationException($"More than one {noun} of '{instance.Name}' is named '{text}'. Name it by its folder: {string.Join(", ", named.Select(entry => entry.FolderName).Order(StringComparer.OrdinalIgnoreCase))}."),
        };
    }
}
