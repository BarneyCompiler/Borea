namespace Borea.Core.Secrets;

/// <summary>
/// The secret store of the operating system for the current user, such as DPAPI, the Keychain or the Secret Service.
/// A call can wait for the user, for example to unlock a keyring, so callers keep it off the UI thread.
/// Every failure throws <see cref="SecretStoreException"/>, whose message never holds the secret.
/// </summary>
public interface ISecretStore
{
    /// <summary>The secret named <paramref name="name"/>, or null when none is stored.</summary>
    string? Read(string name);

    /// <summary>Stores <paramref name="secret"/> under <paramref name="name"/> and replaces an older one.</summary>
    void Write(string name, string secret);

    /// <summary>Deletes the secret named <paramref name="name"/>. Nothing stored is no failure.</summary>
    void Delete(string name);
}
