# ADR 2532: Unified Validation, Terminology & Package Management Architecture

## Metadata

- **ADR Number**: 2532
- **Title**: Unified Validation, Terminology & Package Management Architecture
- **Status**: 📋 **PROPOSED** (2025-01-08)
- **Date**: 2025-01-08
- **Phase**: Future (Post Phase 22) - Coordinated Implementation
- **Implementation Priority**: HIGH
- **Estimated Total Effort**: 12-16 weeks (coordinated phases)
- **Related Documents**:
  - [ADR-2527: Comprehensive Validation System](ADR-2527-comprehensive-validation-system.md)
  - [ADR-2531: Terminology Services Implementation](ADR-2531-terminology-services-implementation.md)
  - [Multi-Version IG Loading System](multi-version-ig-loading-system.md)
  - [ADR-2500: Master Implementation Roadmap](ADR-2500-master-roadmap.md)

---

## Executive Summary

This ADR provides a **unified implementation strategy** for three interconnected systems that must work together:

1. **Package Management** - Load FHIR NPM packages (IGs, profiles, terminology)
2. **Validation System** - Validate resources against profiles and invariants
3. **Terminology Services** - Expand ValueSets, validate codes, translate mappings

### Why These Must Be Coordinated

These systems have **circular dependencies** and **shared infrastructure**:

```
┌──────────────────────────────────────────────────────────────┐
│                    FHIR NPM Package                          │
│  (US Core 5.0.1, contains profiles + ValueSets)             │
└──────────────────────────────────────────────────────────────┘
                    ↓ loaded by                    ↓ contains
        ┌───────────────────────┐      ┌───────────────────────┐
        │  Package Management   │      │   StructureDefinition │
        │  (IImplementationGuide│      │   (US Core Patient)   │
        │   Provider)           │      └───────────────────────┘
        └───────────────────────┘                  ↓ used by
                    ↓ provides profiles to         │
        ┌───────────────────────┐                  │
        │  Validation System    │ ←────────────────┘
        │  (IValidationSchema   │
        │   Resolver)           │ ──→ requires terminology
        └───────────────────────┘          ↓
                                ┌───────────────────────┐
                                │  Terminology Service  │
                                │  ($expand, $validate) │
                                └───────────────────────┘
                                          ↑
                                          │ needs CodeSystem/ValueSet
                                          │ from package
                                          ↓
                    ┌───────────────────────────────────┐
                    │  Package contains:                │
                    │  - ValueSet/administrative-gender │
                    │  - CodeSystem/us-core-race        │
                    └───────────────────────────────────┘
```

**Key Integration Points**:
- Package loading extracts StructureDefinitions → feeds Validation
- Package loading extracts CodeSystems/ValueSets → feeds Terminology
- Validation references terminology bindings → calls Terminology Service
- Validation requires profiles from packages → calls Package Management

### Recommended Phased Approach

| Phase | Duration | Focus | Dependencies |
|-------|----------|-------|--------------|
| **Phase 1: Foundation** | 3-4 weeks | Package loading infrastructure<br/>Terminology indexes | None |
| **Phase 2: Core Services** | 4-5 weeks | Basic validation (Tier 1+2)<br/>Basic terminology ($validate-code, $expand) | Phase 1 |
| **Phase 3: Integration** | 3-4 weeks | Package → Validation bridge<br/>Validation → Terminology bridge | Phase 2 |
| **Phase 4: Advanced** | 2-3 weeks | Profile validation (slicing, extensions)<br/>Advanced terminology ($translate, $subsumes) | Phase 3 |

**Total**: 12-16 weeks for complete implementation

---

## Context

### Current State Analysis

#### Package Management (Multi-Version IG Loading)
**Status**: Design complete, implementation pending
- ✅ Interface design: `IImplementationGuideProvider`, `IImplementationGuidePackageLoader`
- ✅ NPM package loading architecture
- ✅ Tenant-specific IG configuration
- ❌ No implementation exists
- ❌ No storage for extracted resources (StructureDefinition, ValueSet, CodeSystem)

#### Validation System (ADR-2527)
**Status**: Core implemented, profile validation pending
- ✅ Tier 1: Fast structural validation (JSON, required fields)
- ✅ Tier 2: Partial FHIR spec validation (cardinality, FHIRPath invariants)
- ✅ `IFhirValidationService` interface
- ⚠️ Terminology validation: Basic `ITerminologyService` with 10 hardcoded ValueSets
- ❌ Profile validation: No StructureDefinition-based validation
- ❌ No slicing validators
- ❌ No extension validators

#### Terminology Services (ADR-2531)
**Status**: Design complete, implementation pending
- ✅ Investigation complete (3 indexes vs. specialized tables)
- ✅ Performance benchmarks defined
- ❌ No operations implemented ($expand, $validate-code, $lookup, $translate, $subsumes)
- ❌ No terminology indexes
- ❌ No CodeSystem/ValueSet storage beyond Resource table

### Business Drivers

**Why Now?**
1. **US Core Compliance**: US Core profiles require terminology validation against specific ValueSets
2. **Quality Measures**: CMS quality reporting requires validated coded data
3. **Clinical Decision Support**: CDS Hooks require profile-conformant resources
4. **Interoperability**: Trading partners require IG-specific validation

**Use Case Examples**:

```json
// Scenario 1: Validate US Core Patient resource
POST /Patient
{
  "resourceType": "Patient",
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  },
  "extension": [{
    "url": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-race",
    "extension": [{
      "url": "ombCategory",
      "valueCoding": {
        "system": "urn:oid:2.16.840.1.113883.6.238",
        "code": "2106-3",
        "display": "White"
      }
    }]
  }],
  "identifier": [/* ... */],
  "name": [/* ... */]
}

// Server must:
1. Load US Core 5.0.1 package (if not cached)
2. Extract us-core-patient StructureDefinition
3. Validate resource against profile (extensions, slices, cardinality)
4. Validate race code against http://hl7.org/fhir/us/core/ValueSet/omb-race-category
5. Return HTTP 400 with OperationOutcome if invalid
```

```json
// Scenario 2: Expand ValueSet for UI dropdown
GET /ValueSet/$expand?url=http://hl7.org/fhir/us/core/ValueSet/us-core-medication-codes&count=100

// Server must:
1. Resolve ValueSet from US Core package
2. Parse compose.include rules (RxNorm + CVX + NDC)
3. Query CodeSystem.concept[] or Concept table
4. Apply filters
5. Return expansion with 100 codes
```

---

## Decision

Implement a **unified infrastructure** with shared caching, storage, and resolution layers:

### Unified Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Tenant Configuration Layer                       │
│  - Which IGs to load (US Core 5.0.1, mCODE 2.0.0)                  │
│  - Validation strictness (warn vs error)                            │
│  - Terminology fallback (local → external)                          │
└─────────────────────────────────────────────────────────────────────┘
                                ↓
┌─────────────────────────────────────────────────────────────────────┐
│                    Package Management Layer                          │
│                                                                      │
│  IImplementationGuideProvider                                       │
│  ├── LoadPackageAsync(url) → ImplementationGuidePackage            │
│  ├── ExtractResourcesAsync(package, "StructureDefinition")         │
│  └── ResolveProfileAsync(canonical) → StructureDefinition          │
│                                                                      │
│  Storage: PackageResource table (extracted resources)               │
│  Cache: PackageCache (in-memory, per-tenant)                        │
└─────────────────────────────────────────────────────────────────────┘
                    ↓ provides                  ↓ provides
        ┌──────────────────────┐   ┌──────────────────────────────┐
        │  Validation Layer    │   │  Terminology Layer           │
        │                      │   │                              │
        │  IValidationSchema   │   │  ITerminologyService         │
        │  Resolver            │   │  ├── ExpandValueSetAsync     │
        │  ├── GetSchema()     │   │  ├── ValidateCodeAsync       │
        │  │   (from package)  │   │  └── LookupCodeAsync         │
        │  │                   │   │                              │
        │  IAssertion[]        │   │  Storage: Concept,           │
        │  ├── Cardinality     │   │           ValueSetExpansion  │
        │  ├── FHIRPath        │   │  Cache: TerminologyCache     │
        │  ├── Binding ────────┼───→  (calls terminology)         │
        │  └── Slicing         │   │                              │
        └──────────────────────┘   └──────────────────────────────┘
```

### Shared Infrastructure Components

#### 1. Unified Resource Cache

**Purpose**: Single cache for all FHIR conformance resources (StructureDefinition, ValueSet, CodeSystem, ConceptMap)

```csharp
public interface IFhirConformanceCache
{
    // Generic resource caching
    ValueTask<T?> GetAsync<T>(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default)
        where T : Resource;

    ValueTask SetAsync<T>(
        string tenantId,
        string canonical,
        T resource,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
        where T : Resource;

    // Bulk operations for package loading
    ValueTask SetManyAsync<T>(
        string tenantId,
        IReadOnlyDictionary<string, T> resources,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
        where T : Resource;

    // Invalidation
    ValueTask InvalidateAsync(
        string tenantId,
        string canonical,
        CancellationToken cancellationToken = default);

    ValueTask InvalidateTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default);
}

// Implementation: Two-tier cache
public class TwoTierConformanceCache : IFhirConformanceCache
{
    private readonly IMemoryCache _l1Cache; // In-memory (fast)
    private readonly IDistributedCache _l2Cache; // Redis (shared across servers)
    private readonly IFhirRepository _repository; // Database (source of truth)

