namespace Borea.Core.Secrets;

/// <summary>Why Borea cannot keep a secret on this computer.</summary>
public enum SecretStoreProblem
{
    /// <summary>Borea has no secret store for this operating system.</summary>
    Unsupported,

    /// <summary>The secret store is not there, such as Linux without a keyring that offers the Secret Service.</summary>
    Missing,

    /// <summary>The secret store refused, for example because it is locked or the user cancelled its prompt.</summary>
    Refused,
}
