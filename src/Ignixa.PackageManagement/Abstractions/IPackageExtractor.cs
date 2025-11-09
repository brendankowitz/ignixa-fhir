using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// Extracts resources from a FHIR NPM package.
/// </summary>
public interface IPackageExtractor
{
    /// <summary>
    /// Extracts resources from a package stream.
    /// </summary>
    /// <param name="packageStream">Stream containing the package .tgz file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with manifest and resources</returns>
    Task<PackageExtractionResult> ExtractAsync(
        Stream packageStream,
        CancellationToken cancellationToken);
}
