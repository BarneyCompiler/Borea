using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Storage.Launch;

internal static class ModBlame
{
    /// <summary>
    /// The installed mod behind the first of the assemblies: a mod whose id is
    /// the assembly's name, or whose folder holds that assembly.
    /// </summary>
    public static InstalledMod? Find(Instance instance, string modsFolder, IEnumerable<string> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var byId = instance.Mods.FirstOrDefault(mod => string.Equals(mod.ModId, assembly, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;

            foreach (var mod in instance.Mods)
            {
                var folder = Path.Combine(modsFolder, mod.ModId);
                try
                {
                    if (Directory.Exists(folder) && Directory.EnumerateFiles(folder, assembly + ".dll", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true }).Any())
                        return mod;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // a folder that cannot be searched, such as a link to a removed folder, blames nothing
                }
            }
        }

        return null;
    }
}
