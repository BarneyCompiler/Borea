using System.Text;

namespace Borea.Core.Stewardship;

public enum IndexStatusAction
{
    Dispute,
    Delist,
    Retract,
    Lift,
}

/// <summary>
/// One steward change of index-status.toml, which goes into a pull request of its own.
/// The reason goes into the file and into the pull request. A lift writes it only into the pull request.
/// </summary>
/// <param name="State">The state it sets, or for a lift the state of the entry it removes.</param>
/// <param name="Version">The pack version of a retracted state, else null.</param>
public sealed record IndexStatusChange(IndexStatusAction Action, string Id, string State, string? Version, string Reason)
{
    public const string BranchPrefix = "steward/";

    /// <summary>A reason is one line, so it also refuses the Unicode line and paragraph separators.</summary>
    private static readonly char[] LineBreaks = [(char)0x2028, (char)0x2029];

    public static IndexStatusChange Dispute(string id, string reason) => new(IndexStatusAction.Dispute, id, IndexStatusEntry.Disputed, null, reason);

    public static IndexStatusChange Delist(string id, string reason) => new(IndexStatusAction.Delist, id, IndexStatusEntry.Delisted, null, reason);

    public static IndexStatusChange Retract(string id, string? version, string reason) => new(IndexStatusAction.Retract, id, IndexStatusEntry.Retracted, version, reason);

    /// <summary>Whether the reason is one sentence that index-status.toml and the pull request can hold: not empty, one line, and no control character.</summary>
    public static bool IsValidReason(string? reason)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        return trimmed.Length > 0 && !trimmed.Any(char.IsControl) && trimmed.IndexOfAny(LineBreaks) < 0;
    }

    public static IndexStatusChange Lift(IndexStatusEntry entry, string reason)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(IndexStatusAction.Lift, entry.Id, entry.State, entry.Version, reason);
    }

    /// <summary>The title of the pull request and the commit message, such as "Delist MyMod".</summary>
    public string Title => $"{Action} {Id}{(Version is null ? string.Empty : " " + Version)}";

    /// <summary>The branch of content-index the change goes to, steward/&lt;state&gt;-&lt;id&gt;, or steward/lift-&lt;id&gt; for a lift.</summary>
    public string Branch =>
        $"{BranchPrefix}{(Action == IndexStatusAction.Lift ? "lift" : State)}-{Id.ToLowerInvariant()}{(Version is null ? string.Empty : "-" + Version)}";

    /// <summary>The body of the pull request. It mentions each owner, so the owner is told.</summary>
    /// <param name="owners">The logins that own the listing or pack, without the steward who opens it.</param>
    public string Body(IReadOnlyCollection<string> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        var scope = Version is null ? $"`{Id}`" : $"version {Version} of `{Id}`";
        var body = new StringBuilder(Action == IndexStatusAction.Lift
            ? $"Lifts `{State}` from {scope} in `{IndexStatusDocument.Path}`."
            : $"Sets `{State}` on {scope} in `{IndexStatusDocument.Path}`.");
        body.Append("\n\nReason: ").Append(Reason.Trim());
        if (owners.Count > 0)
            body.Append("\n\n").Append(string.Join(' ', owners.Select(owner => "@" + owner))).Append(owners.Count == 1 ? " owns " : " own ").Append($"`{Id}`.");

        return body.ToString();
    }
}