    public async ValueTask<T?> GetAsync<T>(
        string tenantId,
        string canonical,
        string? version,
        CancellationToken cancellationToken) where T : Resource
    {
        var key = BuildCacheKey(tenantId, canonical, version);

        // L1: Memory cache (fastest)
        if (_l1Cache.TryGetValue<T>(key, out var cachedResource))
            return cachedResource;

        // L2: Distributed cache (shared)
        var l2Json = await _l2Cache.GetStringAsync(key, cancellationToken);
        if (l2Json != null)
        {
            var resource = JsonSerializer.Deserialize<T>(l2Json);
            _l1Cache.Set(key, resource, TimeSpan.FromMinutes(30));
            return resource;
        }

        // L3: Database (source of truth)
        // Query PackageResource table or Resource table
        var dbResource = await _repository.GetConformanceResourceAsync<T>(
            tenantId, canonical, version, cancellationToken);

        if (dbResource != null)
        {
            // Populate caches
            await SetAsync(tenantId, canonical, dbResource, TimeSpan.FromHours(4), cancellationToken);
        }

        return dbResource;
    }
}
```

#### 2. Package Resource Storage

**Purpose**: Store extracted conformance resources from packages for fast retrieval

```sql
-- New table: PackageResource
CREATE TABLE dbo.PackageResource (
    PackageResourceId BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Package metadata
    PackageId NVARCHAR(256) NOT NULL,           -- "hl7.fhir.us.core"
    PackageVersion NVARCHAR(100) NOT NULL,      -- "5.0.1"

    -- Resource metadata
    ResourceType NVARCHAR(64) NOT NULL,         -- "StructureDefinition"
    Canonical NVARCHAR(512) NOT NULL,           -- "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
    Version NVARCHAR(100),                      -- "5.0.1"
    ResourceId NVARCHAR(64) NOT NULL,           -- "us-core-patient"

    -- Resource content
    ResourceJson NVARCHAR(MAX) NOT NULL,        -- Full JSON

    -- Indexing
    FhirVersion NVARCHAR(10) NOT NULL,          -- "R4"
    Kind NVARCHAR(50),                          -- "resource" for StructureDefinition

    -- Metadata
    LoadedDate DATETIMEOFFSET NOT NULL DEFAULT GETUTCDATE(),
    IsActive BIT NOT NULL DEFAULT 1,

    CONSTRAINT UQ_PackageResource_Canonical UNIQUE (PackageId, PackageVersion, Canonical)
);

-- Indexes for fast lookups
CREATE NONCLUSTERED INDEX IX_PackageResource_Canonical
ON dbo.PackageResource (Canonical, Version)
INCLUDE (ResourceJson, ResourceType);

CREATE NONCLUSTERED INDEX IX_PackageResource_Package
ON dbo.PackageResource (PackageId, PackageVersion, ResourceType);

CREATE NONCLUSTERED INDEX IX_PackageResource_Type_FhirVersion
ON dbo.PackageResource (ResourceType, FhirVersion)
INCLUDE (Canonical, ResourceId);
```

**Why a Separate Table?**
- ✅ Fast queries: No need to decompress RawResource from main Resource table
- ✅ Package versioning: Multiple versions of same profile can coexist
- ✅ Immutable: Package resources don't change (unlike tenant-created resources)
- ✅ Tenant-independent: Same package shared across tenants (saves storage)

#### 3. Conformance Resource Resolver (Unified)

**Purpose**: Single abstraction for resolving any conformance resource, with fallback chain

```csharp
public interface IConformanceResourceResolver
{
    /// <summary>
    /// Resolve any conformance resource by canonical URL
    /// </summary>
    ValueTask<T?> ResolveAsync<T>(
        string tenantId,
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default)
        where T : Resource;

    /// <summary>
    /// Resolve with fallback chain
    /// </summary>
    ValueTask<T?> ResolveWithFallbackAsync<T>(
        string tenantId,
        string canonical,
        string? version,
        ConformanceResolutionOptions options,
        CancellationToken cancellationToken = default)
        where T : Resource;
}

public record ConformanceResolutionOptions
{
    public bool AllowPackageResources { get; init; } = true;
    public bool AllowTenantResources { get; init; } = true;
    public bool AllowExternalRegistry { get; init; } = false;
    public IReadOnlyList<string>? PreferredPackages { get; init; }
}

public class ConformanceResourceResolver : IConformanceResourceResolver
{
    private readonly IFhirConformanceCache _cache;
    private readonly IFhirRepository _repository;
    private readonly IImplementationGuideProvider _packageProvider;
    private readonly ILogger<ConformanceResourceResolver> _logger;

    public async ValueTask<T?> ResolveWithFallbackAsync<T>(
        string tenantId,
        string canonical,
        string? version,
        ConformanceResolutionOptions options,
        CancellationToken cancellationToken) where T : Resource
    {
        // 1. Check cache first (all sources)
        var cached = await _cache.GetAsync<T>(tenantId, canonical, version, cancellationToken);
        if (cached != null) return cached;

        // 2. Try tenant-created resources (uploaded by user)
        if (options.AllowTenantResources)
        {
            var tenantResource = await _repository.GetByCanonicalAsync<T>(
                tenantId, canonical, version, cancellationToken);
            if (tenantResource != null)
            {
                await _cache.SetAsync(tenantId, canonical, tenantResource, cancellationToken: cancellationToken);
                return tenantResource;
            }
        }

        // 3. Try package resources (loaded from IGs)
        if (options.AllowPackageResources)
        {
            var packageResource = await ResolveFromPackagesAsync<T>(
                tenantId, canonical, version, options.PreferredPackages, cancellationToken);
            if (packageResource != null)
            {
                await _cache.SetAsync(tenantId, canonical, packageResource, cancellationToken: cancellationToken);
                return packageResource;
            }
        }

        // 4. Try external registry (packages.fhir.org)
        if (options.AllowExternalRegistry)
        {
            var externalResource = await ResolveFromExternalRegistryAsync<T>(
                canonical, version, cancellationToken);
            if (externalResource != null)
            {
                await _cache.SetAsync(tenantId, canonical, externalResource, cancellationToken: cancellationToken);
                return externalResource;
            }
        }

        _logger.LogWarning(
            "Failed to resolve {ResourceType} with canonical {Canonical} version {Version} for tenant {TenantId}",
            typeof(T).Name, canonical, version ?? "<latest>", tenantId);

        return null;
    }

    private async ValueTask<T?> ResolveFromPackagesAsync<T>(
        string tenantId,
        string canonical,
        string? version,
        IReadOnlyList<string>? preferredPackages,
        CancellationToken cancellationToken) where T : Resource
    {
        // Query PackageResource table
        // If preferredPackages specified, prioritize those
        // Otherwise, return latest version across all packages

        // Implementation: SQL query or Entity Framework
        return null; // Placeholder
    }
}
```

---

## Multi-Version Package Support & Resolution

### Supporting Multiple Package Versions (US Core 6 & 7 Coexisting)

The `PackageResource` table supports multiple versions of the same package through its composite key:

```sql
-- Both versions coexist in the table
PackageResource:
  (PackageId='hl7.fhir.us.core', PackageVersion='6.0.0',
   Canonical='http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient')

  (PackageId='hl7.fhir.us.core', PackageVersion='7.0.0',
   Canonical='http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient')
```

### Resolution Priority & Fallback Chain

The resolver uses a **4-tier fallback chain** with tenant resources taking priority:

```
Resolution Order (highest to lowest priority):
┌────────────────────────────────────────────────┐
│ 1. Cache (L1 Memory + L2 Redis)               │ ← All sources cached here
├────────────────────────────────────────────────┤
│ 2. Tenant Resources (Resource table)          │ ← USER UPLOADS (highest priority)
│    - StructureDefinitions uploaded via POST   │
│    - ValueSets created by tenant               │
│    - CodeSystems customized for organization  │
├────────────────────────────────────────────────┤
│ 3. Package Resources (PackageResource table)  │ ← IG PACKAGES (standard definitions)
│    - Loaded from NPM packages                  │
│    - Version selection based on:               │
│      a. Explicit version in canonical URL      │
│      b. Preferred packages (header/config)     │
│      c. Tenant default packages                │
│      d. Latest version available               │
├────────────────────────────────────────────────┤
│ 4. External Registry (packages.fhir.org)      │ ← FALLBACK (slowest, optional)
│    - Live download if local not found          │
│    - Cached after first retrieval              │
└────────────────────────────────────────────────┘
```

**Why Tenant Resources First?**
- ✅ **Customization**: Organizations can override standard definitions
- ✅ **Testing**: Upload draft profiles for validation testing
- ✅ **Local Extensions**: Organization-specific profiles not in public IGs
- ✅ **Hotfixes**: Patch broken ValueSets without waiting for IG update

### Version Resolution Strategy

```csharp
private async ValueTask<T?> ResolveFromPackagesAsync<T>(
    string tenantId,
    string canonical,
    string? version,
    IReadOnlyList<string>? preferredPackages,
    CancellationToken cancellationToken) where T : Resource
{
    // 1. EXPLICIT VERSION (highest priority)
    // Example: "http://.../us-core-patient|6.0.0"
    if (!string.IsNullOrEmpty(version))
    {
        var exact = await QueryPackageResourceAsync<T>(
            canonical, version, packageId: null, cancellationToken);
        if (exact != null)
        {
            _logger.LogDebug("Resolved {Canonical}|{Version} with explicit version",
                canonical, version);
            return exact;
        }
    }

    // 2. PREFERRED PACKAGES (from request headers or options)
    // Example: X-IG-Version: hl7.fhir.us.core@6.0.0
    if (preferredPackages?.Count > 0)
    {
        foreach (var packageRef in preferredPackages)
        {
            var (packageId, packageVersion) = ParsePackageRef(packageRef);
            var fromPreferred = await QueryPackageResourceAsync<T>(
                canonical, packageVersion, packageId, cancellationToken);
            if (fromPreferred != null)
            {
                _logger.LogInformation(
                    "Resolved {Canonical} from preferred package {PackageId}@{Version}",
                    canonical, packageId, packageVersion);
                return fromPreferred;
            }
        }
    }

    // 3. TENANT DEFAULT PACKAGES (from configuration)
    var tenantConfig = await _tenantConfig.GetConfigurationAsync(tenantId, cancellationToken);
    var defaultPackages = tenantConfig.DefaultPackages
        .GetValueOrDefault(FhirVersion.R4, Array.Empty<string>());

    foreach (var packageRef in defaultPackages)
    {
        var (packageId, packageVersion) = ParsePackageRef(packageRef);
        var fromDefault = await QueryPackageResourceAsync<T>(
            canonical, packageVersion, packageId, cancellationToken);
        if (fromDefault != null)
        {
            _logger.LogInformation(
                "Resolved {Canonical} from tenant default {PackageId}@{Version}",
                canonical, packageId, packageVersion);
            return fromDefault;
        }
    }

    // 4. LATEST VERSION (fallback)
    var latest = await QueryLatestPackageResourceAsync<T>(canonical, cancellationToken);
    if (latest != null)
    {
        _logger.LogWarning(
            "Resolved {Canonical} to latest version (no explicit version specified)",
            canonical);
    }

    return latest;
}

// SQL query for latest version using semantic versioning
private async Task<T?> QueryLatestPackageResourceAsync<T>(
    string canonical,
    CancellationToken cancellationToken) where T : Resource
{
    // Parse semantic versions (MAJOR.MINOR.PATCH)
    var sql = @"
        SELECT TOP 1 ResourceJson
        FROM PackageResource
        WHERE Canonical = @canonical
          AND ResourceType = @resourceType
        ORDER BY
            CAST(PARSENAME(PackageVersion, 3) AS INT) DESC,  -- Major
            CAST(PARSENAME(PackageVersion, 2) AS INT) DESC,  -- Minor
            CAST(PARSENAME(PackageVersion, 1) AS INT) DESC   -- Patch";

    var result = await _dbContext.PackageResources
        .FromSqlRaw(sql, new { canonical, resourceType = typeof(T).Name })
        .FirstOrDefaultAsync(cancellationToken);

    return result != null ? JsonSerializer.Deserialize<T>(result.ResourceJson) : null;
}
```

### Tenant Configuration for Version Control

```csharp
public record TenantPackageConfiguration
{
    public required string TenantId { get; init; }

