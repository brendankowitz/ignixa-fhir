namespace Ignixa.PackageManagement.Models;

/// <summary>Output of complete extraction; the extractor returns no result on failure.</summary>
/// <param name="Manifest">The single canonical package manifest.</param>
/// <param name="JsonEntries">Non-manifest JSON in archive order, returned as a read-only collection.</param>
/// <param name="Statistics">Accounting for the complete archive, including ignored entries.</param>
/// <remarks>
/// Direct construction does not confer extractor validation. Record/list equality is not a
/// cryptographic artifact identity. Source approval and FHIR validation remain caller responsibilities.
/// </remarks>
public sealed record StrictPackageExtractionResult(
    StrictPackageManifest Manifest,
    IReadOnlyList<StrictPackageEntry> JsonEntries,
    PackageExtractionStatistics Statistics);
