namespace Ignixa.Domain.Terminology;

/// <summary>
/// Separates a terminology canonical URL from its optional, case-sensitive business version.
/// </summary>
public readonly record struct TerminologyCanonicalReference(string Url, string? Version)
{
    public static TerminologyCanonicalReference Parse(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        var separator = canonical.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0)
        {
            return new(canonical, null);
        }
        if (separator == 0 || separator == canonical.Length - 1 || canonical.IndexOf('|', separator + 1) >= 0)
        {
            throw new FormatException($"Invalid version-qualified terminology canonical '{canonical}'.");
        }
        return new(canonical[..separator], canonical[(separator + 1)..]);
    }
}
