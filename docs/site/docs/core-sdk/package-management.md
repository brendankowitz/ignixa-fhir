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
