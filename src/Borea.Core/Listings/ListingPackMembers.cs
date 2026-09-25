using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>
/// What a pack version can pin: listings of type mod that are not delisted, at stamped releases that are not yanked.
/// A pin to anything else is one that no client can install.
/// </summary>
public static class ListingPackMembers
{
    /// <summary>The listed mods that have at least one release to pin, in the order of the snapshot.</summary>
    public static IReadOnlyList<ContentIndexListing> Candidates(ContentIndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.Where(listing => IsCandidate(listing) && Offered(listing).Count > 0).ToList();
    }

    /// <summary>The releases of a listing that a pin can name, newest first.</summary>
    public static IReadOnlyList<ModVersionMetadata> Offered(ContentIndexListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        return IsCandidate(listing) ? listing.Releases.Where(release => !release.Yanked).OrderByDescending(release => release.Version).ToList() : [];
    }

    /// <summary>The listed mod a pin names, or null when the snapshot has no such mod to pin.</summary>
    public static ContentIndexListing? Listing(ContentIndexSnapshot snapshot, string id)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.FirstOrDefault(listing => ModIds.Equals(listing.Id, id) && IsCandidate(listing));
    }

    /// <summary>The release a pin names, when a pin can name it.</summary>
    public static ModVersionMetadata? Release(ContentIndexSnapshot snapshot, ListingPackMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (Listing(snapshot, member.Id) is not { } listing || !ModVersion.TryParse(member.Version, out var version))
            return null;

        return Offered(listing).FirstOrDefault(release => release.Version == version);
    }

    /// <summary>
    /// The pinned release with the highest game_min, which is the game_min the pack needs, or null when no pin names a release.
    /// Only the revision orders game versions, so it decides.
    /// </summary>
    public static ModVersionMetadata? HighestGameMin(ContentIndexSnapshot snapshot, IEnumerable<ListingPackMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        return members.Select(member => Release(snapshot, member)).OfType<ModVersionMetadata>().MaxBy(release => release.GameMinRevision);
    }

    private static bool IsCandidate(ContentIndexListing listing) =>
        listing.Authored?.Type == ContentType.Mod && listing.IndexStatus?.State != IndexStatusState.Delisted;
}
