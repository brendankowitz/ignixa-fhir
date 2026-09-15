---
sidebar_position: 9
title: Package Management
description: FHIR package management and loading
---

# Ignixa.PackageManagement

Download, cache, and load FHIR implementation guide packages from NPM registries.

## Installation

```bash
dotnet add package Ignixa.PackageManagement
```

## Quick Start

```csharp
using Ignixa.PackageManagement;
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging;

// Create package loader with caching
var cacheManager = new PackageCacheManager("/var/cache/fhir-packages", logger);
var loader = new NpmPackageLoader(httpClient, cacheManager, null, logger);

// Download a package
var packageStream = await loader.DownloadPackageAsync("hl7.fhir.us.core", "6.1.0", cancellationToken);

// Extract resources from package
var extractor = new PackageExtractor(logger);
var result = await extractor.ExtractAsync(packageStream, cancellationToken);

// Access extracted resources
foreach (var resource in result.Resources)
{
    Console.WriteLine($"{resource.ResourceType}: {resource.Canonical}");
}
```

## Package Loading

### NPM Registry

The `NpmPackageLoader` downloads packages from configurable NPM registries (default: https://packages.simplifier.net).

```csharp
// Download a package from registry
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
var packageStream = await loader.DownloadPackageAsync(
    "hl7.fhir.us.core",
    "6.1.0",
    cancellationToken
);
```

### Composite Loading

Use `CompositePackageLoader` to try multiple loaders in sequence (e.g., Embedded → NPM):

```csharp
var embeddedLoader = new EmbeddedPackageLoader(embeddedPackages, logger);
var npmLoader = new NpmPackageLoader(httpClient, cacheManager, null, logger);

var compositeLoader = new CompositePackageLoader(
    logger,
    embeddedLoader,  // Try built-in packages first
    npmLoader        // Fall back to NPM registry
);

var packageStream = await compositeLoader.DownloadPackageAsync(
    "hl7.fhir.us.core",
    "6.1.0",
    cancellationToken
);
```

## Package Discovery

### Search for Packages

```csharp
var searchService = new NpmPackageSearchService(httpClient, options, logger);

var results = await searchService.SearchPackagesAsync(
    "us core",
    maxResults: 10,
    cancellationToken
);

foreach (var result in results)
{
    Console.WriteLine($"Package: {result.PackageId}");
    Console.WriteLine($"  Latest: {result.LatestVersion}");
    Console.WriteLine($"  Description: {result.Description}");
    Console.WriteLine($"  Relevance: {result.RelevanceScore}%");
}
```

### Search Result

```csharp
public record PackageSearchResult
{
    // Package ID (e.g., "hl7.fhir.us.core")
    public required string PackageId { get; init; }
    
    // Package description
    public string? Description { get; init; }
    
    // FHIR version(s)
    public string? FhirVersion { get; init; }
    
    // Latest version
    public string? LatestVersion { get; init; }
    
    // Search relevance score (0-100)
    public int RelevanceScore { get; init; }
}
```

## Resource Extraction

### Extract from Package Stream

```csharp
var extractor = new PackageExtractor(logger);
var extractionResult = await extractor.ExtractAsync(packageStream, cancellationToken);

var manifest = extractionResult.Manifest;
var resources = extractionResult.Resources;

Console.WriteLine($"Package: {manifest.Name}@{manifest.Version}");
Console.WriteLine($"FHIR Version: {manifest.FhirVersion}");
Console.WriteLine($"Resources: {resources.Count}");
```

### Extracted Resources

```csharp
public record ExtractedResource
{
    // Resource type (e.g., "StructureDefinition")
    public required string ResourceType { get; init; }
    
    // Canonical URL
    public required string Canonical { get; init; }
    
    // Resource version
    public string? Version { get; init; }
    
    // Resource ID
    public required string ResourceId { get; init; }
    
    // Full JSON
    public required string ResourceJson { get; init; }
    
    // FHIR version
    public required string FhirVersion { get; init; }
}
```

### Strict, Bounded Extraction (Opt-in)

`PackageExtractor.ExtractStrictAsync(Stream, PackageExtractionLimits, CancellationToken)`
is additive. It does not change `IPackageExtractor`, its permissive `ExtractAsync` method,
or existing result models and callers.

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;

var extractor = new PackageExtractor(loggerFactory.CreateLogger<PackageExtractor>());
var limits = new PackageExtractionLimits(
    maxCompressedBytes: 32 * 1024 * 1024,
    maxExpandedBytes: 256 * 1024 * 1024,
    maxEntryBytes: 16 * 1024 * 1024,
    maxArchiveEntries: 10_000,
    maxJsonDepth: 64);

StrictPackageExtractionResult result =
    await extractor.ExtractStrictAsync(packageStream, limits, cancellationToken);

int searchParameters = result.JsonEntries.Count(e => e.ResourceType == "SearchParameter");
int otherResources = result.JsonEntries.Count(e =>
    e.ResourceType is not null && e.ResourceType != "SearchParameter");
int metadataFiles = result.JsonEntries.Count(e => e.ResourceType is null);
```

The host selects and validates the resource types it imports; the extractor does not activate
FHIR resources, validate SearchParameter identity, or execute NPM scripts.

#### Limits, counting and stream lifetime

All five constructor arguments are independently configurable positive `int` maxima, immutable
after construction. Equality with a maximum is accepted; one over fails the entire operation.
The defaults align with the initial OSS policy; deployments must still validate package fit
and memory budgets:

| Limit | Default | Counting rule |
|---|---:|---|
| `MaxCompressedBytes` | 32 MiB (33,554,432 bytes) | Actual bytes read from current input position through EOF, including the gzip envelope |
| `MaxExpandedBytes` | 256 MiB (268,435,456 bytes) | All decompressed tar bytes: headers, metadata, ignored files, padding and end/record blocks |
| `MaxEntryBytes` | 16 MiB (16,777,216 bytes) | Every physical entry's payload, including hidden metadata and non-JSON files |
| `MaxArchiveEntries` | 10,000 | Physical nonzero tar headers, including local Pax/GNU metadata, directories and ignored files |
| `MaxJsonDepth` | 64 | Object/array nesting for each JSON file, including the manifest and metadata; outer container is depth one |

Byte limits use positive `int` values because this API buffers archives in memory. An OSS byte
policy represented as `long` must be validated within `1..Int32.MaxValue` and converted with
`checked((int)value)` at the host boundary. Reject values above `Int32.MaxValue` (2,147,483,647)
as invalid configuration; do not truncate, clamp or use unchecked casts. Policy conversion
belongs to the caller, not to a new overload of this extraction API.

`Statistics` reports actual `CompressedBytes`, `ExpandedBytes`, `PayloadBytes`,
`PhysicalEntries` and `LogicalEntries`. Payload bytes sum all physical entry bodies, excluding
their headers/padding. Logical entries are the resolved files/directories returned by the tar
reader; they include the manifest and ignored non-JSON files, but exclude hidden metadata
headers. Physical count therefore also bounds logical count.

The caller owns the stream. Reading is asynchronous, cancellation-aware, starts at its current
position, and never queries its `Length` or `Position`; non-seekable streams are supported.
The input remains open on success, validation failure and cancellation. Compressed reads stop
at most one byte beyond the configured maximum (needed to distinguish exact-limit EOF).
Cancellation is checked during input reads, inflation, framing validation, archive traversal,
entry reads, JSON token validation and before returning.
Pending input reads receive the cancellation token; the caller's stream must honor it.
Synchronous CPU/library operations are cooperatively cancellable between checkpoints, not
preemptible mid-call. This API does not impose an acquisition deadline.

Extraction is **in memory**, not streaming delivery of partially trusted resources. Compressed
and expanded buffers, individual JSON payloads, parser state and returned strings can coexist.
Byte limits are not peak-memory limits. Budget concurrency and heap headroom separately;
very large archives may require different limits or a different delivery strategy.
No extracted paths are written to disk.

#### Raw entries and manifest metadata

The result contains a `StrictPackageManifest`, `IReadOnlyList<StrictPackageEntry> JsonEntries`,
and `PackageExtractionStatistics`. Each entry has `Path`, original UTF-8-decoded `Json`, and
nullable `ResourceType`. Every non-manifest `.json` file (case-insensitive extension) is retained:

- A nonempty string `resourceType` identifies a resource candidate, including types outside
  the legacy conformance allowlist. Unknown resource-type strings remain for host validation.
- An absent `resourceType`, or a JSON root other than an object, denotes metadata JSON.
  JSON null/arrays/scalars are therefore retained as metadata, not counted as FHIR resources.
- A present non-string/blank `resourceType`, malformed JSON, invalid Unicode or duplicate JSON
  properties is an explicit failure. No entry is silently skipped for a JSON error.
- Canonical URL, `id`, resource version and other fields remain in the original JSON.
  Missing/invalid SearchParameter identity is never dropped or replaced by fabricated values.

Exactly one manifest at **`package/package.json`** is required, independent of tar order.
`Name` and `Version` must be nonblank strings; this is not a SemVer/source identity validator.
`FhirVersion` preserves singular `fhirVersion` or null when absent. `FhirVersions` preserves
the plural string array, in declared order, or an empty list when absent. Both can coexist;
conflicts and compatibility with a running FHIR version are host policy. `Json` retains the
entire manifest, including dependencies and the distinction between absent and empty arrays.
There is **no strict version fallback**. The unchanged permissive API still defaults missing
singular metadata to R4 `4.0.1`; callers opting into strict mode must choose any compatibility
fallback explicitly, rather than assigning resources a version based on archive ordering.

These public records are output DTOs. Direct construction does not perform extraction or FHIR
validation. The extractor supplies read-only collection backing; record equality does not
provide structural list equality, validated provenance or cryptographic artifact identity.

#### Archive and path policy

Supported archives contain one complete gzip member with verified header (including optional
header CRC), deflate termination, payload CRC and size footer. Missing/truncated data, another
gzip member, or any compressed trailing bytes fail. Tar requires two complete zero end blocks;
additional complete zero-filled record blocks are accepted and counted. Nonzero entry padding,
incomplete blocks, nonzero trailing data, and additional archives after the end marker fail.

Regular V7/USTAR/GNU/Pax files and directories are supported. Before the BCL `TarReader`
processes hidden metadata, a bounded framing gate validates physical checksums, sizes, counts,
payload/padding boundaries and supported types. Local Pax attributes and GNU long-path
metadata are allowed; a metadata header must precede a file/directory, not another metadata
header or EOF. Global Pax, sparse files, GNU long-link metadata, links, devices, FIFOs and
other special types are rejected. Pax `size`, `linkpath`, `hdrcharset`, `GNU.sparse*` and
`SCHILY.*` attributes are deliberately unsupported so they cannot change the validated layout.
Duplicate or malformed Pax attributes fail. Embedded LF, CR or NUL within an attribute is
rejected because supported BCL runtimes parse record boundaries differently. Numeric sizes
accept octal digits with optional leading spaces and trailing NUL/space padding, or positive
GNU base-256; digits after a NUL terminator, negative/overflowing binary sizes and binary
checksums are rejected. GNU long paths may include or omit their final NUL, with the actual
physical payload size counting toward limits in either case.

Opaque metadata-header names are not extraction paths. A narrow framing map preserves the
effective UTF-8 wire name, including USTAR prefixes and local Pax/GNU path overrides, without
trimming spaces. NUL-terminated name fields cannot hide nonzero bytes after the terminator.
The effective spelling is path-validated before BCL name normalization. Every file/directory
returned by `TarReader` must match the mapped physical data offset, payload size, type and
exact effective name; early termination, skipped entries, extra entries or disagreement fail.
This reconciliation includes ignored non-JSON files as well as retained JSON and manifests.

All logical paths must use `/` separators under the exact `package` root. Directories may have
one trailing slash, which is removed for identity comparisons. Empty/traversal/dot segments,
rooted/drive/UNC paths, backslashes, control characters, colons, trailing segment dots/spaces,
and invalid UTF-8 replacement characters are rejected. No URL decoding or Unicode
normalization is performed. Duplicate full paths are compared ordinally, case-insensitively;
file/directory and file/ancestor collisions also fail. Directory ancestors may be implicit.
Misplaced/nested manifests whose leaf is exactly `package.json` (case-insensitively), including
cased canonical-path aliases, fail instead of using last-one-wins. Longer leaves such as
`SearchParameter-package.json` and `notpackage.json` are ordinary JSON entries, not manifests.

#### Failure contract

`PackageExtractionException : IOException` carries a `PackageExtractionDiagnostic`:
`Code`, optional one-based **physical** `EntryIndex`, and optional configured `Limit`.
Indices always include hidden metadata headers. Resolved-path/JSON validation errors identify
the file/directory's data header. Metadata decoding failures identify the metadata header carrying
the malformed bytes; for example, invalid UTF-8 in GNU long-path metadata yields `UnsafePath`
at that metadata header. Framing errors identify the physical header being validated.
Reader disagreement identifies the expected physical entry. An incomplete/trailing
archive can identify the next expected index. Archive-wide failures may omit the index. Codes are:

- `CompressedSizeLimit`, `ExpandedSizeLimit`, `EntrySizeLimit`, `EntryCountLimit`, `JsonDepthLimit`
- `InvalidArchive`, `UnsafePath`, `DuplicatePath`, `UnsupportedEntryType`
- `MissingManifest`, `DuplicateManifest`, `InvalidManifest`, `InvalidJson`

Messages/diagnostics contain no package JSON, attacker-controlled filenames or parser messages,
and this strict path does not log package content. Failure never returns a partial result.
Cancellation remains `OperationCanceledException`; null/unreadable input or null limits are
argument errors. Underlying caller-stream I/O failures propagate rather than being disguised
as archive validation success.

This API establishes a generic extraction boundary, not source trust, cryptographic artifact
identity, acquisition/registry behavior, installation state or server ownership/activation.

## Caching

### Configure Cache Location

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://packages.simplifier.net",
    RequestTimeout = TimeSpan.FromSeconds(30),
    EnableRetryPolicies = true
};

var cacheManager = new PackageCacheManager("/var/cache/fhir-packages", logger);
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Cache Manager

```csharp
var cacheManager = new PackageCacheManager(cacheDirectory, logger);

// Get cache path for a package
var cachePath = cacheManager.GetCachePath("hl7.fhir.us.core", "6.1.0");

// Check if package is cached
bool isCached = cacheManager.IsCached("hl7.fhir.us.core", "6.1.0");

// Delete cached package
cacheManager.DeleteCachedPackage("hl7.fhir.us.core", "6.1.0");

// Clear entire cache
cacheManager.ClearCache();
```

## Offline Mode

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://packages.simplifier.net",
    EnableRetryPolicies = false
};

var cacheManager = new PackageCacheManager(offlineCacheDirectory, logger);

// Use only cached packages - no network requests
var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

## Registry Configuration

### Custom Registry

```csharp
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://my-internal-registry.example.org"
};

