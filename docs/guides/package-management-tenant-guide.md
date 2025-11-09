# Tenant-Aware Package Management Guide

## Overview

FHIR packages (Implementation Guides) contain conformance resources like StructureDefinitions, ValueSets, and CodeSystems. The package management system allows tenants to load and manage FHIR packages with efficient shared storage.

**Key Design Principle**: Packages are stored globally for efficiency (US Core 6.1.0 is identical for all tenants), but the loading/unloading operations are tenant-aware for management flexibility.

---

## Architecture

### Storage Model

```
Database Structure:
├── PackageResource (global, shared across all tenants)
│   ├── PackageId: "hl7.fhir.us.core"
│   ├── PackageVersion: "6.1.0"
│   ├── Canonical: "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
│   ├── ResourceJson: { "resourceType": "StructureDefinition", ... }
│   └── IsActive: true
│
└── Tenant-Specific Configuration (in TenantConfiguration)
    ├── TenantId: 1
    ├── DisplayName: "Mayo Clinic"
    └── Packages: ["hl7.fhir.us.core@6.1.0"]  # Which packages this tenant uses
```

### Resolution Fallback Chain

When a validation engine needs a StructureDefinition like `http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient`:

```
1. Check Cache (L1 In-Memory)
   └─ Cache key: "conformance:{tenantId}:{canonical}|{version}"
   └─ Fast path for frequently used resources

2. Check Tenant Resources (Database - Resource table WHERE TenantId={id})
   └─ Tenant-specific customizations or uploads
   └─ Overrides standard packages

3. Check Package Resources (Database - PackageResource table)
   └─ Standard resources from loaded packages (shared across tenants)
   └─ US Core, mCODE, etc.

4. External Registry Fallback
   └─ Query tx.fhir.org if resource not found locally
   └─ Requires network access
```

### Phase 1 Limitation

In Phase 1, all tenants share the same `PackageResource` table. This is **by design** because:

- ✅ FHIR packages are **immutable** - US Core 6.1.0 is identical regardless of tenant
- ✅ Reduces storage: 100 tenants × 5MB package = 500MB → 5MB with shared storage
- ✅ Reduces bandwidth: Package downloaded once, imported once
- ✅ Tenants still control **which packages** they load via TenantConfiguration

**Phase 2 Enhancement**: IFhirRepository will be extended with a PackageResources property for proper per-tenant scoping if needed.

---

## API Usage

### 1. Load a Package

Load a FHIR package (e.g., US Core 6.1.0) into a tenant's database.

**Request:**
```http
POST /tenant/1/admin/packages/load
Content-Type: application/json

{
  "packageId": "hl7.fhir.us.core",
  "version": "6.1.0"
}
```

**Response:**
```json
{
  "packageId": "hl7.fhir.us.core",
  "packageVersion": "6.1.0",
  "totalResources": 156,
  "importedResources": 156,
  "durationMilliseconds": 1250,
  "resourcesByType": {
    "StructureDefinition": 89,
    "ValueSet": 45,
    "CodeSystem": 15,
    "SearchParameter": 7
  }
}
```

**Processing Flow:**
1. Download package from `packages.fhir.org/{packageId}/{version}`
2. Check local cache first (prevents re-downloading)
3. Extract conformance resources from .tgz archive
4. Filter to 12 supported types (StructureDefinition, ValueSet, etc.)
5. Batch upsert to database (500 resources per transaction)
6. Cache the downloaded package locally for future use

**Error Handling:**
- `400 Bad Request` - Invalid tenantId, packageId, or version
- `404 Not Found` - Package not found in NPM registry
- `500 Internal Server Error` - Database or extraction error

### 2. List Loaded Packages

List all packages currently loaded for a tenant.

**Request:**
```http
GET /tenant/1/admin/packages
```

**Response:**
```json
{
  "packages": [
    {
      "packageId": "hl7.fhir.us.core",
      "version": "6.1.0"
    },
    {
      "packageId": "hl7.fhir.us.mcode",
      "version": "3.0.0"
    }
  ],
  "count": 2
}
```

