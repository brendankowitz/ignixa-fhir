using Ignixa.Domain.Abstractions;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// Imports extracted package resources to the database.
/// </summary>
public interface IPackageResourceImporter
{
    /// <summary>
    /// Imports extracted resources to the PackageResource table.
    /// </summary>
    /// <param name="extraction">Extracted resources and manifest</param>
    /// <param name="repository">Tenant-specific package resource repository</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with statistics</returns>
    Task<PackageImportResult> ImportAsync(
        PackageExtractionResult extraction,
        IPackageResourceRepository repository,
        CancellationToken cancellationToken);
}
