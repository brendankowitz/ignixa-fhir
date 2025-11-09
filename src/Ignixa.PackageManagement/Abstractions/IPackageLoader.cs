namespace Ignixa.PackageManagement.Abstractions;

/// <summary>
/// Downloads FHIR NPM packages from a registry.
/// </summary>
public interface IPackageLoader
{
    /// <summary>
    /// Downloads a package from the NPM registry.
    /// </summary>
    /// <param name="packageId">Package ID (e.g., "hl7.fhir.us.core")</param>
    /// <param name="version">Package version (e.g., "5.0.1")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream containing the package .tgz file</returns>
    /// <remarks>
    /// The caller is responsible for disposing the returned stream.
    /// </remarks>
    Task<Stream> DownloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);
}