    // Pin specific package versions per FHIR version
    public IReadOnlyDictionary<FhirVersion, IReadOnlyList<string>> DefaultPackages { get; init; } =
        new Dictionary<FhirVersion, IReadOnlyList<string>>
        {
            [FhirVersion.R4] = new[]
            {
                "hl7.fhir.r4.core@4.0.1",
                "hl7.fhir.us.core@6.0.0"  // Pin to v6
            }
        };

    // Fallback strategy when no version specified
    public VersionResolutionStrategy VersionResolution { get; init; } =
        VersionResolutionStrategy.TenantDefault;

    // Allow multiple versions to coexist?
    public bool AllowMultipleVersions { get; init; } = true;
}

public enum VersionResolutionStrategy
{
    TenantDefault,  // Use tenant's configured default packages
    Latest,         // Always use latest version in PackageResource table
    Oldest,         // Use oldest version (for maximum stability)
    Fail            // Require explicit version, error if not specified
}
```

### Resolution Examples

#### Example 1: Explicit Version in Profile URL

```http
POST /Patient
{
  "resourceType": "Patient",
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient|6.0.0"]
  }
}

→ Resolution:
  1. Cache: MISS
  2. Tenant Resources: MISS (not uploaded by user)
  3. Package Resources:
     - Parse version "6.0.0" from canonical
     - Query: WHERE Canonical=... AND Version='6.0.0'
     - HIT ✅ Returns US Core 6.0.0 profile
```

#### Example 2: Request Header Specifies Package

```http
POST /Patient
X-IG-Version: hl7.fhir.us.core@7.0.0
{
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  }
}

→ Resolution:
  1. Cache: MISS
  2. Tenant Resources: MISS
  3. Package Resources:
     - PreferredPackages = ["hl7.fhir.us.core@7.0.0"] (from header)
     - Query: WHERE Canonical=... AND PackageId='hl7.fhir.us.core' AND PackageVersion='7.0.0'
     - HIT ✅ Returns US Core 7.0.0 profile
```

#### Example 3: Tenant Default Package

```http
POST /Patient
# Tenant config: defaultPackages = { R4: ["hl7.fhir.us.core@6.0.0"] }
{
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  }
}

→ Resolution:
  1. Cache: MISS
  2. Tenant Resources: MISS
  3. Package Resources:
     - No explicit version, no preferred packages
     - Tenant defaults = ["hl7.fhir.us.core@6.0.0"]
     - Query: WHERE Canonical=... AND PackageId='hl7.fhir.us.core' AND PackageVersion='6.0.0'
     - HIT ✅ Returns US Core 6.0.0 profile (tenant default)
```

#### Example 4: Latest Version Fallback

```http
POST /Patient
# No version specified, no tenant defaults
{
  "meta": {
    "profile": ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]
  }
}

→ Resolution:
  1. Cache: MISS
  2. Tenant Resources: MISS
  3. Package Resources:
     - No version, no preferred packages, no tenant defaults
     - Query: WHERE Canonical=... ORDER BY version DESC
     - HIT ✅ Returns US Core 7.0.0 (latest available)
     - ⚠️ Warning logged: "Using latest version, consider pinning"
```

#### Example 5: Tenant Resource Override

```http
# Step 1: Tenant uploads custom profile
POST /StructureDefinition
{
  "resourceType": "StructureDefinition",
  "url": "http://example.org/fhir/StructureDefinition/custom-patient",
  "version": "1.0.0",
  "name": "CustomPatient",
  "status": "active",
  "kind": "resource",
  "type": "Patient"
  // ... custom constraints
}
→ Saved to Resource table (tenant-specific)

# Step 2: Validate against custom profile
POST /Patient
{
  "meta": {
    "profile": ["http://example.org/fhir/StructureDefinition/custom-patient"]
  }
}

→ Resolution:
  1. Cache: MISS
  2. Tenant Resources:
     - Query Resource table: WHERE url='http://example.org/.../custom-patient'
     - HIT ✅ Returns tenant's custom profile
  3. (Package Resources: skipped, already found)

→ Validation uses tenant's custom profile, not package version
```

### Package Version Migration Strategy

**Scenario**: Upgrade from US Core 6.0.0 to 7.0.0

```bash
# Step 1: Load new package (coexists with v6)
POST /admin/packages/load
{
  "packageId": "hl7.fhir.us.core",
  "version": "7.0.0"
}
→ PackageResource table now has both 6.0.0 and 7.0.0

# Step 2: Test with specific tenant (canary deployment)
PUT /admin/tenants/test-tenant/config
{
  "defaultPackages": {
    "R4": ["hl7.fhir.us.core@7.0.0"]
  }
}
→ test-tenant now uses v7, others still on v6

# Step 3: Validate test resources
POST /Patient
X-Tenant-Id: test-tenant
{ ... }
→ Validation uses US Core 7.0.0 profiles

# Step 4: Gradual rollout to production tenants
PUT /admin/tenants/tenant1/config { ... "7.0.0" }
PUT /admin/tenants/tenant2/config { ... "7.0.0" }

