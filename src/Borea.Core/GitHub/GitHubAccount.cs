namespace Borea.Core.GitHub;

/// <summary>A GitHub account. The login can change hands, so a proof by owner compares <see cref="Id"/>.</summary>
public sealed record GitHubAccount(string Login, long Id);
