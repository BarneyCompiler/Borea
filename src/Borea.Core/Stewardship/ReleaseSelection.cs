namespace Borea.Core.Stewardship;

/// <summary>The releases of one listing that an amendment applies to, as the options --version, --up-to and --all of tools/amend.py select them.</summary>
public sealed class ReleaseSelection
{
    private ReleaseSelection(IReadOnlyList<string>? versions, string? upTo)
    {
        Versions = versions;
        UpToVersion = upTo;
    }

    public static ReleaseSelection All { get; } = new(null, null);

    /// <summary>The versions named one by one, or null.</summary>
    public IReadOnlyList<string>? Versions { get; }

    /// <summary>The version at or below which every release is selected, or null.</summary>
    public string? UpToVersion { get; }

    /// <summary>One or several versions, each as an author may write it, so "0.7" selects 0.7.0.</summary>
    public static ReleaseSelection Of(params IEnumerable<string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        var list = versions.ToList();
        return list.Count > 0 ? new(list, null) : throw new ArgumentException("A selection names at least one version.", nameof(versions));
    }

    /// <summary>Every release at or below the version by SemVer precedence.</summary>
    public static ReleaseSelection UpTo(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new(null, version);
    }
}