# Step 5: After all migrated, optionally remove v6
DELETE /admin/packages/hl7.fhir.us.core/6.0.0
→ Deletes from PackageResource table
```

### Configuration Examples

**Development (lenient, latest versions)**:
```json
{
  "tenantId": "dev",
  "defaultPackages": {
    "R4": ["hl7.fhir.r4.core@4.0.1"]
  },
  "versionResolution": "Latest",
  "allowMultipleVersions": true
}
```

**Staging (realistic, pinned major version)**:
```json
{
  "tenantId": "staging",
  "defaultPackages": {
    "R4": [
      "hl7.fhir.r4.core@4.0.1",
      "hl7.fhir.us.core@6.0.0"
    ]
  },
  "versionResolution": "TenantDefault",
  "allowMultipleVersions": true
}
```

**Production (strict, explicit versions required)**:
```json
{
  "tenantId": "prod",
  "defaultPackages": {
    "R4": [
      "hl7.fhir.r4.core@4.0.1",
      "hl7.fhir.us.core@6.0.0",
      "hl7.fhir.us.mcode@2.0.0"
    ]
  },
  "versionResolution": "Fail",  // Force explicit versions
  "allowMultipleVersions": false
}
```

### Table Comparison: Resource vs PackageResource

| Aspect | Resource Table | PackageResource Table |
|--------|----------------|----------------------|
| **Source** | User uploaded (POST/PUT) | Package imported (admin API) |
| **Tenant Scope** | Per-tenant (partitioned) | Global (shared across tenants) |
| **Mutability** | Mutable (can UPDATE/DELETE) | Immutable (delete entire package) |
| **Storage** | Compressed (Gzip) | Uncompressed (fast reads) |
| **Versioning** | FHIR meta.versionId (1, 2, 3...) | Package semver (6.0.0, 7.0.0) |
| **Resolution Priority** | **Higher** (tenant customization) | Lower (standard definitions) |
| **Cache TTL** | 1 hour (may change frequently) | 4 hours (immutable) |
| **Use Cases** | Testing, customization, local extensions | Standard IGs, shared profiles |

---

## Implementation Plan

### Phase 1: Foundation (3-4 weeks)

**Goal**: Build shared infrastructure and basic package loading

#### Week 1: Database Schema & Migrations

1. **Create PackageResource table**
   ```bash
   cd src/Ignixa.DataLayer.SqlEntityFramework
   dotnet ef migrations add AddPackageResourceTable
   ```

2. **Create terminology indexes** (from ADR-2531)
   - `IX_TokenSearchParam_SearchParamId_SystemId_Code`
   - `IX_TokenSearchParam_SystemId_Code`
   - `IX_TokenSearchParam_ResourceTypeId_SearchParamId`

3. **Test migration on dev database**

#### Week 2-3: Package Management Core

4. **Implement NPM package loader**
   ```
   src/Ignixa.PackageManagement/
     ├── NpmPackageLoader.cs                    (download .tgz from packages.fhir.org)
     ├── PackageExtractor.cs                    (extract resources from tarball)
     ├── PackageResourceImporter.cs             (save to PackageResource table)
     └── IImplementationGuideProvider.cs        (interface from spec)
   ```

5. **Implement conformance cache**
   ```
   src/Ignixa.Domain/Caching/
     ├── IFhirConformanceCache.cs               (interface)
     ├── TwoTierConformanceCache.cs             (memory + Redis)
     └── ConformanceResourceResolver.cs         (unified resolver)
   ```

6. **Create package loading endpoint** (admin-only)
   ```
   POST /admin/packages/load
   {
     "packageId": "hl7.fhir.us.core",
     "version": "5.0.1",
     "source": "https://packages.fhir.org/hl7.fhir.us.core/5.0.1"
   }
   ```

#### Week 4: Testing & Integration

7. **Load test packages**
   - HL7 FHIR Core (base StructureDefinitions)
   - US Core 5.0.1
   - Verify PackageResource table populated

8. **Test conformance resolver**
   - Resolve `http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient`
   - Verify cache hit rates
   - Test fallback chain

**Phase 1 Deliverables**:
- ✅ PackageResource table with indexes
- ✅ Terminology indexes (3 from ADR-2531)
- ✅ NPM package loading infrastructure
- ✅ Conformance cache with L1 (memory) + L2 (Redis)
- ✅ Admin endpoint to load packages
- ✅ US Core 5.0.1 loaded and queryable

---

### Phase 2: Core Services (4-5 weeks)

**Goal**: Implement basic validation and terminology operations

#### Week 5-6: Validation Schema Building

9. **Implement schema compilation** (from ADR-2527)
   ```
   src/Ignixa.Validation/
     ├── Schema/
     │   ├── IValidationSchemaResolver.cs
     │   ├── ValidationSchema.cs
     │   ├── ValidationSchemaBuilder.cs          (builds from StructureDefinition)
     │   └── CachedValidationSchemaResolver.cs
     └── Assertions/
         ├── IAssertion.cs
         ├── CardinalityAssertion.cs
         ├── FhirPathAssertion.cs
         ├── TypeAssertion.cs
         └── ChoiceTypeAssertion.cs
   ```

10. **Build schemas from packages**
    - Read StructureDefinition from PackageResource table
    - Compile to ValidationSchema with assertions
    - Cache compiled schemas

11. **Test with base FHIR profiles**
    - Patient, Observation, Condition
    - Verify cardinality, FHIRPath invariants

#### Week 7-8: Basic Terminology

12. **Implement terminology handlers** (from ADR-2531)
    ```
    src/Ignixa.Application/Features/Terminology/
      ├── ValidateCodeQuery.cs
      ├── ValidateCodeHandler.cs
      ├── ExpandValueSetQuery.cs
      └── ExpandValueSetHandler.cs
    ```

13. **Implement terminology endpoints**
    ```
    src/Ignixa.Api/Endpoints/TerminologyEndpoints.cs
      - POST /ValueSet/$validate-code
      - GET /ValueSet/$validate-code?url=...&code=...
      - POST /ValueSet/$expand
      - GET /ValueSet/$expand?url=...
    ```

14. **Test with package ValueSets**
    - Load ValueSet from US Core package
    - Expand using existing indexes
    - Validate codes

#### Week 9: Integration Testing

15. **End-to-end tests**
    - Load US Core package
    - Validate US Core Patient resource
    - Expand US Core ValueSet
    - Measure performance

**Phase 2 Deliverables**:
- ✅ Validation schema builder (StructureDefinition → ValidationSchema)
- ✅ Basic assertions (cardinality, FHIRPath, type)
- ✅ $validate-code operation (ValueSet)
- ✅ $expand operation (small ValueSets <10K codes)
- ✅ Integration tests passing

---

### Phase 3: Integration (3-4 weeks)

**Goal**: Connect validation to terminology, packages to validation

#### Week 10-11: Binding Validation

16. **Implement BindingAssertion** (calls terminology)
    ```csharp
    public class BindingAssertion : IAssertion
    {
        private readonly string _valueSetUrl;
        private readonly BindingStrength _strength;
        private readonly ITerminologyService _terminologyService;

        public async ValueTask<IssueAssertion?> ValidateAsync(
            JsonNode node,
            ValidationContext context,
            CancellationToken cancellationToken)
        {
            // Extract code from Coding/CodeableConcept
            var (system, code, display) = ExtractCoding(node);

            // Call terminology service
            var result = await _terminologyService.ValidateCodeAsync(
                system, code, display, _valueSetUrl, cancellationToken);

            if (!result.IsValid && _strength == BindingStrength.Required)
            {
                return new IssueAssertion
                {
                    Severity = IssueSeverity.Error,
                    Code = IssueType.CodeInvalid,
                    Diagnostics = result.Message
                };
            }

            return null;
        }
    }
    ```

17. **Test binding validation**
    - US Core Patient.gender (required binding to http://hl7.org/fhir/ValueSet/administrative-gender)
    - Invalid code → HTTP 400
    - Valid code → HTTP 201

#### Week 12: Profile Resolution from Packages

18. **Implement profile-based validation**
    ```csharp
    // In CreateOrUpdateResourceHandler
    var profileUrls = resource.Meta?.Profile ?? [];

    foreach (var profileUrl in profileUrls)
    {
        // Resolve StructureDefinition from package
        var structureDef = await _conformanceResolver.ResolveAsync<StructureDefinition>(
            tenantId, profileUrl, version: null, cancellationToken);

        if (structureDef == null)
        {
            _logger.LogWarning("Profile not found: {ProfileUrl}", profileUrl);
            continue;
        }

        // Get compiled schema
        var schema = _schemaResolver.GetSchema(profileUrl);
        if (schema == null)
        {
            // Build schema on-demand
            schema = _schemaBuilder.BuildSchema(structureDef);
            _schemaResolver.CacheSchema(profileUrl, schema);
        }

        // Validate against profile
        var validationResult = await _validator.ValidateAsync(resource, schema, cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Issues);
        }
    }
    ```

19. **Test US Core profile validation**
    - Submit resource with `meta.profile = ["http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"]`
    - Verify profile loaded from package
    - Verify validation errors for missing required elements

#### Week 13: Performance Optimization

20. **Optimize cache hit rates**
    - Pre-warm cache with common profiles on startup
    - Monitor cache misses
    - Tune TTL values

21. **Benchmark validation pipeline**
    - Target: <50ms for basic validation
    - Target: <200ms for profile validation with terminology

**Phase 3 Deliverables**:
- ✅ Binding validation integrated with terminology service
- ✅ Profile validation using package StructureDefinitions
- ✅ End-to-end US Core validation working
- ✅ Performance targets met (<200ms)

---

### Phase 4: Advanced Features (2-3 weeks)

**Goal**: Complete advanced validation and terminology features

#### Week 14: Slicing & Extensions

22. **Implement SlicingAssertion**
    ```csharp
    public class SlicingAssertion : IAssertion
    {
        private readonly SlicingDefinition _slicing;
        private readonly IReadOnlyList<SliceDefinition> _slices;

        public async ValueTask<IssueAssertion?> ValidateAsync(
            JsonNode node,
            ValidationContext context,
            CancellationToken cancellationToken)
        {
            // Get array elements
            var elements = node.AsArray();

            // Match each element to a slice using discriminator
            var sliceMatches = new Dictionary<string, List<JsonNode>>();
            foreach (var element in elements)
            {
                var sliceName = MatchSlice(element, _slices);
                if (!sliceMatches.ContainsKey(sliceName))
                    sliceMatches[sliceName] = new List<JsonNode>();
                sliceMatches[sliceName].Add(element);
            }

            // Validate cardinality for each slice
            foreach (var slice in _slices)
            {
                var count = sliceMatches.TryGetValue(slice.Name, out var matches) ? matches.Count : 0;

                if (count < slice.Min)
                {
                    return new IssueAssertion
                    {
                        Severity = IssueSeverity.Error,
                        Code = IssueType.Required,
                        Diagnostics = $"Slice '{slice.Name}' requires at least {slice.Min} elements, found {count}"
                    };
                }

                if (slice.Max != "*" && count > int.Parse(slice.Max))
                {
                    return new IssueAssertion
                    {
                        Severity = IssueSeverity.Error,
                        Code = IssueType.TooMany,
                        Diagnostics = $"Slice '{slice.Name}' allows at most {slice.Max} elements, found {count}"
                    };
                }
            }

            return null;
        }
    }
    ```

23. **Test US Core slicing**
    - US Core Patient.identifier (sliced by system)
    - US Core Patient.name (sliced by use)

#### Week 15-16: Advanced Terminology

24. **Implement specialized terminology tables** (from ADR-2531 Phase 2)
    - Concept table (for CodeSystem.concept[])
    - ValueSetExpansion cache
    - ConceptMapElement table

25. **Implement remaining operations**
    - $lookup
    - $translate
    - $subsumes

26. **Bulk terminology import**
    - LOINC, SNOMED CT, RxNorm extraction
    - Background job integration

**Phase 4 Deliverables**:
- ✅ Slicing validation working
- ✅ Extension validation working
- ✅ All 5 terminology operations implemented
- ✅ Specialized terminology tables
- ✅ Bulk import for large terminologies

---

## Custom Resource Support Architecture

### Overview

Custom resources (resource types not defined in the core FHIR specification) are supported through Implementation Guide loading and validation. This section describes how custom resources fit into the unified validation, terminology, and package architecture.

**Examples of custom resources**:
- **ViewDefinition** (SQL-on-FHIR v2 IG)
- **MolecularSequence** extensions (Genomics IG)
- **Organization-specific resources** (custom IGs)

**Key principle**: Custom resources follow the same validation and storage patterns as core FHIR resources once their StructureDefinitions are loaded via packages.

### FHIR Specification Guidance

Based on FHIR R4/R5/R6 specifications:

- **StructureDefinition Definition**: Custom resources are defined with `kind="resource"` and `derivation="specialization"`
- **Server Discretion**: Servers have discretion to accept or reject unknown resource types (404 Not Found is the standard response)
- **Validation Requirements**: Validation is **discretionary** - servers choose how much to perform
- **Extensibility Rule**: Extensions cannot define new resource types (must use StructureDefinition)
- **CapabilityStatement**: Servers MUST declare supported resource types in CapabilityStatement

**Canonical Spec URLs**:
- StructureDefinition.kind: https://hl7.org/fhir/R4/structuredefinition-definitions.html#StructureDefinition.kind
- StructureDefinition.derivation: https://hl7.org/fhir/R4/structuredefinition-definitions.html#StructureDefinition.derivation
- RESTful API: https://hl7.org/fhir/R4/http.html#general

### Middleware Acceptance Strategy

**Decision**: Hybrid Validation - Accept custom resources IF the IG is loaded OR tenant configuration allows

#### When Custom Resources Are Accepted

Custom resources (unknown resource types) are accepted by the middleware when:

1. **IG Loaded** (Recommended)
   - StructureDefinition for the resource type is available in PackageResource table
   - Full validation tiers (Tier 1+2+3) can be applied
   - SearchParameters are registered and indexed

2. **Tenant Configuration** (Optional, configurable)
   - Tenant setting `AllowUnknownResourceTypes = true`
   - Useful for testing and development
   - Only Tier 1 (structural) validation applied

#### Implementation Example

```csharp
public class FhirResourceTypeValidator
{
    private readonly ISchemaProvider _schemaProvider;
    private readonly IConformanceResourceResolver _conformanceResolver;
    private readonly TenantConfiguration _tenantConfig;
    private readonly ILogger<FhirResourceTypeValidator> _logger;

    public async ValueTask<bool> IsValidResourceTypeAsync(
        string resourceType,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // 1. Check core FHIR resources
        if (_schemaProvider.ResourceTypeNames.Contains(resourceType))
            return true;

        // 2. Check loaded IGs for custom resources
        // Try to resolve StructureDefinition from packages
        var structureDefCanonical = $"http://hl7.org/fhir/StructureDefinition/{resourceType}";
        var structureDef = await _conformanceResolver.ResolveAsync<StructureDefinition>(
            tenantId,
            structureDefCanonical,
            version: null,
            cancellationToken);

        if (structureDef != null && IsCustomResource(structureDef))
            return true;

        // 3. Check tenant configuration
        var config = await _tenantConfig.GetConfigurationAsync(tenantId, cancellationToken);
        if (config.AllowUnknownResourceTypes)
        {
            _logger.LogWarning(
                "Accepting unvalidated custom resource: {ResourceType} for tenant {TenantId}",
                resourceType, tenantId);
            return true;
        }

        return false;
    }

    private bool IsCustomResource(StructureDefinition sd)
    {
        // Custom resource detection algorithm (see below)
        return sd.Kind == StructureDefinitionKind.Resource
            && sd.Derivation == StructureDefinitionDerivation.Specialization
            && sd.Abstract != true;
    }
}
```

#### Configuration

```json
{
  "validation": {
    "customResources": {
      "allowUnknownResourceTypes": false,
      "requireIgForAcceptance": true,
      "requireValidationForAcceptance": false,
      "failOnMissingStructureDefinition": false
    }
  }
}
```

### Validation Tier Assignment

**Decision**: Custom resources follow the same validation tier rules as core resources

| Tier | When Applied | Validation | Custom Resources |
|------|-------------|-----------|-----------------|
| **Tier 1** | Always | JSON structure, resourceType field | ✅ Always applied |
| **Tier 2** | If IG loaded | StructureDefinition-based validation | ✅ Applied if StructureDefinition available |
| **Tier 3** | If terminology available | ValueSet binding, reference validation | ✅ Applied if profiles loaded |

#### Strictness Configuration

```csharp
public enum CustomResourceValidationStrictness
{
    /// <summary>Lenient: Only Tier 1, accept any custom resource</summary>
    Lenient,

