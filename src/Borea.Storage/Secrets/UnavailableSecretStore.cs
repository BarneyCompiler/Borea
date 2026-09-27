using Borea.Core.Secrets;

namespace Borea.Storage.Secrets;

/// <summary>A store that refuses every call with one problem, and so never holds a secret.</summary>
internal sealed class UnavailableSecretStore(SecretStoreProblem problem) : ISecretStore
{
    public string? Read(string name) => throw Refusal();

    public void Write(string name, string secret) => throw Refusal();

    public void Delete(string name) => throw Refusal();

    private SecretStoreException Refusal() => new(problem, "No secret store works on this computer.");
}
