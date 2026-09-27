using Borea.Core.Paths;
using Borea.Core.Secrets;

namespace Borea.Storage.Secrets;

public static class SecretStores
{
    /// <summary>
    /// The secret store of this operating system for the current user: DPAPI on Windows, the Keychain on macOS, the Secret Service on Linux.
    /// Elsewhere every call fails with <see cref="SecretStoreProblem.Unsupported"/>.
    /// </summary>
    public static ISecretStore ForCurrentUser(IGamePathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (OperatingSystem.IsWindows())
            return new DpapiSecretStore(paths.GetSecretsFolder());

        if (OperatingSystem.IsMacOS())
            return new KeychainSecretStore();

        if (OperatingSystem.IsLinux())
            return new LibSecretStore();

        return new UnavailableSecretStore(SecretStoreProblem.Unsupported);
    }
}