    /// <summary>Moderate: Tier 1+2 if IG loaded, warn if not</summary>
    Moderate,

    /// <summary>Strict: Tier 1+2+3 required, reject if IG not loaded</summary>
    Strict
}
```

**Behavior Examples**:

```csharp
// Lenient mode
POST /ViewDefinition { ... }
// Result: Tier 1 only (JSON structure)
// Accepted even without ViewDefinition StructureDefinition

// Moderate mode (recommended)
POST /ViewDefinition { ... }
// Result: Tier 1 + Tier 2 (if StructureDefinition loaded)
// Warning logged if StructureDefinition not available
// Accepted regardless

// Strict mode
POST /ViewDefinition { ... }
// Result: Tier 1 + Tier 2 + Tier 3 required
// HTTP 400 returned if StructureDefinition not loaded
```

### Storage Architecture

**Decision**: Custom resource instances stored in main Resource table (same as core resources)

#### Storage Tables

```sql
-- Resource Table (main, tenant-partitioned)
-- Stores instances of both core and custom resources
Resource:
  TenantId: 1
  ResourceType: "ViewDefinition"
  ResourceId: "patient-demographics"
  JsonData: {...}

  TenantId: 1
  ResourceType: "Patient"
  ResourceId: "123"
  JsonData: {...}

-- PackageResource Table (global, shared)
-- Stores StructureDefinitions and other conformance resources from IGs
PackageResource:
  PackageId: "hl7.fhir.uv.sql-on-fhir"
  PackageVersion: "2.0.0"
  ResourceType: "StructureDefinition"
  Canonical: "http://hl7.org/fhir/uv/sql-on-fhir/StructureDefinition/ViewDefinition"
  ResourceJson: {...}
```

**Rationale**:
- ✅ Same multi-tenant isolation as core resources
- ✅ Same versioning, history, search indexing infrastructure
- ✅ PackageResource stores metadata (StructureDefinitions from IGs)
- ✅ Resource table stores instances (created by tenants)
- ✅ No schema changes needed for custom resources

### Custom Resource Detection

**Decision**: Detect custom resources from StructureDefinition metadata using standardized algorithm

#### Detection Algorithm

A custom resource is identified when:

1. `StructureDefinition.kind == "resource"` (must be resource type)
2. `StructureDefinition.derivation == "specialization"` (not a profile/constraint)
3. `StructureDefinition.type` NOT in core FHIR resource list
4. `StructureDefinition.abstract != true` (not abstract)

#### Implementation

```csharp
public static class CustomResourceDetector
{
    // Core FHIR R4 resource types (153 total)
    private static readonly HashSet<string> CoreR4Resources = new(StringComparer.Ordinal)
    {
        "Account", "ActivityDefinition", "AdverseEvent", "AllergyIntolerance",
        "Appointment", "AppointmentResponse", "AuditEvent", "Basic", "Binary",
        "BiologicallyDerivedProduct", "BodyStructure", "Bundle", "CapabilityStatement",
        "CarePlan", "CareTeam", "CatalogEntry", "ChargeItem", "ChargeItemDefinition",
        "Claim", "ClaimResponse", "ClinicalImpression", "CodeSystem", "Communication",
        "CommunicationRequest", "CompartmentDefinition", "Composition", "ConceptMap",
        "Condition", "Consent", "Contract", "Coverage", "CoverageEligibilityRequest",
        "CoverageEligibilityResponse", "DetectedIssue", "Device", "DeviceDefinition",
        "DeviceMetric", "DeviceRequest", "DeviceUseStatement", "DiagnosticReport",
        "DocumentManifest", "DocumentReference", "EffectEvidenceSynthesis", "Encounter",
        "Endpoint", "EnrollmentRequest", "EnrollmentResponse", "EpisodeOfCare",
        "EventDefinition", "Evidence", "EvidenceVariable", "ExampleScenario",
        "ExplanationOfBenefit", "FamilyMemberHistory", "Flag", "Goal", "GraphDefinition",
        "Group", "GuidanceResponse", "HealthcareService", "ImagingStudy", "Immunization",
        "ImmunizationEvaluation", "ImmunizationRecommendation", "ImplementationGuide",
        "InsurancePlan", "Invoice", "Library", "Linkage", "List", "Location", "Measure",
        "MeasureReport", "Media", "Medication", "MedicationAdministration",
        "MedicationDispense", "MedicationKnowledge", "MedicationRequest",
        "MedicationStatement", "MedicinalProduct", "MedicinalProductAuthorization",
        "MedicinalProductContraindication", "MedicinalProductIndication",
        "MedicinalProductIngredient", "MedicinalProductInteraction",
        "MedicinalProductManufactured", "MedicinalProductPackaged",
        "MedicinalProductPharmaceutical", "MedicinalProductUndesirableEffect",
        "MessageDefinition", "MessageHeader", "MolecularSequence", "NamingSystem",
        "NutritionOrder", "Observation", "ObservationDefinition", "OperationDefinition",
        "OperationOutcome", "Organization", "OrganizationAffiliation", "Parameters",
        "Patient", "PaymentNotice", "PaymentReconciliation", "Person", "PlanDefinition",
        "Practitioner", "PractitionerRole", "Procedure", "Provenance", "Questionnaire",
        "QuestionnaireResponse", "RelatedPerson", "RequestGroup", "ResearchDefinition",
        "ResearchElementDefinition", "ResearchStudy", "ResearchSubject", "RiskAssessment",
        "RiskEvidenceSynthesis", "Schedule", "SearchParameter", "ServiceRequest", "Slot",
        "Specimen", "SpecimenDefinition", "StructureDefinition", "StructureMap",
        "Subscription", "Substance", "SubstanceNucleicAcid", "SubstancePolymer",
        "SubstanceProtein", "SubstanceReferenceInformation", "SubstanceSourceMaterial",
        "SubstanceSpecification", "SupplyDelivery", "SupplyRequest", "Task",
        "TerminologyCapabilities", "TestReport", "TestScript", "ValueSet",
        "VerificationResult", "VisionPrescription"
    };

    public static bool IsCustomResource(
        StructureDefinition structureDefinition,
        FhirSpecification fhirVersion = FhirSpecification.R4)
    {
        // 1. Must be a resource type
        if (structureDefinition.Kind != StructureDefinitionKind.Resource)
            return false;

        // 2. Must be a specialization (not a constraint/profile)
        if (structureDefinition.Derivation != StructureDefinitionDerivation.Specialization)
            return false;

        // 3. Must NOT be abstract
        if (structureDefinition.Abstract == true)
            return false;

        // 4. Must NOT be a core FHIR resource
        var coreResources = GetCoreResourcesForVersion(fhirVersion);
        if (coreResources.Contains(structureDefinition.Type))
            return false;

        return true;
    }

    private static HashSet<string> GetCoreResourcesForVersion(FhirSpecification version)
    {
        return version switch
        {
            FhirSpecification.R4 => CoreR4Resources,
            FhirSpecification.R5 => CoreR5Resources,  // 165 resources
            FhirSpecification.STU3 => CoreSTU3Resources,  // 94 resources
            _ => throw new ArgumentException($"Unsupported FHIR version: {version}")
        };
    }
}
```

#### Usage Example

```csharp
// Check if a resource is custom
var structureDef = await _conformanceResolver.ResolveAsync<StructureDefinition>(
    tenantId,
    "http://hl7.org/fhir/uv/sql-on-fhir/StructureDefinition/ViewDefinition",
    cancellationToken: ct);

if (CustomResourceDetector.IsCustomResource(structureDef))
{
    // This is a custom resource - handle accordingly
    await _igRegistry.RegisterCustomResourceAsync(
        tenantId,
        structureDef.Type,  // "ViewDefinition"
        structureDef,
        ct);
}
```

### CapabilityStatement Advertisement

**Decision**: Auto-advertise custom resources from loaded IGs in CapabilityStatement

#### When Custom Resources Appear

Custom resources are automatically added to CapabilityStatement when:

1. StructureDefinition is loaded into PackageResource table
2. CustomResourceDetector identifies it as a custom resource
3. Tenant has access to the IG (via configuration or auto-discovery)

#### Implementation Example

```csharp
public class CapabilityStatementBuilder
{
    private readonly IConformanceResourceResolver _conformanceResolver;
    private readonly IIgRegistry _igRegistry;
    private readonly ILogger<CapabilityStatementBuilder> _logger;

    public async Task<CapabilityStatement> BuildAsync(
        string tenantId,
        FhirSpecification fhirVersion,
        CancellationToken cancellationToken)
    {
        var cs = new CapabilityStatement
        {
            FhirVersion = fhirVersion.ToCapabilityStatementFhirVersion(),
            Software = new CapabilityStatement.SoftwareComponent
            {
                Name = "Ignixa FHIR Server",
                Version = typeof(Program).Assembly.GetName().Version?.ToString()
            },
            Rest = new List<CapabilityStatement.RestComponent>
            {
                new()
                {
                    Mode = CapabilityStatement.RestfulCapabilityMode.Server,
                    Resource = new List<CapabilityStatement.ResourceComponent>()
                }
            }
        };

        var restComponent = cs.Rest[0];

        // 1. Add core resources
        foreach (var resourceType in _schemaProvider.ResourceTypeNames)
        {
            AddResourceToCapabilityStatement(restComponent, resourceType);
        }

        // 2. Add custom resources from loaded IGs
        var customResources = await _igRegistry.GetCustomResourcesForTenantAsync(
            tenantId, cancellationToken);

        foreach (var customResourceType in customResources)
        {
            AddResourceToCapabilityStatement(restComponent, customResourceType);
            _logger.LogInformation(
                "Added custom resource to CapabilityStatement: {ResourceType}",
                customResourceType);
        }

        return cs;
    }

