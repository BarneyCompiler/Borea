using Borea.Core.Index;

namespace Borea.Core.Stewardship;

/// <summary>One entry of index-status.toml in content-index, as the file writes it.</summary>
/// <param name="State">The state text, which can be one this build does not know.</param>
/// <param name="Version">The pack version of a retracted state.</param>
public sealed record IndexStatusEntry(string Id, string State, string? Version, string? Since, string? Reason)
{
    public const string Delisted = "delisted";

    public const string Disputed = "disputed";

    public const string Retracted = "retracted";

    public IndexStatusState Kind => State switch
    {
        Delisted => IndexStatusState.Delisted,
        Disputed => IndexStatusState.Disputed,
        Retracted => IndexStatusState.Retracted,
        _ => IndexStatusState.Unknown,
    };

    /// <summary>Whether this is the entry of <paramref name="id"/>, <paramref name="state"/> and <paramref name="version"/>. Ids compare case-insensitively, as the index does.</summary>
    public bool Names(string id, string state, string? version) =>
        string.Equals(Id, id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(State, state, StringComparison.Ordinal)
        && string.Equals(Version, version, StringComparison.Ordinal);
}
