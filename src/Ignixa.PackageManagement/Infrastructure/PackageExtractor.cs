using System.Formats.Tar;
using System.Text.Json;
using Ignixa.PackageManagement.Abstractions;
using Ignixa.PackageManagement.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Extracts FHIR resources from a package tarball.
/// </summary>
public class PackageExtractor : IPackageExtractor
{
    private readonly ILogger<PackageExtractor> _logger;

    /// <summary>
    /// Conformance resource types that should be extracted from packages.
    /// </summary>
    private static readonly HashSet<string> ConformanceResourceTypes = new(StringComparer.Ordinal)
    {
        "StructureDefinition",
        "ValueSet",
        "CodeSystem",
        "ConceptMap",
        "SearchParameter",
        "OperationDefinition",
        "CapabilityStatement",
        "CompartmentDefinition",
        "ImplementationGuide",
        "GraphDefinition",
        "NamingSystem",
        "TerminologyCapabilities"
    };

    /// <summary>
    /// Initializes a new instance of the PackageExtractor class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public PackageExtractor(ILogger<PackageExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts resources from a package stream.
    /// </summary>
    /// <param name="packageStream">Stream containing the package .tgz file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with manifest and resources</returns>
    public async Task<PackageExtractionResult> ExtractAsync(
        Stream packageStream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageStream);
        if (!packageStream.CanRead)
            throw new ArgumentException("Package stream must be readable", nameof(packageStream));

        _logger.LogInformation("Starting package extraction");

        try
        {
            // Read entire stream into memory for processing
            var buffer = new byte[packageStream.Length];
            await packageStream.ReadExactlyAsync(buffer, 0, buffer.Length, cancellationToken);

            _logger.LogInformation("Read {Size} bytes from package stream", buffer.Length);

            PackageManifest? manifest = null;
            var resources = new List<ExtractedResource>();

            // Process tar entries
            using var memoryStream = new MemoryStream(buffer);
            using var gzipStream = new System.IO.Compression.GZipStream(memoryStream, System.IO.Compression.CompressionMode.Decompress);
            using var tarReader = new TarReader(gzipStream);

            TarEntry? entry;
            while ((entry = await tarReader.GetNextEntryAsync(copyData: false, cancellationToken)) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Extract package.json (manifest)
                if (entry.Name.EndsWith("package.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifest = await ExtractManifestAsync(entry, cancellationToken);
                    continue;
                }

                // Extract FHIR resource JSON files
                if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && entry.EntryType == TarEntryType.RegularFile)
                {
                    // Use manifest FHIR version if available, otherwise default to 4.0.1
                    var fhirVersion = manifest?.FhirVersion ?? "4.0.1";
                    var resource = await ExtractResourceAsync(entry, fhirVersion, cancellationToken);
                    if (resource != null)
                    {
                        resources.Add(resource);
                    }
                }
            }

            if (manifest == null)
            {
                throw new InvalidOperationException("Package does not contain a valid package.json file");
            }

            _logger.LogInformation(
                "Package extraction complete. Manifest: {PackageId}@{Version}. Resources: {Count}",
                manifest.Name, manifest.Version, resources.Count);

            // Log breakdown by resource type
            var byType = resources.GroupBy(r => r.ResourceType);
            foreach (var group in byType)
            {
                _logger.LogDebug(
                    "Extracted {Count} {ResourceType} resources",
                    group.Count(), group.Key);
            }

            return new PackageExtractionResult
            {
                Manifest = manifest,
                Resources = resources.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract package");
            throw;
        }
    }

    /// <summary>
    /// Extracts the package manifest (package.json).
    /// </summary>
    private async Task<PackageManifest?> ExtractManifestAsync(
        TarEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await ReadEntryContentAsync(entry, cancellationToken);
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            var name = root.GetProperty("name").GetString();
            var version = root.GetProperty("version").GetString();
            var fhirVersion = root.TryGetProperty("fhirVersion", out var fv)
                ? fv.GetString()
                : "4.0.1"; // Default to R4

            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;
            var description = root.TryGetProperty("description", out var d) ? d.GetString() : null;
            var license = root.TryGetProperty("license", out var l) ? l.GetString() : null;

            return new PackageManifest
            {
                Name = name!,
                Version = version!,
                FhirVersion = fhirVersion!,
                Title = title,
                Description = description,
                License = license
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract package manifest");
            throw;
        }
    }

    /// <summary>
    /// Extracts a FHIR resource from a tar entry if it's a conformance resource.
    /// </summary>
    /// <param name="entry">Tar entry containing the resource</param>
    /// <param name="fhirVersion">FHIR version from package manifest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task<ExtractedResource?> ExtractResourceAsync(
        TarEntry entry,
        string fhirVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            var content = await ReadEntryContentAsync(entry, cancellationToken);
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;

            // Get resource type
            if (!root.TryGetProperty("resourceType", out var rtElement))
                return null;

            var resourceType = rtElement.GetString();
            if (string.IsNullOrEmpty(resourceType) || !IsConformanceResource(resourceType))
                return null;

            // Get canonical URL
            if (!root.TryGetProperty("url", out var urlElement))
                return null;

            var canonical = urlElement.GetString();
            if (string.IsNullOrEmpty(canonical))
                return null;

            // Get optional version
            string? version = null;
            if (root.TryGetProperty("version", out var vElement))
            {
                version = vElement.GetString();
            }

            // Get resource ID
            var resourceId = root.TryGetProperty("id", out var idElement)
                ? idElement.GetString()
                : null;

            if (string.IsNullOrEmpty(resourceId))
                return null;

            return new ExtractedResource
            {
                ResourceType = resourceType,
                Canonical = canonical,
                Version = version,
                ResourceId = resourceId,
                ResourceJson = content,
                FhirVersion = fhirVersion
            };
        }
        catch (JsonException ex)
        {
            // Log but continue - some JSON files may not be FHIR resources
            _logger.LogDebug(ex, "Skipping entry {EntryName} - not valid JSON or not a conformance resource", entry.Name);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting resource from entry {EntryName}", entry.Name);
            return null;
        }
    }

    /// <summary>
    /// Reads the complete content of a tar entry.
    /// </summary>
    private static async Task<string> ReadEntryContentAsync(
        TarEntry entry,
        CancellationToken cancellationToken)
    {
        using var entryStream = new MemoryStream();
        if (entry.DataStream != null)
        {
            await entry.DataStream.CopyToAsync(entryStream, cancellationToken);
        }
        entryStream.Position = 0;

        using var reader = new StreamReader(entryStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if a resource type is a conformance resource that should be extracted.
    /// </summary>
    private static bool IsConformanceResource(string resourceType)
    {
        return ConformanceResourceTypes.Contains(resourceType);
    }
}