    private void AddResourceToCapabilityStatement(
        CapabilityStatement.RestComponent restComponent,
        string resourceType)
    {
        var resourceComponent = new CapabilityStatement.ResourceComponent
        {
            Type = resourceType,
            Interaction = new List<CapabilityStatement.ResourceInteractionComponent>
            {
                new() { Code = CapabilityStatement.TypeRestfulInteraction.Create },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.Read },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.Update },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.Delete },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.SearchType },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.HistoryInstance },
                new() { Code = CapabilityStatement.TypeRestfulInteraction.HistoryType }
            },
            Versioning = CapabilityStatement.ResourceVersionPolicy.Versioned,
            ReadHistory = true,
            UpdateCreate = true,
            ConditionalCreate = true,
            ConditionalUpdate = true
        };

        // Get profile URL if available
        var structureDef = _conformanceResolver.ResolveAsync<StructureDefinition>(
            tenantId: "<system>",
            canonical: $"http://hl7.org/fhir/StructureDefinition/{resourceType}",
            version: null,
            cancellationToken: CancellationToken.None).Result;

        if (structureDef?.Url != null)
        {
            resourceComponent.Profile = structureDef.Url;
        }

        restComponent.Resource.Add(resourceComponent);
    }
}
```

#### CapabilityStatement Output Example

```json
{
  "resourceType": "CapabilityStatement",
  "fhirVersion": "4.0.1",
  "rest": [{
    "mode": "server",
    "resource": [
      {
        "type": "Patient",
        "profile": "http://hl7.org/fhir/StructureDefinition/Patient",
        "interaction": [
          {"code": "create"},
          {"code": "read"},
          {"code": "update"}
        ]
      },
      {
        "type": "ViewDefinition",
        "profile": "http://hl7.org/fhir/uv/sql-on-fhir/StructureDefinition/ViewDefinition",
        "interaction": [
          {"code": "create"},
          {"code": "read"},
          {"code": "update"}
        ]
      }
    ]
  }]
}
```

### SearchParameter Registration

**Decision**: Extract SearchParameters from IG during loading and register with search indexer

#### SearchParameter Extraction

When an IG is loaded, SearchParameters are extracted and registered:

```csharp
public class ImplementationGuideLoader
{
    private readonly IConformanceResourceResolver _conformanceResolver;
    private readonly ISearchParameterRegistry _searchParamRegistry;
    private readonly ILogger<ImplementationGuideLoader> _logger;

    public async Task LoadImplementationGuideAsync(
        string packageId,
        string version,
        string tenantId,
        LoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LoadOptions();

        // 1. Download and extract package
        var package = await _packageLoader.LoadPackageAsync(
            $"https://packages.fhir.org/{packageId}/{version}",
            cancellationToken);

        var structureDefinitions = package.GetResourcesByType<StructureDefinition>();
        var searchParameters = package.GetResourcesByType<SearchParameter>();

        // 2. Import StructureDefinitions to PackageResource table
        await _packageImporter.ImportAsync(structureDefinitions, packageId, version, cancellationToken);

        // 3. Register SearchParameters
        var customResources = new List<string>();
        foreach (var sd in structureDefinitions)
        {
            if (CustomResourceDetector.IsCustomResource(sd))
            {
                customResources.Add(sd.Type);
            }
        }

        // Get SearchParameters for custom resources
        var customResourceSearchParams = searchParameters
            .Where(sp => customResources.Contains(sp.Base?.FirstOrDefault()?.Code))
            .ToList();

        _logger.LogInformation(
            "Registering {Count} SearchParameters for custom resources",
            customResourceSearchParams.Count);

        foreach (var searchParam in customResourceSearchParams)
        {
            // Register with search parameter indexer
            await _searchParamRegistry.RegisterSearchParameterAsync(
                tenantId,
                searchParam,
                indexExisting: options.ReindexExistingResources,
                cancellationToken);
        }

        // 4. Optional: Reindex existing resources
        if (options.ReindexExistingResources)
        {
            foreach (var resourceType in customResources)
            {
                _logger.LogInformation(
                    "Reindexing existing {ResourceType} resources with new SearchParameters",
                    resourceType);

                await _reindexService.ReindexResourceTypeAsync(
                    tenantId,
                    resourceType,
                    cancellationToken);
            }
        }

        _logger.LogInformation(
            "Successfully loaded IG {PackageId}@{Version} with {ResourceCount} custom resources",
            packageId, version, customResources.Count);
    }
}

public class LoadOptions
{
    /// <summary>Reindex existing resources after loading new SearchParameters (expensive)</summary>
    public bool ReindexExistingResources { get; set; } = false;

    /// <summary>Allow search parameters without matching StructureDefinition</summary>
    public bool AllowOrphanedSearchParameters { get; set; } = false;
}
```

#### SearchParameter Registration Example

```http
# After loading SQL-on-FHIR v2 IG, these SearchParameters are registered:

GET /SearchParameter?base=ViewDefinition

{
  "resourceType": "Bundle",
  "entry": [
    {
      "resource": {
        "resourceType": "SearchParameter",
        "name": "name",
        "code": "name",
        "base": ["ViewDefinition"],
        "type": "string"
      }
    },
    {
      "resource": {
        "resourceType": "SearchParameter",
        "name": "resource",
        "code": "resource",
        "base": ["ViewDefinition"],
        "type": "string"
      }
    },
    {
      "resource": {
        "resourceType": "SearchParameter",
        "name": "status",
        "code": "status",
        "base": ["ViewDefinition"],
        "type": "token"
      }
    }
  ]
}
```

### Example: ViewDefinition Workflow

**Step 1: Admin loads SQL-on-FHIR v2 IG**

```http
POST /$load-ig

{
  "packageId": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0"
}

← HTTP 200 OK
{
  "packageId": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "resourcesImported": 18,
  "customResources": ["ViewDefinition"],
  "searchParameters": 3
}
```

**Step 2: Client checks CapabilityStatement**

```http
GET /metadata

← HTTP 200 OK
{
  "resourceType": "CapabilityStatement",
  "rest": [{
    "resource": [
      {
        "type": "ViewDefinition",
        "profile": "http://hl7.org/fhir/uv/sql-on-fhir/StructureDefinition/ViewDefinition",
        "searchParam": [
          {"name": "name", "type": "string"},
          {"name": "resource", "type": "string"},
          {"name": "status", "type": "token"}
        ]
      }
    ]
  }]
}
```

**Step 3: Client creates ViewDefinition**

```http
POST /ViewDefinition

{
  "resourceType": "ViewDefinition",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [...]
}

← HTTP 201 Created
Location: /ViewDefinition/patient-demographics
```

Server validation steps:
1. Middleware accepts `ViewDefinition` (detected as custom resource)
2. Tier 1 validation: JSON structure ✅
3. Tier 2 validation: StructureDefinition loaded, validates required fields ✅
4. Stores in Resource table
5. Indexes search parameters (name, resource, status)

**Step 4: Client searches ViewDefinitions**

```http
GET /ViewDefinition?name=patient_demographics

← HTTP 200 OK
{
  "resourceType": "Bundle",
  "entry": [{
    "resource": {
      "resourceType": "ViewDefinition",
      "id": "patient-demographics",
      "name": "patient_demographics"
    }
  }]
}
```

### Integration with Unified Architecture

**Custom resources fit seamlessly into ADR-2532**:

| Component | Integration |
|-----------|-----------|
| **Package Management** | StructureDefinitions extracted from IGs → PackageResource table |
| **Validation System** | Custom StructureDefinitions used to build ValidationSchemas (Tier 2+3) |
| **Terminology Services** | Custom resource ValueSet bindings validated same as core resources |
| **CapabilityStatement** | Auto-updated when custom StructureDefinitions loaded |
| **Search Indexing** | Custom resource SearchParameters registered and indexed |
| **Multi-tenancy** | Custom resources isolated per tenant (same Resource table isolation) |
| **Caching** | StructureDefinitions cached same as core profiles (ConformanceCache) |

### Success Criteria

✅ Custom resources accepted when IG loaded
✅ Middleware accepts ViewDefinition (and other custom resources)
✅ Validation tiers applied consistently (Tier 1 always, Tier 2 if IG loaded)
✅ CapabilityStatement advertises custom resources
✅ SearchParameters for custom resources indexed and functional
✅ End-to-end workflow: Load IG → Create instance → Search → Validate

---

## Unified Data Flow Diagrams

### Resource Creation with Full Validation

```
┌───────────────────────────────────────────────────────────────────┐
│ Client: POST /Patient                                             │
│ {                                                                 │
│   "resourceType": "Patient",                                      │
│   "meta": {                                                       │
│     "profile": ["http://hl7.org/fhir/us/core/.../us-core-patient"]│
│   },                                                              │
│   "extension": [{                                                 │
│     "url": "http://hl7.org/fhir/us/core/.../us-core-race",      │
│     "extension": [{"url": "ombCategory", "valueCoding": {...}}]  │
│   }],                                                             │
│   "gender": "female"                                              │
│ }                                                                 │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ CreateOrUpdateResourceHandler                                     │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 1: Resolve Profile from Package                             │
│                                                                   │
│ ConformanceResourceResolver.ResolveAsync<StructureDefinition>(   │
│   tenantId: 1,                                                    │
│   canonical: "http://hl7.org/fhir/us/core/.../us-core-patient"  │
│ )                                                                 │
│                                                                   │
│ Fallback chain:                                                  │
│ 1. Check L1 cache (memory) → MISS                               │
│ 2. Check L2 cache (Redis) → MISS                                │
│ 3. Query PackageResource table:                                  │
│    SELECT ResourceJson FROM PackageResource                       │
│    WHERE Canonical = '...' AND PackageId = 'hl7.fhir.us.core'   │
│    → HIT: Return StructureDefinition                             │
│ 4. Populate L1 + L2 caches                                       │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 2: Build/Get Validation Schema                              │
│                                                                   │
│ ValidationSchemaResolver.GetSchema(                               │
│   "http://hl7.org/fhir/us/core/.../us-core-patient"             │
│ )                                                                 │
│                                                                   │
│ Schema cache → MISS                                              │
│                                                                   │
│ ValidationSchemaBuilder.BuildSchema(structureDefinition)         │
│ → Parses StructureDefinition.snapshot.element[]                 │
│ → Creates assertions:                                            │
│   - CardinalityAssertion (identifier: 1..*)                     │
│   - BindingAssertion (gender → administrative-gender)           │
│   - SlicingAssertion (extension sliced by url)                  │
│   - FhirPathAssertion (ele-1, patient invariants)               │
│                                                                   │
│ → Cache compiled schema                                          │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 3: Validate Resource Against Schema                         │
│                                                                   │
│ FhirValidationService.ValidateAsync(resource, schema)            │
│                                                                   │
│ For each assertion in schema:                                    │
│                                                                   │
│ 1. CardinalityAssertion (identifier)                             │
│    → Check resource.identifier exists                            │
│    → ✅ PASS (1 identifier found)                                │
│                                                                   │
│ 2. BindingAssertion (gender)                                     │
│    → Extract: system=null, code="female"                         │
│    → Call: TerminologyService.ValidateCodeAsync(                │
│         system: null,                                             │
│         code: "female",                                           │
│         valueSetUrl: "http://hl7.org/fhir/ValueSet/administrative-gender"│
│       )                                                           │
│    ↓                                                             │
│    ┌──────────────────────────────────────────────┐             │
│    │ TerminologyService                           │             │
│    │                                               │             │
│    │ 1. Resolve ValueSet from package:            │             │
│    │    ConformanceResourceResolver.ResolveAsync  │             │
│    │    → Cache HIT: ValueSet                     │             │
│    │                                               │             │
│    │ 2. Query codes (using indexes):              │             │
│    │    SELECT 1 FROM TokenSearchParam            │             │
│    │    WHERE SystemId = (SELECT SystemId...)     │             │
│    │      AND Code = 'female'                     │             │
│    │    → Found: ✅                                │             │
│    │                                               │             │
│    │ 3. Return: IsValid=true                      │             │
│    └──────────────────────────────────────────────┘             │
│    → ✅ PASS                                                     │
│                                                                   │
│ 3. SlicingAssertion (extension)                                  │
│    → Match extension by url                                      │
│    → Validate slice cardinality                                  │
│    → ✅ PASS                                                     │
│                                                                   │
│ 4. FhirPathAssertion (ele-1: hasValue() or children())          │
│    → Evaluate FHIRPath expression                                │
│    → ✅ PASS                                                     │
│                                                                   │
│ Validation Result: ✅ ALL ASSERTIONS PASSED                      │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 4: Save Resource                                            │
│                                                                   │
│ Repository.CreateAsync(resource)                                 │
│ → Insert into Resource table                                     │
│ → Index search parameters (including terminology codes)          │
│                                                                   │
│ Return: HTTP 201 Created                                         │
│ Location: /Patient/patient-123                                   │
└───────────────────────────────────────────────────────────────────┘
```

### Package Loading Flow

```
┌───────────────────────────────────────────────────────────────────┐
│ Admin: POST /admin/packages/load                                 │
│ {                                                                 │
│   "packageId": "hl7.fhir.us.core",                               │
│   "version": "5.0.1"                                             │
│ }                                                                 │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 1: Download Package                                         │
│                                                                   │
│ NpmPackageLoader.LoadPackageAsync(                               │
│   "https://packages.fhir.org/hl7.fhir.us.core/5.0.1"            │
│ )                                                                 │
│                                                                   │
│ 1. HTTP GET package.tgz                                          │
│ 2. Extract tarball → package.json + *.json files                │
│ 3. Parse package.json for metadata                               │
│                                                                   │
│ Result: ImplementationGuidePackage                               │
│   - Info: { id, version, fhirVersion, dependencies }            │
│   - Resources: { "StructureDefinition-us-core-patient.json": ... }│
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 2: Extract & Classify Resources                             │
│                                                                   │
│ PackageExtractor.ExtractResourcesAsync(package)                  │
│                                                                   │
│ For each *.json file:                                            │
│ 1. Parse JSON                                                    │
│ 2. Identify resourceType                                         │
│ 3. Extract canonical URL (for StructureDefinition/ValueSet/etc) │
│                                                                   │
│ Results:                                                         │
│ - 25 StructureDefinitions (us-core-patient, us-core-observation, etc)│
│ - 15 ValueSets (us-core-race, us-core-ethnicity, etc)           │
│ - 10 CodeSystems (us-core-provenance-participant-type, etc)     │
│ - 2 SearchParameters                                             │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 3: Import to PackageResource Table                          │
│                                                                   │
│ PackageResourceImporter.ImportAsync(resources)                   │
│                                                                   │
│ BEGIN TRANSACTION                                                │
│                                                                   │
│ For each resource:                                               │
│   INSERT INTO PackageResource (                                  │
│     PackageId,                                                   │
│     PackageVersion,                                              │
│     ResourceType,                                                │
│     Canonical,                                                   │
│     Version,                                                     │
│     ResourceId,                                                  │
│     ResourceJson,                                                │
│     FhirVersion                                                  │
│   ) VALUES (                                                     │
│     'hl7.fhir.us.core',                                         │
│     '5.0.1',                                                     │
│     'StructureDefinition',                                       │
│     'http://hl7.org/fhir/us/core/.../us-core-patient',         │
│     '5.0.1',                                                     │
│     'us-core-patient',                                           │
│     '{...json...}',                                              │
│     'R4'                                                         │
│   )                                                              │
│                                                                   │
│ COMMIT TRANSACTION                                               │
│                                                                   │
│ Result: 52 resources imported                                    │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 4: Extract Terminology Resources (Optional - Phase 2)       │
│                                                                   │
│ For each CodeSystem:                                             │
│   ConceptExtractor.ExtractAsync(codeSystem)                      │
│   → Parse concept[] array                                        │
│   → INSERT INTO Concept table (bulk)                            │
│                                                                   │
│ For each ValueSet:                                               │
│   ValueSetExpander.PreComputeAsync(valueSet)                     │
│   → Expand ValueSet                                              │
│   → INSERT INTO ValueSetExpansion table (cache)                 │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Step 5: Warm Caches                                              │
│                                                                   │
│ For each common profile:                                         │
│   ConformanceCache.SetAsync(canonical, resource)                 │
│                                                                   │
│ Result: L1 + L2 caches populated                                │
└───────────────────────────────────────────────────────────────────┘
                           ↓
