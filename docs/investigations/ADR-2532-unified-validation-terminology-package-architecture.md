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
