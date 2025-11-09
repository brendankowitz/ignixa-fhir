using System.Reflection;
using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Loads FHIR packages that are embedded as resources in a .NET assembly.
/// Used for bundled packages like SQL-on-FHIR ViewDefinition.
/// </summary>
public class EmbeddedPackageLoader : IPackageLoader
{
    private readonly Assembly _assembly;
    private readonly ILogger<EmbeddedPackageLoader> _logger;

    /// <summary>
    /// Maps package IDs to their assembly resource names.
    /// Example: "local.ignixa.sqlonfhir" -> "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2"
    /// </summary>
    private static readonly Dictionary<string, string> PackageResourceMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { "local.ignixa.sqlonfhir", "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2" }
    };

    public EmbeddedPackageLoader(Assembly assembly, ILogger<EmbeddedPackageLoader> logger)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Downloads an embedded package by extracting it from assembly resources.
    /// Embedded packages are distributed as part of the application assembly.
    /// </summary>
    public async Task<Stream> DownloadPackageAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        }

        // Check if this package is embedded in the assembly
        if (!PackageResourceMapping.TryGetValue(packageId, out var packageResourcePrefix))
        {
            throw new InvalidOperationException(
                $"Package '{packageId}' is not available as an embedded resource. " +
                $"Embedded packages: {string.Join(", ", PackageResourceMapping.Keys)}");
        }

        try
        {
            _logger.LogInformation(
                "Loading embedded package {PackageId}@{Version} from assembly",
                packageId,
                version);

            // Get all resource names from assembly
            var resourceNames = _assembly.GetManifestResourceNames();

            // Find package.json to verify package exists
            var packageJsonName = resourceNames.FirstOrDefault(r =>
                r.StartsWith(packageResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                r.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));

            if (packageJsonName == null)
            {
                throw new FileNotFoundException(
                    $"Embedded package '{packageId}' not found in assembly. " +
                    $"Expected resource with name starting with '{packageResourcePrefix}' and ending with 'package.json'");
            }

            _logger.LogDebug(
                "Found embedded package resource: {ResourceName}",
                packageJsonName);

            // Create in-memory tarball (simplified: just return a stream of the JSON structure)
            // For a real implementation, this would create a proper .tgz with package/ directory
            var packageStream = await CreatePackageStreamAsync(packageResourcePrefix, cancellationToken);

            _logger.LogInformation(
                "Successfully loaded embedded package {PackageId}@{Version}",
                packageId,
                version);

            return packageStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error loading embedded package {PackageId}@{Version}",
                packageId,
                version);
            throw;
        }
    }

    /// <summary>
    /// Creates an in-memory package stream from embedded resources.
    /// Assembles package.json and StructureDefinition JSON files into a .tgz tarball.
    /// </summary>
    private async Task<Stream> CreatePackageStreamAsync(
        string packageResourcePrefix,
        CancellationToken cancellationToken)
    {
        var resultStream = new MemoryStream();

        // Get all resources under this package
        var resourceNames = _assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(packageResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogDebug("Found {Count} embedded resources for package", resourceNames.Count);

        // Create tarball with gzip compression
        using (var gzipStream = new System.IO.Compression.GZipStream(resultStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var tarWriter = new System.Formats.Tar.TarWriter(gzipStream, leaveOpen: false))
        {
            foreach (var resourceName in resourceNames)
            {
                // Extract relative path from resource name
                // Example: "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2.package.package.json" -> "package/package.json"
                var relativePath = ExtractRelativePathFromResourceName(resourceName, packageResourcePrefix);

                _logger.LogDebug("Adding {ResourceName} as {RelativePath} to tarball", resourceName, relativePath);

                using var resourceStream = _assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    _logger.LogWarning("Could not load embedded resource: {ResourceName}", resourceName);
                    continue;
                }

                // Read resource content
                using var memoryBuffer = new MemoryStream();
                await resourceStream.CopyToAsync(memoryBuffer, cancellationToken);
                memoryBuffer.Position = 0;

                // Create tar entry
                var tarEntry = new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, relativePath)
                {
                    DataStream = memoryBuffer
                };

                await tarWriter.WriteEntryAsync(tarEntry, cancellationToken);
            }
        }

        resultStream.Position = 0;
        return resultStream;
    }

    /// <summary>
    /// Extracts the relative path from the embedded resource name.
    /// Converts "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2.package.package.json" to "package/package.json"
    /// </summary>
    private static string ExtractRelativePathFromResourceName(string resourceName, string packageResourcePrefix)
    {
        // Remove the package prefix
        var relativePart = resourceName.Substring(packageResourcePrefix.Length).TrimStart('.');

        // Replace dots with slashes, but keep the file extension intact
        // "package.package.json" -> "package/package.json"
        var lastDotIndex = relativePart.LastIndexOf('.');
        if (lastDotIndex > 0)
        {
            var pathPart = relativePart.Substring(0, lastDotIndex).Replace('.', '/');
            var extension = relativePart.Substring(lastDotIndex);
            return pathPart + extension;
        }

        return relativePart.Replace('.', '/');
    }
}
