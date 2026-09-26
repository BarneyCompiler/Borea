using Borea.Core.Mods;

namespace Borea.Cli.Commands;

internal static class LoaderLookup
{
    public static async Task<ModMetadata> GetListingAsync(
        IModRepository repository,
        string loaderId,
        CancellationToken cancellationToken)
    {
        var listings = await repository.GetAvailableModsAsync(cancellationToken).ConfigureAwait(false);
        return GetListing(listings, loaderId);
    }

    public static ModMetadata GetListing(IReadOnlyList<ModMetadata> listings, string loaderId)
    {
        var listing = listings.FirstOrDefault(candidate => ModIds.Equals(candidate.ModId, loaderId));
        if (listing is null)
            throw new InvalidOperationException($"Mod loader '{loaderId}' is not available from the configured sources.");

        if (listing.Type != ContentType.ModLoader)
            throw new InvalidOperationException($"'{listing.ModId}' is a {listing.Type}, not a mod loader.");

        return listing;
    }
}
