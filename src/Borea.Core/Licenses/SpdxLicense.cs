namespace Borea.Core.Licenses;

/// <summary>One license of an SPDX license expression, with the exception that a WITH adds to it.</summary>
public sealed record SpdxLicense(string Id, string? Exception)
{
    /// <summary>The license as an expression writes it, such as MIT or GPL-2.0-only WITH Classpath-exception-2.0.</summary>
    public string Name => Exception is null ? Id : $"{Id} WITH {Exception}";
}
