namespace Borea.Storage.Secrets;

internal static class SecretNames
{
    /// <summary>The name, which DPAPI uses as a file name, so it may hold only lowercase letters, digits and hyphens.</summary>
    public static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
            throw new ArgumentException("A secret name holds only lowercase letters, digits and hyphens.", nameof(name));

        return name;
    }
}
