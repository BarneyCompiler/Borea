namespace Borea.Core.Listings;

/// <summary>Reads a thread of the KSA forums.</summary>
public interface IForumThreadReader
{
    /// <summary>The prefixes in front of the thread title, such as "Gameplay". Empty when the thread has none or could not be read.</summary>
    Task<IReadOnlyList<ForumPrefix>> GetPrefixesAsync(string threadUrl, CancellationToken cancellationToken = default);
}

/// <summary>A thread prefix as the forum shows it, with its XenForo prefix id when the page gives one.</summary>
public sealed record ForumPrefix(string Text, int? Id = null);
