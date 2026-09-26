namespace Borea.Core.Stewardship;

/// <summary>
/// An amendment of release files as a person asks for it, with the values as typed, like the options of tools/amend.py.
/// <see cref="ReleaseAmendment.Create"/> resolves and checks it.
/// </summary>
public sealed record ReleaseChange
{
    /// <summary>A game build, or a month for its first build, that raises the lower game bound.</summary>
    public string? GameMin { get; init; }

    /// <summary>A game build, or a month that is over for its last build, that adds or lowers the upper game bound.</summary>
    public string? GameMax { get; init; }

    public bool Yank { get; init; }

    /// <summary>One sentence shown with the yank, and only with a yank.</summary>
    public string? YankReason { get; init; }

    public string? LoaderMin { get; init; }

    public string? LoaderMax { get; init; }

    /// <summary>Dependency entries that were missing, including a conflict.</summary>
    public IReadOnlyList<ReleaseDependencyAddition> AddedDependencies { get; init; } = [];

    /// <summary>New bounds of dependencies that the releases state, applied after <see cref="AddedDependencies"/>.</summary>
    public IReadOnlyList<ReleaseDependencyBounds> DependencyBounds { get; init; } = [];
}

/// <param name="Kind">A dependency kind as a release file writes it, such as "conflict".</param>
public sealed record ReleaseDependencyAddition(string Id, string Kind);

/// <summary>A new min, a new max or both of the dependency on <paramref name="Id"/>, which matches without regard to case.</summary>
public sealed record ReleaseDependencyBounds(string Id, string? Min, string? Max);

/// <summary>Who makes an amendment, which decides whether it may widen a release.</summary>
public enum ReleaseAmender
{
    /// <summary>A steward acting alone, who only narrows (RFC 0031).</summary>
    Steward,

    /// <summary>The verified owner of the listing, or anyone on the owner's request, who may also widen (RFC 0079).</summary>
    Owner,
}

/// <param name="Path">The path of the release file in content-index-releases.</param>
/// <param name="Text">The new text of the file.</param>
/// <param name="Widens">Whether the change widens the release, which only the owner does.</param>
public sealed record AmendedRelease(string Path, string Text, bool Widens);
