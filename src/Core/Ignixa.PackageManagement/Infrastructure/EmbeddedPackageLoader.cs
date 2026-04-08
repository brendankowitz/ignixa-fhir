using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.PackageManagement.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Loads FHIR packages that are embedded as resources in a .NET assembly.
/// Used for bundled packages like SQL-on-FHIR ViewDefinition.
/// Supports multiple embedded packages registered via IEmbeddedPackage.
/// </summary>
public partial class EmbeddedPackageLoader : IPackageLoader
{
    private readonly List<IEmbeddedPackage> _embeddedPackages;
    private readonly ILogger<EmbeddedPackageLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbeddedPackageLoader"/> class.
    /// </summary>
    /// <param name="embeddedPackages">The embedded package definitions to load.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public EmbeddedPackageLoader(IEnumerable<IEmbeddedPackage> embeddedPackages, ILogger<EmbeddedPackageLoader> logger)
    {
        _embeddedPackages = embeddedPackages?.ToList() ?? throw new ArgumentNullException(nameof(embeddedPackages));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_embeddedPackages.Count == 0)
        {
            throw new ArgumentException("At least one embedded package must be provided", nameof(embeddedPackages));
        }
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

        // Find the embedded package matching this ID
        var embeddedPackage = _embeddedPackages.FirstOrDefault(p =>
            p.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (embeddedPackage == null)
        {
            var availablePackages = string.Join(", ", _embeddedPackages.Select(p => p.PackageId));
            throw new InvalidOperationException(
                $"Package '{packageId}' is not available as an embedded resource. " +
                $"Embedded packages: {availablePackages}");
        }

        try
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                var assemblyName = embeddedPackage.Assembly.GetName().Name;
            LogLoadingEmbeddedPackage(_logger, packageId, version, assemblyName);
            }

            // Get all resource names from assembly
            var resourceNames = embeddedPackage.Assembly.GetManifestResourceNames();

            // Find package.json to verify package exists
            var packageJsonName = resourceNames.FirstOrDefault(r =>
                r.StartsWith(embeddedPackage.ResourcePrefix, StringComparison.OrdinalIgnoreCase) &&
                r.EndsWith("package.json", StringComparison.OrdinalIgnoreCase));

            if (packageJsonName == null)
            {
                throw new FileNotFoundException(
                    $"Embedded package '{packageId}' not found in assembly. " +
                    $"Expected resource with name starting with '{embeddedPackage.ResourcePrefix}' and ending with 'package.json'");
            }

            LogFoundEmbeddedResource(_logger, packageJsonName);

            // Create in-memory tarball
            var packageStream = await CreatePackageStreamAsync(embeddedPackage, cancellationToken);

            LogEmbeddedPackageLoaded(_logger, packageId, version);

            return packageStream;
        }
        catch (Exception ex)
        {
            LogErrorLoadingEmbeddedPackage(_logger, ex, packageId, version);
            throw;
        }
    }

    /// <summary>
    /// Creates an in-memory package stream from embedded resources.
    /// Assembles package.json and StructureDefinition JSON files into a .tgz tarball.
    /// </summary>
    private async Task<Stream> CreatePackageStreamAsync(
        IEmbeddedPackage embeddedPackage,
        CancellationToken cancellationToken)
    {
        var resultStream = new MemoryStream();

        // Get all resources under this package
        var resourceNames = embeddedPackage.Assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith(embeddedPackage.ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        LogFoundEmbeddedResources(_logger, resourceNames.Count);

        // Create tarball with gzip compression
        using (var gzipStream = new System.IO.Compression.GZipStream(resultStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        using (var tarWriter = new System.Formats.Tar.TarWriter(gzipStream, leaveOpen: false))
        {
            foreach (var resourceName in resourceNames)
            {
                // Extract relative path from resource name
                // Example: "Ignixa.SqlOnFhir.packages.sql-on-fhir-v2.package.package.json" -> "package/package.json"
                var relativePath = ExtractRelativePathFromResourceName(resourceName, embeddedPackage.ResourcePrefix);

                LogAddingResourceToTarball(_logger, resourceName, relativePath);

                using var resourceStream = embeddedPackage.Assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    LogCouldNotLoadEmbeddedResource(_logger, resourceName);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading embedded package {PackageId}@{Version} from assembly {AssemblyName}")]
    private static partial void LogLoadingEmbeddedPackage(ILogger logger, string packageId, string version, string? assemblyName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found embedded package resource: {ResourceName}")]
    private static partial void LogFoundEmbeddedResource(ILogger logger, string resourceName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully loaded embedded package {PackageId}@{Version}")]
    private static partial void LogEmbeddedPackageLoaded(ILogger logger, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading embedded package {PackageId}@{Version}")]
    private static partial void LogErrorLoadingEmbeddedPackage(ILogger logger, Exception ex, string packageId, string version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} embedded resources for package")]
    private static partial void LogFoundEmbeddedResources(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Adding {ResourceName} as {RelativePath} to tarball")]
    private static partial void LogAddingResourceToTarball(ILogger logger, string resourceName, string relativePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load embedded resource: {ResourceName}")]
    private static partial void LogCouldNotLoadEmbeddedResource(ILogger logger, string resourceName);
}