┌───────────────────────────────────────────────────────────────────┐
│ Response: HTTP 200 OK                                            │
│ {                                                                 │
│   "packageId": "hl7.fhir.us.core",                               │
│   "version": "5.0.1",                                            │
│   "resourcesImported": 52,                                       │
│   "structureDefinitions": 25,                                    │
│   "valueSets": 15,                                               │
│   "codeSystems": 10,                                             │
│   "searchParameters": 2                                          │
│ }                                                                 │
└───────────────────────────────────────────────────────────────────┘
```

---

## Configuration & Tenant Management

### Tenant Configuration Model

```csharp
public record TenantValidationConfiguration
{
    public required string TenantId { get; init; }

    // Package configuration
    public IReadOnlyDictionary<FhirVersion, IReadOnlyList<string>> DefaultPackages { get; init; } =
        new Dictionary<FhirVersion, IReadOnlyList<string>>
        {
            [FhirVersion.R4] = new[] { "hl7.fhir.r4.core@4.0.1", "hl7.fhir.us.core@5.0.1" }
        };

    // Validation settings
    public ValidationStrictness Strictness { get; init; } = ValidationStrictness.Moderate;
    public bool FailOnProfileNotFound { get; init; } = false;
    public bool FailOnTerminologyUnavailable { get; init; } = false;

    // Terminology settings
    public TerminologyFallbackStrategy TerminologyFallback { get; init; } = TerminologyFallbackStrategy.Warn;
    public bool AllowExternalTerminologyServer { get; init; } = false;
    public string? ExternalTerminologyServerUrl { get; init; }

    // Cache settings
    public TimeSpan ConformanceCacheTtl { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan ValidationSchemaCacheTtl { get; init; } = TimeSpan.FromHours(1);
}

public enum ValidationStrictness
{
    Lenient,    // Only Tier 1 validation (structural)
    Moderate,   // Tier 1 + Tier 2 (spec), warnings for missing profiles
    Strict      // All tiers, errors for missing profiles
}

public enum TerminologyFallbackStrategy
{
    Fail,       // Return error if terminology unavailable
    Warn,       // Return warning, allow resource
    Ignore      // Skip terminology validation entirely
}
```

### Configuration Examples

**Development (fast, lenient)**:
```json
{
  "tenantId": "dev",
  "strictness": "Lenient",
  "failOnProfileNotFound": false,
  "failOnTerminologyUnavailable": false,
  "terminologyFallback": "Ignore",
  "defaultPackages": {
    "R4": ["hl7.fhir.r4.core@4.0.1"]
  }
}
```

**Staging (realistic, moderate)**:
```json
{
  "tenantId": "staging",
  "strictness": "Moderate",
  "failOnProfileNotFound": false,
  "failOnTerminologyUnavailable": false,
  "terminologyFallback": "Warn",
  "defaultPackages": {
    "R4": ["hl7.fhir.r4.core@4.0.1", "hl7.fhir.us.core@5.0.1"]
  }
}
```

**Production (strict, compliant)**:
```json
{
  "tenantId": "prod",
  "strictness": "Strict",
  "failOnProfileNotFound": true,
  "failOnTerminologyUnavailable": false,
  "terminologyFallback": "Warn",
  "allowExternalTerminologyServer": true,
  "externalTerminologyServerUrl": "https://tx.fhir.org/r4",
  "defaultPackages": {
    "R4": ["hl7.fhir.r4.core@4.0.1", "hl7.fhir.us.core@5.0.1", "hl7.fhir.us.mcode@2.0.0"]
  }
}
```

---

## Performance Targets & Monitoring

### Performance SLAs by Phase

| Operation | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Target |
|-----------|---------|---------|---------|---------|--------|
| **Package Load** (first time) | N/A | 10s | 10s | 10s | <15s for US Core |
| **Package Load** (cached) | N/A | 500ms | 100ms | 50ms | <100ms |
| **Profile Resolution** (cache miss) | N/A | 200ms | 50ms | 20ms | <50ms |
| **Profile Resolution** (cache hit) | N/A | N/A | 5ms | 2ms | <5ms |
| **Validation** (Tier 1 only) | 20ms | 20ms | 15ms | 15ms | <25ms |
| **Validation** (Tier 1+2, no profiles) | N/A | 50ms | 40ms | 40ms | <50ms |
| **Validation** (Full with profile) | N/A | N/A | 200ms | 150ms | <200ms |
| **$validate-code** | N/A | 15ms | 10ms | 5ms | <10ms |
| **$expand** (small <1K) | N/A | 80ms | 50ms | 20ms | <50ms |
| **$expand** (large >10K, cached) | N/A | N/A | N/A | 100ms | <200ms |

### Monitoring Metrics

```csharp
public static class ValidationMetrics
{
    // Package Management
    public static Counter<long> PackageLoadRequests { get; } =
        Meter.CreateCounter<long>("package.load.requests");

    public static Histogram<double> PackageLoadDuration { get; } =
        Meter.CreateHistogram<double>("package.load.duration", "ms");

    public static Counter<long> PackageResourceImports { get; } =
        Meter.CreateCounter<long>("package.resource.imports");

    // Conformance Resolution
    public static Counter<long> ConformanceResolutions { get; } =
        Meter.CreateCounter<long>("conformance.resolutions");

    public static Counter<long> ConformanceCacheHits { get; } =
        Meter.CreateCounter<long>("conformance.cache.hits");

    public static Counter<long> ConformanceCacheMisses { get; } =
        Meter.CreateCounter<long>("conformance.cache.misses");

    // Validation
    public static Histogram<double> ValidationDuration { get; } =
        Meter.CreateHistogram<double>("validation.duration", "ms");

    public static Counter<long> ValidationFailures { get; } =
        Meter.CreateCounter<long>("validation.failures");

    public static Counter<long> ProfileValidations { get; } =
        Meter.CreateCounter<long>("validation.profile.count");

    // Terminology
    public static Histogram<double> TerminologyValidationDuration { get; } =
        Meter.CreateHistogram<double>("terminology.validation.duration", "ms");

    public static Histogram<double> ValueSetExpansionDuration { get; } =
        Meter.CreateHistogram<double>("terminology.expansion.duration", "ms");

