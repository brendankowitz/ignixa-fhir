---
sidebar_position: 8
title: Package Management
description: FHIR package management and loading
---

# Ignixa.PackageManagement

Download, cache, and load FHIR implementation guide packages.

## Installation

```bash
dotnet add package Ignixa.PackageManagement
```

## Quick Start

```csharp
using Ignixa.PackageManagement;

// Create package manager
var packageManager = new FhirPackageManager();

// Install a package
await packageManager.InstallAsync("hl7.fhir.us.core", "6.1.0");

// Load resources
var profiles = packageManager.GetResources("StructureDefinition");
```

## Package Installation

### From Registry

```csharp
// Install from packages.fhir.org
await packageManager.InstallAsync("hl7.fhir.us.core", "6.1.0");

// Install latest version
await packageManager.InstallAsync("hl7.fhir.us.core");

// Install with dependencies
await packageManager.InstallAsync("hl7.fhir.us.core", "6.1.0", includeDependencies: true);
```

### From File

```csharp
// Install from local tgz file
await packageManager.InstallFromFileAsync("./my-package.tgz");
```

### From URL

```csharp
// Install from URL
await packageManager.InstallFromUrlAsync("https://packages.simplifier.net/hl7.fhir.us.core/6.1.0");
```

## Package Discovery

### List Installed

```csharp
var installed = packageManager.GetInstalledPackages();

foreach (var pkg in installed)
{
    Console.WriteLine($"{pkg.Name}@{pkg.Version}");
}
```

### Search Registry

```csharp
var results = await packageManager.SearchAsync("us core");

foreach (var pkg in results)
{
    Console.WriteLine($"{pkg.Name}: {pkg.Description}");
}
```

### Get Package Info

```csharp
var info = await packageManager.GetPackageInfoAsync("hl7.fhir.us.core");

Console.WriteLine($"Latest: {info.LatestVersion}");
Console.WriteLine($"Versions: {string.Join(", ", info.Versions)}");
```

## Resource Loading

### Get All Resources

```csharp
// Get all resources from a package
var resources = packageManager.GetResources("hl7.fhir.us.core");
```

### Get By Type

```csharp
// Get StructureDefinitions
var profiles = packageManager.GetResources("hl7.fhir.us.core", "StructureDefinition");

// Get ValueSets
var valueSets = packageManager.GetResources("hl7.fhir.us.core", "ValueSet");
```

### Get By URL

```csharp
// Get specific resource by canonical URL
var profile = packageManager.GetResourceByUrl(
    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
);
```

### Search Resources

```csharp
var results = packageManager.SearchResources(
    resourceType: "StructureDefinition",
    filter: r => r["type"]?.Text == "Patient"
);
```

## Caching

### Cache Location

```csharp
var options = new PackageManagerOptions
{
    CacheDirectory = "/var/cache/fhir-packages",
    MaxCacheSize = 1_000_000_000, // 1GB
    CacheExpiration = TimeSpan.FromDays(30)
};

var packageManager = new FhirPackageManager(options);
```

### Clear Cache

```csharp
// Clear all cached packages
packageManager.ClearCache();

// Clear specific package
packageManager.ClearCache("hl7.fhir.us.core");
```

## Dependency Management

### View Dependencies

```csharp
var deps = await packageManager.GetDependenciesAsync("hl7.fhir.us.core", "6.1.0");

foreach (var dep in deps)
{
    Console.WriteLine($"  {dep.Name}@{dep.Version}");
}
```

### Install Tree

```csharp
// Install with full dependency tree
await packageManager.InstallAsync(
    "hl7.fhir.us.core", 
    "6.1.0", 
    includeDependencies: true,
    skipOptional: true
);
```

## Package Manifest

### Read Manifest

```csharp
var manifest = await packageManager.GetManifestAsync("hl7.fhir.us.core");

Console.WriteLine($"Name: {manifest.Name}");
Console.WriteLine($"Title: {manifest.Title}");
Console.WriteLine($"FHIR Version: {manifest.FhirVersion}");
Console.WriteLine($"Author: {manifest.Author}");
```

### Manifest Properties

```csharp
public class PackageManifest
{
    public string Name { get; }
    public string Version { get; }
    public string Title { get; }
    public string Description { get; }
    public string FhirVersion { get; }
    public string Author { get; }
    public string Url { get; }
    public IReadOnlyList<PackageDependency> Dependencies { get; }
}
```

## Integration with Validation

```csharp
// Load profiles for validation
var packageManager = new FhirPackageManager();
await packageManager.InstallAsync("hl7.fhir.us.core", "6.1.0");

var profiles = packageManager.GetResources("StructureDefinition");

// Create validator with loaded profiles
var resolver = new PackageProfileResolver(packageManager);
var validator = new FhirValidator(options, resolver);

// Validate against US Core Patient
var outcome = await validator.ValidateAsync(
    patient,
    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
);
```

## Offline Mode

```csharp
var options = new PackageManagerOptions
{
    OfflineMode = true,
    CacheDirectory = "/var/cache/fhir-packages"
};

// Only uses cached packages, no network requests
var packageManager = new FhirPackageManager(options);
```

## Registry Configuration

### Custom Registry

```csharp
var options = new PackageManagerOptions
{
    Registries = new[]
    {
        "https://packages.fhir.org",
        "https://packages.simplifier.net",
        "https://my-internal-registry.example.org"
    }
};
```

### Authentication

```csharp
var options = new PackageManagerOptions
{
    RegistryCredentials = new Dictionary<string, string>
    {
        ["https://my-internal-registry.example.org"] = "Bearer my-token"
    }
};
```

## Related Documentation

- [Validation](/docs/core-sdk/validation)
- [Core SDK Overview](/docs/core-sdk/overview)
