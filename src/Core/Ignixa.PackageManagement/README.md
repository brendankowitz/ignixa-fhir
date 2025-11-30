# Ignixa.PackageManagement

NPM package management for FHIR Implementation Guides. Handles downloading, caching, and loading FHIR packages from NPM registries like packages.fhir.org.

## Why Use This Package?

- **Download FHIR packages**: Fetch Implementation Guides and core spec packages from NPM registries
- **Local caching**: Automatically cache downloaded packages to avoid re-downloads
- **Multiple sources**: Support for NPM registry, embedded packages, and custom loaders
- **Resource extraction**: Extract StructureDefinitions, ValueSets, and other conformance resources from packages

## Installation

```bash
dotnet add package Ignixa.PackageManagement
```

## Quick Start

### Loading a Package from NPM

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Microsoft.Extensions.Logging;

// Create HTTP client and logger
var httpClient = new HttpClient();
var logger = loggerFactory.CreateLogger<NpmPackageLoader>();

// Create package loader
var loader = new NpmPackageLoader(httpClient, logger);

// Download a package
var packageStream = await loader.LoadAsync(
    "hl7.fhir.us.core",
    "5.0.1",
    cancellationToken);

// packageStream contains the .tgz file
```

### Using Package Cache

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Set up cache manager
var cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "fhir-packages");
var cacheManager = new PackageCacheManager(cacheDirectory);

// Create loader with caching
var loader = new NpmPackageLoader(
    httpClient,
    cacheManager,
    options: null,
    logger);

// First call downloads from NPM
var stream1 = await loader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);

// Second call uses cached version (instant)
var stream2 = await loader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);
```

### Custom Registry Options

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Use a custom NPM registry
var options = new NpmPackageLoaderOptions
{
    RegistryUrl = "https://my-custom-registry.org/"
};

var loader = new NpmPackageLoader(httpClient, cacheManager, options, logger);
```

### Extracting Package Contents

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Extract package to directory
var extractor = new PackageExtractor();
var packageDirectory = await extractor.ExtractAsync(
    packageStream,
    "path/to/extract",
    cancellationToken);

// packageDirectory contains:
// - package.json (package metadata)
// - package/ directory with resources
```

### Loading Resources from Package

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Abstractions;

// Create resource provider
var resourceProvider = new PackageResourceProvider(
    packageDirectory,
    logger);

// Get all StructureDefinitions
var structureDefinitions = resourceProvider.GetResources("StructureDefinition");

foreach (var sd in structureDefinitions)
{
    Console.WriteLine($"Loaded {sd.Name}: {sd.Url}");
}
```

## Common Package IDs

```csharp
// US Core Implementation Guides
"hl7.fhir.us.core"     - US Core IG (v5.0.1, v6.1.0, etc.)

// International Patient Summary
"hl7.fhir.uv.ips"      - IPS IG

// Other Common IGs
"hl7.fhir.us.carin-bb" - CARIN Blue Button
"hl7.fhir.us.davinci-pdex" - Da Vinci Payer Data Exchange
```

## Advanced Usage

### Composite Package Loader

Load from multiple sources with fallback:

```csharp
using Ignixa.PackageManagement.Infrastructure;

// Combine embedded + NPM loaders
var embeddedLoader = new EmbeddedPackageLoader();
var npmLoader = new NpmPackageLoader(httpClient, cacheManager, options, logger);

var compositeLoader = new CompositePackageLoader(
    embeddedLoader,  // Try embedded first (fast)
    npmLoader        // Fall back to NPM (slow)
);

// Will check embedded first, then NPM
var stream = await compositeLoader.LoadAsync("hl7.fhir.r4.core", "4.0.1", cancellationToken);
```

### Searching for Packages

```csharp
using Ignixa.PackageManagement.Infrastructure;

var searchService = new NpmPackageSearchService(httpClient, logger);

// Search for packages
var results = await searchService.SearchAsync("us-core", cancellationToken);

foreach (var result in results)
{
    Console.WriteLine($"{result.Name} - {result.Description}");
}

// Get package metadata
var metadata = await searchService.GetPackageMetadataAsync(
    "hl7.fhir.us.core",
    cancellationToken);

Console.WriteLine($"Latest version: {metadata.DistTags.Latest}");
```

## Integration with Other Packages

This package is often used together with:

- **Ignixa.Specification**: Load packages to build custom schema providers
- **Ignixa.Validation**: Load profiles for validation
- **Ignixa.Search**: Load custom SearchParameter definitions

```csharp
// Example: Load US Core for validation
var loader = new NpmPackageLoader(httpClient, logger);
var stream = await loader.LoadAsync("hl7.fhir.us.core", "5.0.1", cancellationToken);

var extractor = new PackageExtractor();
var packageDir = await extractor.ExtractAsync(stream, tempPath, cancellationToken);

var resourceProvider = new PackageResourceProvider(packageDir, logger);
var profiles = resourceProvider.GetResources("StructureDefinition");

// Use profiles with Ignixa.Validation
```

## Package Cache Location

By default, packages are cached in:
- **Windows**: `%LOCALAPPDATA%\fhir-packages`
- **Linux/Mac**: `~/.local/share/fhir-packages`

You can override this by providing a custom `PackageCacheManager`.

## License

MIT License - see LICENSE file in repository root
