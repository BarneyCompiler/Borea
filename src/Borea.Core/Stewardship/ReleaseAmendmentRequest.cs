using System.Text;
using Borea.Core.Mods;

namespace Borea.Core.Stewardship;

/// <summary>A steward amendment of the releases of one listing, as the steward asks for it. It goes into one pull request of content-index-releases.</summary>
/// <param name="ListingId">The id of the listing, as its release folder spells it.</param>
/// <param name="Change">The change as typed. A yank without a reason of its own takes <paramref name="Reason"/>.</param>
/// <param name="Reason">One sentence for the pull request, and for a yank also the reason that players see.</param>
public sealed record ReleaseAmendmentRequest(string ListingId, ReleaseSelection Selection, ReleaseChange Change, string Reason)
{
    /// <summary>The characters that a value of <see cref="Command"/> keeps without quotes.</summary>
    private const string PlainPunctuation = "._-:=/@+";

    /// <summary>The change that goes into the files.</summary>
    public ReleaseChange Amendment => Change is { Yank: true, YankReason: null } ? Change with { YankReason = Reason.Trim() } : Change;

    /// <summary>The branch of content-index-releases the amendment goes to, steward/amend-&lt;id&gt;.</summary>
    public string Branch => $"{IndexStatusChange.BranchPrefix}amend-{ListingId.ToLowerInvariant()}";

    /// <summary>Whether it changes bounds that the listing in content-index also states, which the next stamp takes from the listing.</summary>
    public bool ChangesAuthoredBounds =>
        Change.LoaderMin is not null || Change.LoaderMax is not null || Change.AddedDependencies.Count > 0 || Change.DependencyBounds.Count > 0;

    /// <summary>The same amendment as a command of tools/amend.py for a POSIX shell, so a reviewer can derive it again.</summary>
    public string Command
    {
        get
        {
            var arguments = new List<string> { "python3", "tools/amend.py", "--listing", ListingId };
            if (Selection.UpToVersion is { } upTo)
                arguments.AddRange(["--up-to", upTo.Trim()]);
            else if (Selection.Versions is { } versions)
                arguments.AddRange(versions.SelectMany(version => new[] { "--version", version.Trim() }));
            else
                arguments.Add("--all");

            var change = Amendment;
            void Option(string name, string? value)
            {
                if (value is not null)
                    arguments.AddRange([name, value.Trim()]);
            }

            Option("--game-min", change.GameMin);
            Option("--game-max", change.GameMax);
            if (change.Yank)
                arguments.Add("--yank");
            Option("--reason", change.YankReason);
            Option("--loader-min", change.LoaderMin);
            Option("--loader-max", change.LoaderMax);
            foreach (var bounds in change.DependencyBounds.Where(bounds => bounds.Min is not null))
                Option("--dependency-min", $"{bounds.Id.Trim()}={bounds.Min!.Trim()}");
            foreach (var bounds in change.DependencyBounds.Where(bounds => bounds.Max is not null))
                Option("--dependency-max", $"{bounds.Id.Trim()}={bounds.Max!.Trim()}");
            foreach (var addition in change.AddedDependencies)
                Option("--add-dependency", $"{addition.Id.Trim()}:{addition.Kind.Trim()}");

            return string.Join(' ', arguments.Select(Quote));
        }
    }

    /// <summary>A value as a POSIX shell reads it back, in single quotes unless it is plain.</summary>
    private static string Quote(string value) =>
        value.Length > 0 && value.All(character => char.IsAsciiLetterOrDigit(character) || PlainPunctuation.Contains(character))
            ? value
            : "'" + value.Replace("'", @"'\''", StringComparison.Ordinal) + "'";
}

/// <summary>An amendment derived at one commit of the base branch, before anything is written.</summary>
/// <param name="Files">Every selected release file, in the order of the selection.</param>
/// <param name="Owners">The logins that own the listing, as the ownership proof names them, without the signed-in steward.</param>
public sealed record ReleaseAmendmentPreview(ReleaseAmendmentRequest Request, IReadOnlyList<ReleaseFilePreview> Files, IReadOnlyList<string> Owners)
{
    /// <summary>The title names at most this many versions, and a range beyond.</summary>
    private const int TitleVersions = 3;

    /// <summary>The files that the amendment changes.</summary>
    public IReadOnlyList<ReleaseFilePreview> Changed => [.. Files.Where(file => file.After is not null)];

    /// <summary>The title of the pull request and the commit message, such as "Amend MyMod 1.0.0, 1.1.0".</summary>
    public string Title
    {
        get
        {
            var versions = ChangedVersions();
            return versions.Count switch
            {
                0 => $"Amend {Request.ListingId}",
                <= TitleVersions => $"Amend {Request.ListingId} {string.Join(", ", versions)}",
                _ => $"Amend {Request.ListingId} {versions[0]} to {versions[^1]} ({versions.Count} releases)",
            };
        }
    }

    /// <summary>The body of the pull request. It mentions each owner, so the owner is told.</summary>
    public string Body
    {
        get
        {
            var versions = ChangedVersions();
            var body = new StringBuilder(versions.Count == 1
                ? $"Amends release {versions[0]} of `{Request.ListingId}`."
                : $"Amends {versions.Count} releases of `{Request.ListingId}`: {string.Join(", ", versions)}.");
            body.Append("\n\nReason: ").Append(Request.Reason.Trim());
            body.Append("\n\nThe same amendment with the tools of this repository:\n\n```text\n").Append(Request.Command).Append("\n```");
            if (Request.ChangesAuthoredBounds)
                body.Append("\n\nThe listing in content-index states its bounds separately, so the next release is stamped without this change until the listing has it too.");
            if (OwnerMention.Of(Owners, Request.ListingId) is { } mention)
                body.Append("\n\n").Append(mention);

            return body.ToString();
        }
    }

    /// <summary>Whether both derive the same files from the same texts, so what the steward saw is what is sent.</summary>
    public bool HasSameFiles(ReleaseAmendmentPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Files.SequenceEqual(other.Files);
    }

    /// <summary>The changed versions, oldest first by SemVer precedence.</summary>
    private List<string> ChangedVersions() =>
        [.. Changed.Select(file => file.Version).OrderBy(ModVersion.Parse).ThenBy(version => version, StringComparer.Ordinal)];
}

/// <param name="Path">The path of the file in content-index-releases.</param>
/// <param name="Before">The text of the file on the base branch.</param>
/// <param name="After">The amended text, or null when the release already says this.</param>
public sealed record ReleaseFilePreview(string Version, string Path, string Before, string? After)
{
    /// <summary>The change as a unified diff without file headers, the form GitHub shows, or null when nothing changes.</summary>
    public string? Patch => After is null ? null : LineDiff.Unified(Before, After);
}
