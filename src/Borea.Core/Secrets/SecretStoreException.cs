namespace Borea.Core.Secrets;

/// <summary>A call to the secret store failed. The message names the store and never the secret.</summary>
public sealed class SecretStoreException : Exception
{
    public SecretStoreProblem Problem { get; }

    public SecretStoreException(SecretStoreProblem problem, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Problem = problem;
    }
}
