using System.Globalization;
using System.Text.Json;
using Borea.Core.Index;
using Borea.Core.Mods;

namespace Borea.Core.Listings;

/// <summary>
/// The owner record of a pack, packs/&lt;id&gt;/owner.json in content-index. The first pull request of a pack adds it,
/// and the checks compare its numeric account id with the author of each later version, because a login can change.
/// </summary>
public sealed record ListingPackOwner(string Login, long Id)
{
    public const string FileName = "owner.json";

    public static string PathOf(string packId)
    {
        ArgumentNullException.ThrowIfNull(packId);
        return $"{ListingDraft.PacksFolder}/{packId}/{FileName}";
    }

    /// <summary>The record in the layout of the owner records in content-index.</summary>
    public string ToJson() =>
        $"{{\n  \"github_login\": {JsonSerializer.Serialize(Login)},\n  \"github_id\": {Id.ToString(CultureInfo.InvariantCulture)}\n}}\n";

    /// <summary>The record, or null when the text is not an owner record with a login and a positive account id.</summary>
    public static ListingPackOwner? Parse(string? text)
    {
        if (text is null)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("github_login", out var login) && login.ValueKind == JsonValueKind.String && login.GetString() is { Length: > 0 } name
                && root.TryGetProperty("github_id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0
                    ? new ListingPackOwner(name, number)
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The id of the listing or pack in the snapshot that already holds <paramref name="packId"/> in any letter case, or null.
    /// The paths of content-index are case-sensitive, so an owner record that is not found does not make the id free.
    /// </summary>
    public static string? HolderOf(ContentIndexSnapshot snapshot, string packId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Listings.Select(listing => listing.Id)
            .Concat(snapshot.Packs.Select(pack => pack.Id))
            .FirstOrDefault(id => ModIds.Equals(id, packId));
    }
}
