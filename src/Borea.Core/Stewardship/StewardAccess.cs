namespace Borea.Core.Stewardship;

/// <summary>Where the signed-in account is a steward: GitHub lets it bypass the ruleset of main in that index repository.</summary>
public sealed record StewardAccess(string Login, bool ContentIndex, bool ContentIndexReleases)
{
    public const string ReleasesRepository = "KSAModding/content-index-releases";

    public bool IsSteward => ContentIndex || ContentIndexReleases;
}