var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Retry Policies

```csharp
// Built-in resilience policies for transient failures
var retryPolicy = PackageLoaderResiliencePolicies.CreateRetryPolicy(logger);
var circuitBreakerPolicy = PackageLoaderResiliencePolicies.CreateCircuitBreakerPolicy(logger);

// Configure resilient HTTP handler
var handler = new ResilientHttpMessageHandler(new HttpClientHandler(), logger);
var httpClient = new HttpClient(handler);
```

## Package Manifest

### PackageManifest Record

```csharp
public record PackageManifest
{
    // Package name (e.g., "hl7.fhir.us.core")
    public required string Name { get; init; }
    
    // Package version (e.g., "5.0.1")
    public required string Version { get; init; }
    
    // FHIR version (e.g., "4.0.1")
    public required string FhirVersion { get; init; }
    
    // Package title
    public string? Title { get; init; }
    
    // Package description
    public string? Description { get; init; }
    
    // License
    public string? License { get; init; }
}
```

## Known Packages

```csharp
// Core FHIR packages (pre-compiled, should not load at runtime)
if (KnownPackages.IsCorePackage("hl7.fhir.r4.core"))
{
    // Skip loading - use embedded Ignixa.Specification instead
}

// Examples of core packages:
// - hl7.fhir.r2.core
// - hl7.fhir.r3.core
// - hl7.fhir.r4.core
// - hl7.fhir.r4b.core
// - hl7.fhir.r5.core
```

## Related Documentation

- [Validation](/docs/core-sdk/validation)
- [Core SDK Overview](/docs/core-sdk/overview)