    public static Counter<long> TerminologyServiceCalls { get; } =
        Meter.CreateCounter<long>("terminology.service.calls");
}
```

### Alerting Thresholds

```yaml
# Application Insights / Prometheus alerts
alerts:
  - name: HighValidationLatency
    condition: validation.duration.p95 > 500ms
    severity: warning

  - name: ConformanceCacheMissRate
    condition: (cache.misses / (cache.hits + cache.misses)) > 0.3
    severity: warning

  - name: PackageLoadFailures
    condition: package.load.errors > 5 in 10m
    severity: critical

  - name: ValidationFailureSpike
    condition: validation.failures > 100 in 5m
    severity: warning
```

---

## Testing Strategy

### Unit Tests

```csharp
// Package Management
[Fact]
public async Task LoadPackage_ValidNpmPackage_ExtractsAllResources()
{
    // Arrange
    var packageUrl = new Uri("https://packages.fhir.org/hl7.fhir.us.core/5.0.1");

    // Act
    var package = await _loader.LoadPackageAsync(packageUrl, _ct);

    // Assert
    package.Info.Id.Should().Be("hl7.fhir.us.core");
    package.Info.Version.Should().Be("5.0.1");
    package.Resources.Should().ContainKey("StructureDefinition-us-core-patient.json");
}

// Conformance Resolution
[Fact]
public async Task ResolveAsync_ProfileInPackage_ReturnsStructureDefinition()
{
    // Arrange
    await LoadTestPackage("hl7.fhir.us.core", "5.0.1");
    var canonical = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";

    // Act
    var result = await _resolver.ResolveAsync<StructureDefinition>(
        "tenant1", canonical, version: null, _ct);

    // Assert
    result.Should().NotBeNull();
    result!.Url.Should().Be(canonical);
}

// Validation Schema Building
[Fact]
public async Task BuildSchema_USCorePatient_ContainsBindingAssertions()
{
    // Arrange
    var structureDef = await LoadStructureDefinition("us-core-patient");

    // Act
    var schema = _builder.BuildSchema(structureDef);

    // Assert
    schema.Assertions.Should().Contain(a => a is BindingAssertion);
    var bindingAssertion = schema.Assertions.OfType<BindingAssertion>()
        .FirstOrDefault(a => a.ElementPath == "Patient.gender");
    bindingAssertion.Should().NotBeNull();
    bindingAssertion!.ValueSetUrl.Should().Be("http://hl7.org/fhir/ValueSet/administrative-gender");
}

// Binding Validation
[Fact]
public async Task ValidateAsync_InvalidGenderCode_ReturnsError()
{
    // Arrange
    var resource = CreatePatient(gender: "invalid-code");
    var schema = await GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");

    // Act
    var result = await _validator.ValidateAsync(resource, schema, _ct);

    // Assert
    result.IsValid.Should().BeFalse();
    result.Issues.Should().Contain(i =>
        i.Severity == IssueSeverity.Error &&
        i.Expression == "Patient.gender");
}
```

### Integration Tests

```csharp
[Collection("Database")]
public class USCoreValidationIntegrationTests : IAsyncLifetime
{
    private readonly TestServer _server;
    private readonly HttpClient _client;

    public async Task InitializeAsync()
    {
        // Load US Core package before tests
        await _client.PostAsJsonAsync("/admin/packages/load", new
        {
            packageId = "hl7.fhir.us.core",
            version = "5.0.1"
        });
    }

    [Fact]
    public async Task CreatePatient_USCoreProfile_ValidatesSuccessfully()
    {
        // Arrange
        var patient = new
        {
            resourceType = "Patient",
            meta = new
            {
                profile = new[] { "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient" }
            },
            identifier = new[] { /* ... */ },
            name = new[] { /* ... */ },
            gender = "female"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/Patient", patient);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatePatient_MissingRequiredElement_Returns400()
    {
        // Arrange - missing identifier (required by US Core)
        var patient = new
        {
            resourceType = "Patient",
            meta = new
            {
                profile = new[] { "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient" }
            },
            name = new[] { /* ... */ },
            gender = "female"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/Patient", patient);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var outcome = await response.Content.ReadFromJsonAsync<OperationOutcome>();
        outcome!.Issue.Should().Contain(i =>
            i.Severity == IssueSeverity.Error &&
            i.Diagnostics.Contains("identifier"));
    }
}
```

### Performance Tests

```csharp
[Fact]
public async Task ValidationPerformance_USCorePatient_CompletesUnder200ms()
{
    // Arrange
    var patient = CreateValidUSCorePatient();
    var iterations = 100;
    var stopwatch = Stopwatch.StartNew();

    // Act
    for (int i = 0; i < iterations; i++)
    {
        await _client.PostAsJsonAsync("/Patient", patient);
    }

    stopwatch.Stop();
    var avgDuration = stopwatch.ElapsedMilliseconds / iterations;

    // Assert
    avgDuration.Should().BeLessThan(200, "validation should complete in <200ms");
}
```

---

## Migration & Rollout Strategy

### Phase 1 Rollout (Foundation)

**Week 1: Development Environment**
- Deploy PackageResource table migration
- Deploy terminology indexes
- Test package loading with small packages

**Week 2: Staging Environment**
- Deploy to staging
- Load US Core 5.0.1
- Smoke test conformance resolution

**Week 3-4: Production Deployment**
- Deploy migration during maintenance window
- Load packages asynchronously (background job)
- Monitor performance metrics

### Phase 2-4 Rollout (Gradual Feature Enablement)

**Feature Flags**:
```json
{
  "features": {
    "packageManagement": {
      "enabled": true,
      "allowedPackages": ["hl7.fhir.r4.core", "hl7.fhir.us.core"]
    },
    "profileValidation": {
      "enabled": false,  // Enable per-tenant
      "strictness": "Moderate"
    },
    "terminologyServices": {
      "enabled": true,
      "operations": ["validate-code", "expand"]  // Gradual rollout
    }
  }
}
```

**Tenant Opt-In**:
- Phase 2: Beta tenants only
- Phase 3: Opt-in for production tenants
- Phase 4: Default enabled, opt-out available

---

## Success Criteria

### Phase 1
- ✅ US Core 5.0.1 package loaded and queryable
- ✅ Conformance cache hit rate >80%
- ✅ Package loading completes in <15 seconds

### Phase 2
- ✅ Basic validation (Tier 1+2) working
- ✅ $validate-code and $expand operations functional
- ✅ Validation latency <50ms (p95)

### Phase 3
- ✅ Profile validation working with US Core
- ✅ Binding validation integrated
- ✅ End-to-end validation latency <200ms (p95)

### Phase 4
- ✅ Slicing and extension validation working
- ✅ All 5 terminology operations implemented
- ✅ Large terminology imports (LOINC, SNOMED) successful

---

## Risks & Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| **Package download failures** | Can't load IGs | MEDIUM | Local package cache + retry logic + fallback to local files |
| **Large package memory usage** | OOM during import | MEDIUM | Streaming extraction + chunked imports + background jobs |
| **Schema compilation performance** | Slow first-request latency | HIGH | Pre-compile common schemas on startup + cache aggressively |
| **Terminology query performance** | Slow validation | MEDIUM | Start with indexes (Phase 1), upgrade to specialized tables (Phase 2) |
| **Cache invalidation bugs** | Stale profiles served | LOW | Immutable package resources + version-specific cache keys |
| **Circular dependencies in packages** | Stack overflow | LOW | Dependency graph validation + max depth limit |
| **External terminology unavailable** | Validation fails | MEDIUM | Fallback chain: local → cache → external + graceful degradation |

---

## Conclusion

This unified architecture provides a **cohesive solution** for package management, validation, and terminology services:

✅ **Shared Infrastructure**: Single cache, resolver, and storage layer
✅ **Phased Implementation**: Deliver value incrementally (3-4 week phases)
✅ **Performance Optimized**: Two-tier caching, pre-compiled schemas, indexed queries
✅ **Extensible**: Easy to add new packages, profiles, terminology sources
✅ **Production Ready**: Feature flags, monitoring, graceful degradation

**Recommendation**: **Proceed with Phase 1 immediately** (3-4 weeks). The shared infrastructure benefits all three systems, and early delivery of package loading enables validation and terminology to build on solid foundations.

---

## Appendix: File Structure

```
src/
├── Ignixa.PackageManagement/
│   ├── NpmPackageLoader.cs
│   ├── PackageExtractor.cs
│   ├── PackageResourceImporter.cs
│   ├── IImplementationGuideProvider.cs
│   └── ImplementationGuideProvider.cs
│
├── Ignixa.Domain/
│   ├── Caching/
│   │   ├── IFhirConformanceCache.cs
│   │   ├── TwoTierConformanceCache.cs
│   │   └── ConformanceResourceResolver.cs
│   └── Abstractions/
│       └── IConformanceResourceResolver.cs
│
├── Ignixa.Validation/
│   ├── Schema/
│   │   ├── IValidationSchemaResolver.cs
│   │   ├── ValidationSchema.cs
│   │   ├── ValidationSchemaBuilder.cs
│   │   └── CachedValidationSchemaResolver.cs
│   ├── Assertions/
│   │   ├── IAssertion.cs
│   │   ├── CardinalityAssertion.cs
│   │   ├── FhirPathAssertion.cs
│   │   ├── BindingAssertion.cs           (calls ITerminologyService)
│   │   ├── SlicingAssertion.cs
│   │   └── ExtensionAssertion.cs
│   └── Services/
│       └── FhirValidationService.cs
│
├── Ignixa.Application/
│   ├── Features/
│   │   ├── Terminology/
│   │   │   ├── ValidateCodeQuery.cs
│   │   │   ├── ValidateCodeHandler.cs
│   │   │   ├── ExpandValueSetQuery.cs
│   │   │   ├── ExpandValueSetHandler.cs
│   │   │   ├── LookupCodeQuery.cs
│   │   │   └── LookupCodeHandler.cs
│   │   └── Packages/
│   │       ├── LoadPackageCommand.cs
│   │       └── LoadPackageHandler.cs
│   └── Behaviors/
│       └── ValidationBehavior.cs          (integrated validation)
│
├── Ignixa.Api/
│   └── Endpoints/
│       ├── TerminologyEndpoints.cs
│       └── PackageManagementEndpoints.cs
│
└── Ignixa.DataLayer.SqlEntityFramework/
    ├── Entities/
    │   ├── PackageResourceEntity.cs
    │   ├── ConceptEntity.cs               (Phase 2)
    │   └── ValueSetExpansionEntity.cs     (Phase 2)
    └── Migrations/
        ├── 20250108_AddPackageResourceTable.cs
        ├── 20250108_AddTerminologyIndexes.cs
        └── 20250215_AddTerminologyTables.cs (Phase 2)
```

---

**Document Status**: PROPOSED
**Last Updated**: 2025-01-08
**Next Review**: After Phase 1 completion
**Owner**: Ignixa Development Team
