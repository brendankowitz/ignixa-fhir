namespace Ignixa.PackageManagement.Models;

/// <summary>Verified acquisition only, not proof of installation, compatibility or activation.</summary>
public sealed class AcquiredNpmPackage
{
    internal AcquiredNpmPackage(string sourceId, NpmPackageIdentity identity, NpmPackageIntegrity integrity, StrictPackageExtractionResult extraction)
    {
        SourceId = sourceId;
        Identity = identity;
        Integrity = integrity;
        Extraction = extraction;
    }

    public string SourceId { get; }
    public NpmPackageIdentity Identity { get; }
    public NpmPackageIntegrity Integrity { get; }
    public StrictPackageExtractionResult Extraction { get; }
}