**Notes:**
- Phase 1: Returns all loaded packages (global list for all tenants)
- Phase 2: Will return only packages for the specific tenant

### 3. Unload a Package

Deactivate a package from a tenant's database (soft-delete via IsActive flag).

**Request:**
```http
DELETE /tenant/1/admin/packages/hl7.fhir.us.core/6.1.0
```

**Response:**
```json
{
  "packageId": "hl7.fhir.us.core",
  "version": "6.1.0",
  "resourcesDeactivated": 156
}
```

**Notes:**
- Marks resources as `IsActive = false` (soft delete)
- Packages can be reactivated by loading again
- Actual deletion requires separate administrative operation
- Returns `404 Not Found` if package not in database

---

## Configuration

### Tenant Package Configuration

Specify which packages a tenant uses in `TenantConfiguration`:

```json
{
  "Tenants": {
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Mayo Clinic",
        "Packages": [
          {
            "PackageId": "hl7.fhir.us.core",
            "Version": "6.1.0"
          },
          {
            "PackageId": "hl7.fhir.us.mcode",
            "Version": "3.0.0"
          }
        ]
      }
    ]
  }
}
```

### Package Cache Configuration

Configure local package caching in `Program.cs`:

```csharp
// Optional: Enable local package caching
var cacheDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "IgnixaFhir", "PackageCache");

var packageCacheManager = new PackageCacheManager(cacheDirectory, logger);
builder.Services.AddSingleton(packageCacheManager);

// Register package loader with cache support
builder.Services.AddHttpClient<NpmPackageLoader>((provider, client) =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
})
.ConfigureHttpClient(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("IgnixaFhir/1.0");
});
```

---

## Implementation Details

### Package Loading Pipeline

```csharp
// Flow: HTTP Request → Handler → Provider → Loader → Extractor → Importer

// 1. HTTP Endpoint (AdminPackageEndpoints.cs)
POST /tenant/{tenantId}/admin/packages/load
    ↓
// 2. Medino Handler (LoadPackageHandler.cs)
LoadPackageHandler.HandleAsync(command)
    ↓
// 3. Orchestration (ImplementationGuideProvider.cs)
IImplementationGuideProvider.LoadPackageAsync(tenantId, packageId, version)
    ├─ Step 1: IPackageLoader.DownloadPackageAsync()
    │   └─ NpmPackageLoader downloads from packages.fhir.org
    │   └─ Checks local cache first (PackageCacheManager)
    │
    ├─ Step 2: IPackageExtractor.ExtractAsync()
    │   └─ PackageExtractor parses .tar.gz
    │   └─ Filters 12 conformance resource types
    │
    └─ Step 3: IPackageResourceImporter.ImportAsync()
        └─ PackageResourceImporter batch upserts to database
        └─ Chunks into 500-resource transactions
```

### Conformance Resource Resolution

The `IConformanceResourceResolver` implements the 4-tier fallback chain:

```csharp
public async Task<string?> ResolveAsync(
    string tenantId,
    string canonical,
    string? version = null,
    CancellationToken cancellationToken = default)
{
    // Step 1: Cache (IFhirConformanceCache)
    var cached = await _cache.GetAsync(tenantId, canonical, version, cancellationToken);
    if (cached != null) return cached;

    // Step 2: Database (IPackageResourceRepository)
    var packageResource = await _packageRepository.GetByCanonicalAsync(canonical, version, cancellationToken);
    if (packageResource?.IsActive == true)
    {
        await _cache.SetAsync(tenantId, canonical, packageResource.ResourceJson, cancellationToken: cancellationToken);
        return packageResource.ResourceJson;
    }

    // Step 3: Not found
    return null;
}
```

---

## Performance Considerations

### Caching

**L1 Cache (In-Memory)**
- TTL: 24 hours absolute, 1 hour sliding
- Key format: `conformance:{tenantId}:{canonical}|{version}`
- Updated on first database hit

