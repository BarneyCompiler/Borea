using System.Text.RegularExpressions;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>
/// The versions of a listed pack, for its next version. A pack version never changes, so the next version is a new file,
/// with a version above every version file on main, retracted ones included.
/// </summary>
public static class ListingPackVersions
{
    /// <summary>How many versions <see cref="FreeAsync"/> tries before it gives up.</summary>
    public const int MaxTries = 20;

    /// <summary>The listed pack with this id in any letter case, or null.</summary>
    public static ContentIndexPack? Pack(ContentIndexSnapshot snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Packs.FirstOrDefault(pack => ModIds.Equals(pack.Id, id));
    }

    /// <summary>The version with the highest precedence, retracted or not, or null for a pack without versions.</summary>
    public static ContentIndexPackVersion? Highest(ContentIndexPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        return pack.Versions.MaxBy(version => version.Metadata.Version);
    }

    /// <summary>
    /// The version that names the file of a pack version, packs/&lt;id&gt;/&lt;version&gt;.toml. The index requires the file name
    /// to be the version as the document writes it, and that text keeps build metadata, which <see cref="ModVersion"/> drops.
    /// </summary>
    public static string FileVersion(ContentIndexPackVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return string.IsNullOrEmpty(version.VersionText) ? version.Metadata.Version.ToString() : version.VersionText;
    }

    /// <summary>
    /// The version after <paramref name="version"/>, raised as npm raises a patch: a pre-release becomes its own release,
    /// and any other version gets the next patch number. Null when the patch number cannot grow.
    /// </summary>
    public static ModVersion? Raise(ModVersion version)
    {
        if (version.PreRelease is not null)
            return new ModVersion(version.Major, version.Minor, version.Patch);

        return version.Patch == int.MaxValue ? null : new ModVersion(version.Major, version.Minor, version.Patch + 1);
    }

    /// <summary>
    /// The first version from <paramref name="proposed"/> on, raised each time, for which <paramref name="exists"/> finds no file.
    /// The snapshot follows main with a delay, so main can already have a version that the snapshot does not know.
    /// Null when <see cref="MaxTries"/> versions are all taken.
    /// </summary>
    public static async Task<ModVersion?> FreeAsync(ModVersion proposed, Func<ModVersion, CancellationToken, Task<bool>> exists, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exists);

        ModVersion? version = proposed;
        for (var attempt = 0; attempt < MaxTries && version is { } current; attempt++)
        {
            if (!await exists(current, cancellationToken).ConfigureAwait(false))
                return current;

            version = Raise(current);
        }

        return null;
    }

    /// <summary>
    /// Whether a retraction reason names the mod id as a whole word, in any letter case. A dot followed by a letter or
    /// digit continues an id, and a dot that ends a sentence does not.
    /// </summary>
    public static bool Names(string? reason, string id)
    {
        if (string.IsNullOrEmpty(reason) || string.IsNullOrEmpty(id))
            return false;

        return Regex.IsMatch(reason, $@"(?<![A-Za-z0-9._-]){Regex.Escape(id)}(?![A-Za-z0-9_-]|\.[A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
