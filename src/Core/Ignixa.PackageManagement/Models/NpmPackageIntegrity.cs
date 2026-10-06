namespace Ignixa.PackageManagement.Models;

/// <summary>Exactly one canonical SHA-512 SRI token, supplied by trusted metadata or an administrator.</summary>
public sealed record NpmPackageIntegrity
{
    public NpmPackageIntegrity(string integrity)
    {
        ArgumentNullException.ThrowIfNull(integrity);
        Span<byte> bytes = stackalloc byte[64];
        if (integrity.Length != 95 || !integrity.StartsWith("sha512-", StringComparison.Ordinal) ||
            !Convert.TryFromBase64String(integrity[7..], bytes, out int written) || written != 64 ||
            Convert.ToBase64String(bytes) != integrity[7..])
        {
            throw new ArgumentException("One canonical SHA-512 integrity token is required.", nameof(integrity));
        }

        Integrity = integrity;
    }

    public string Integrity { get; }
}