**Local Disk Cache (Optional)**
- Prevents re-downloading packages
- Location: `%APPDATA%/IgnixaFhir/PackageCache` (Windows) or equivalent
- Filename: `{packageId}_{version}.tgz`

### Package Import Performance

- **Extraction**: ~100ms for US Core 6.1.0 (156 resources)
- **Batch Import**: ~1-2 seconds for 156 resources (500-resource chunks)
- **Total**: ~1.5-3 seconds per package load

### Storage Usage

```
PackageResource Table Size (approx):
- US Core 6.1.0: ~5 MB (156 resources)
- mCODE 3.0.0: ~3 MB (84 resources)
- ValueSet Repository: ~10 MB (500+ value sets)
─────────────
Total per shared package set: ~20 MB

Cost savings with shared storage:
- Single tenant: 20 MB
- 10 tenants (separate storage): 200 MB
- 10 tenants (shared storage): 20 MB ✅ 90% reduction
```

---

## Troubleshooting

### Package Not Found

**Error**: `Package 'hl7.fhir.us.core@6.1.0' not found in NPM registry`

**Causes**:
- Typo in packageId (e.g., `hl7.fhir.us-core` vs `hl7.fhir.us.core`)
- Invalid version (e.g., `6.1` instead of `6.1.0`)
- Package deleted from registry

**Solution**: Verify package exists at `https://packages.fhir.org/{packageId}/{version}`

### Cache Directory Permissions

**Error**: `Failed to cache package ... Permission denied`

**Cause**: Application doesn't have write access to cache directory

**Solution**:
- Ensure cache directory exists
- Grant read/write permissions to application account
- Or disable caching by not registering `PackageCacheManager`

### Memory Issues with Large Packages

**Symptom**: Out of memory when loading package

**Cause**: Entire .tgz file read into MemoryStream

**Mitigation**:
- Allocate additional memory to application
- Pre-download large packages offline
- Note: This is Phase 1; Phase 2 will implement streaming extraction

---

## Related Documentation

- [ADR-2532: FHIR Terminology Services Implementation Strategy](../investigations/ADR-2532-unified-validation-terminology-package-architecture.md)
- [ADR-2523: Multi-Tenancy Architecture](../investigations/ADR-2523-multi-tenancy.md)
- [FHIR Package Specification](https://github.com/FHIR/fhir-package-spec)
- [NPM Registry for FHIR](https://registry.fhir.org/)

---

## API Reference

### LoadPackageCommand
```csharp
public record LoadPackageCommand(
    string TenantId,
    string PackageId,
    string Version
) : IRequest<PackageImportResult>;
```

### ListPackagesQuery
```csharp
public record ListPackagesQuery(
    string TenantId
) : IRequest<ListPackagesResult>;
```

### UnloadPackageCommand
```csharp
public record UnloadPackageCommand(
    string TenantId,
    string PackageId,
    string Version
) : IRequest<UnloadPackageResult>;
```

### IConformanceResourceResolver
```csharp
public interface IConformanceResourceResolver
{
    Task<string?> ResolveAsync(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default);

    Task<string?> ResolveFromPackageAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string canonical,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string tenantId,
        string packageId,
        string packageVersion,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    Task InvalidateTenantCacheAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    Task InvalidateResourceAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default);
}
```

---

## Changelog

### Version 1.0 (Phase 1)
- ✅ Tenant-aware package loading API
- ✅ Global package repository with soft-delete
- ✅ Local disk caching with PackageCacheManager
- ✅ 4-tier conformance resource resolution
- ✅ In-memory caching with tenant-scoped keys
- ✅ Support for 12 conformance resource types

### Planned (Phase 2)
- 🔮 Per-tenant package storage via IFhirRepository.PackageResources
- 🔮 Redis distributed caching (L2)
- 🔮 Transitive dependency resolution
- 🔮 Package signature verification
- 🔮 Offline package loading from .tgz files
