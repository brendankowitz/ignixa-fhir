using Ignixa.PackageManagement.Infrastructure;

namespace Ignixa.PackageManagement.Models;

/// <summary>Administrator-bound trust and inclusive resource policy; no credentials or installation state.</summary>
public sealed class NpmPackageSourcePolicy
{
    public NpmPackageSourcePolicy(
        string sourceId, Uri registryBaseUri, IEnumerable<Uri> allowedArtifactPrefixes,
        PackageExtractionLimits? extractionLimits = null, int maxMetadataBytes = 1048576,
        PackageAcquisitionRetryPolicy? retry = null, int maxRedirects = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(registryBaseUri);
        ArgumentNullException.ThrowIfNull(allowedArtifactPrefixes);
        SourceId = sourceId.Trim();
        if (SourceId.Length is < 1 or > 128 || !NpmPackageIdentity.ValidSegment(SourceId, allowUppercase: true))
        {
            throw new ArgumentException("An administrator-bound source identifier is required.", nameof(sourceId));
        }

        NpmSourceUri.ValidatePrefix(registryBaseUri, nameof(registryBaseUri));
        Uri[] prefixes = allowedArtifactPrefixes.ToArray();
        if (prefixes.Length == 0)
        {
            throw new ArgumentException("At least one artifact directory prefix is required.", nameof(allowedArtifactPrefixes));
        }

        foreach (Uri prefix in prefixes)
        {
            NpmSourceUri.ValidatePrefix(prefix, nameof(allowedArtifactPrefixes));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRedirects);
        RegistryBaseUri = registryBaseUri;
        AllowedArtifactPrefixes = Array.AsReadOnly(prefixes);
        ExtractionLimits = extractionLimits ?? new();
        MaxMetadataBytes = maxMetadataBytes;
        Retry = retry ?? new();
        MaxRedirects = maxRedirects;
    }

    public string SourceId { get; }
    public Uri RegistryBaseUri { get; }
    public IReadOnlyList<Uri> AllowedArtifactPrefixes { get; }
    public PackageExtractionLimits ExtractionLimits { get; }
    public int MaxMetadataBytes { get; }
    public PackageAcquisitionRetryPolicy Retry { get; }
    public int MaxRedirects { get; }
}
