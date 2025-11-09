# Investigation: ViewDefinition Custom Resource Support via Implementation Guide Loading

**Status**: Proposed
**Date**: 2025-01-08
**Author**: Claude Code (Investigation)
**Related**: ADR-2531 (to be created), SQL-on-FHIR v2 Implementation

---

## Executive Summary

This investigation analyzes how to properly support **ViewDefinition**, a custom resource from the SQL-on-FHIR v2 Implementation Guide, in our FHIR server. While our dynamic routing architecture already handles ViewDefinition CRUD operations, we lack proper validation, search parameters, and CapabilityStatement advertisement.

**Key Finding**: ViewDefinition already works through our generic `/{resourceType}` endpoints. The question is how to formalize this support for production readiness.

**Recommendation**: Implement **Implementation Guide (IG) Loading** infrastructure. This industry-standard approach enables:
- Automatic validation using StructureDefinitions from the IG
- Search parameter support from SearchParameter resources in the IG
- CapabilityStatement advertisement of custom resources
- Scalability to any FHIR IG (US Core, Genomics, etc.)
- Future-proof support for FHIR R6 "Additional Resources"

**Estimated Effort**: 32-48 hours (4-6 days) over 4 phases

---

## Problem Statement

### What is ViewDefinition?

**ViewDefinition** is a custom resource type defined by the SQL-on-FHIR v2 Implementation Guide (IG):
- **Purpose**: Defines FHIRPath-based transformations of FHIR resources into tabular (SQL-like) views
- **Origin**: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/
- **Type**: Logical model / custom resource (not part of core FHIR R4/R4B/R5/R6)
- **Use Case**: Analytics, bulk export, data warehousing

### Current Implementation Status

We have **already implemented** ViewDefinition support:

**✅ Working**:
1. **Parsing**: `src/Ignixa.SqlOnFhir/Parsing/ViewDefinitionExpressionParser.cs`
   - Parses ViewDefinition JSON → ViewDefinitionExpression AST
   - Validates structure, compiles FHIRPath expressions

2. **Evaluation**: `src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluator.cs`
   - Transforms FHIR resources → tabular rows
   - Handles forEach unnesting, nested selects, unionAll

3. **Schema Extraction**: `src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaEvaluator.cs`
   - Extracts output column schema from ViewDefinitions
   - Supports Parquet schema generation

4. **Storage**: `src/Ignixa.DataLayer.BlobStorage/ViewDefinitionLoader.cs`
   - Loads ViewDefinition resources using standard FHIR repository
   - Works through our dynamic routing (no special handling needed)

5. **Export Integration**: `src/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs`
   - `$export` operation with `_viewDefinition` parameter
   - Transforms FHIR → Parquet using ViewDefinitions

**Evidence it already works**:
```csharp
// ViewDefinitionLoader.cs - uses standard FHIR read
var resourceKey = new ResourceKey("ViewDefinition", viewDefinitionId);
var searchResult = await repository.GetAsync(resourceKey, cancellationToken);
```

**❌ Missing**:
1. **FHIR REST API**: No `POST /ViewDefinition`, `GET /ViewDefinition/{id}`, etc.
2. **Validation**: No StructureDefinition-based validation
3. **Search Parameters**: No search support (`GET /ViewDefinition?name=...`)
4. **CapabilityStatement**: Not advertised in server metadata
5. **Discovery**: No way to list available ViewDefinitions

### Why This Matters

**Current Workflow** (manual):
```bash
# 1. Manually insert ViewDefinition JSON into datastore
INSERT INTO Resources (TenantId, ResourceType, ResourceId, JsonData) VALUES (1, 'ViewDefinition', 'patient-demographics', '...');

# 2. Reference in export request
POST /tenant/1/$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics
```

**Desired Workflow** (FHIR-compliant):
```bash
# 1. Upload ViewDefinition via FHIR REST API
POST /ViewDefinition
Content-Type: application/fhir+json
{ "resourceType": "ViewDefinition", "name": "patient_demographics", ... }

# 2. Search for ViewDefinitions
GET /ViewDefinition?name=patient_demographics

# 3. Reference in export (same as current)
POST /tenant/1/$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics
```

---

## Approach Analysis

We analyzed four approaches to formalize ViewDefinition support:

### Approach A: Dynamic Custom Resource Support (Current State)

**Description**: Rely on our existing generic routing to handle ViewDefinition.

**Architecture**:
```
Request: POST /ViewDefinition
    ↓
Generic Routing: /{resourceType}
    ↓
Dynamic Endpoint Handler
    ↓
Repository: CreateOrUpdateResourceAsync()
    ↓
Storage: /data/{tenantId}/R4/ViewDefinition/{id}.json
```

**How It Works**:
- Our server uses generic endpoints: `/{resourceType}`, `/{resourceType}/{id}`, etc.
- These endpoints work for **any** resource type, including custom ones
- No code changes needed for basic CRUD operations

**Feasibility**: ✅ Already working

**Pros**:
- ✅ Zero code changes needed
- ✅ Works today for basic CRUD
- ✅ Multi-tenant by default
- ✅ Multi-version compatible
- ✅ Simple to understand

**Cons**:
- ❌ No validation (accepts any JSON with `resourceType: "ViewDefinition"`)
- ❌ No search parameters (can't search by name, resource, status)
- ❌ Not advertised in CapabilityStatement
- ❌ No discovery mechanism
- ❌ Not FHIR-compliant (server doesn't "know" about ViewDefinition)

**Complexity**: 1/5 (trivial - already done)

**Time Estimate**: 0 hours

**Storage Architecture**:
```
FileSystem Storage:
/data/
  ├── tenant-1/
  │   └── R4/
  │       ├── Patient/
  │       │   └── 123.json
  │       └── ViewDefinition/  ← Works automatically
  │           └── patient-demographics.json

SQL Storage:
Resources Table:
| TenantId | ResourceType     | ResourceId            | JsonData              |
|----------|------------------|-----------------------|-----------------------|
| 1        | Patient          | 123                   | {"resourceType":...}  |
| 1        | ViewDefinition   | patient-demographics  | {"resourceType":...}  | ← Works
```

**Validation Strategy**: None (parser-only structural validation during evaluation)

**Search Parameter Handling**: None

**Multi-Tenant Considerations**: ✅ Works (standard resource isolation)

**Multi-Version Considerations**: ✅ Works (ViewDefinition is version-agnostic)

**Recommendation**: ❌ Not sufficient for production use

---

### Approach B: StructureDefinition-Based

**Description**: Manually load the ViewDefinition StructureDefinition from the SQL-on-FHIR v2 IG and use it for validation.

**Architecture**:
```
Startup:
1. Download ViewDefinition StructureDefinition from IG
2. Load into CompositeSchemaProvider
3. Register with validation system

Runtime:
POST /ViewDefinition → Validate using StructureDefinition → Store
```

**How It Works**:
```csharp
// Startup configuration
public class ViewDefinitionStructureDefinitionLoader
{
    public async Task LoadAsync(IFhirVersionContext context)
    {
        // 1. Fetch StructureDefinition from SQL-on-FHIR v2 IG
        var url = "https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/StructureDefinition-ViewDefinition.json";
        var structureDefinition = await FetchStructureDefinitionAsync(url);

        // 2. Register with schema provider
        var schemaProvider = context.GetSchemaProvider(FhirSpecification.R4);
        ((CompositeSchemaProvider)schemaProvider).AddCustomStructureDefinition(structureDefinition);

        // 3. Enable validation
        var validator = new FhirValidator(schemaProvider);
        // Validation now works for ViewDefinition
    }
}
```

**Feasibility**: ✅ Viable but manual

**Pros**:
- ✅ Enables Tier 2 validation (StructureDefinition-based)
- ✅ Relatively simple implementation
- ✅ Works with existing validation infrastructure
- ✅ Can be done per-tenant or globally

**Cons**:
- ❌ Manual setup required (config file or startup code)
- ❌ No search parameters (would need manual registration)
- ❌ Doesn't scale to multiple IGs
- ❌ Still not advertised in CapabilityStatement
- ❌ No automatic discovery of custom resources
- ❌ Fragile (must manually update when IG updates)

**Complexity**: 3/5 (moderate - requires schema provider modifications)

**Time Estimate**: 16-24 hours

**Implementation Tasks**:
1. Fetch StructureDefinition from IG (2-4 hours)
2. Modify `CompositeSchemaProvider` to accept custom definitions (4-6 hours)
3. Register with validation system (2-3 hours)
4. Update configuration system (3-4 hours)
5. Testing and documentation (5-7 hours)

**File Modifications**:
- **Modify**: `src/Ignixa.Specification/CompositeSchemaProvider.cs`
  - Add `AddCustomStructureDefinition(ISourceNode structureDefinition)` method
- **Create**: `src/Ignixa.Application/Configuration/CustomResourceConfiguration.cs`
  - Define custom resource configurations
- **Modify**: `src/Ignixa.Api/Program.cs`
  - Register custom StructureDefinitions at startup
- **Create**: `test/Ignixa.Api.Tests/CustomResourceValidationTests.cs`
  - Validate custom resource validation works

**Storage Architecture**: Same as Approach A (no changes)

**Validation Strategy**:
```csharp
// Tier 2 validation using StructureDefinition
var validator = new FhirValidator(schemaProvider);
var result = validator.Validate(viewDefinitionResource, ValidationTier.Spec);

// Can now validate:
// - Required fields (name, resource, select)
// - Data types (string, array, object)
// - Cardinality (0..1, 1..*, etc.)
```

**Search Parameter Handling**: Still manual (would need separate implementation)

**Multi-Tenant Considerations**: ✅ Works (can register per-tenant or globally)

**Multi-Version Considerations**: ⚠️ Partial (ViewDefinition StructureDefinition targets specific FHIR version)

**Recommendation**: ⚠️ Viable but incomplete solution

---

### Approach C: Hardcoded SQL-on-FHIR Extension

**Description**: Add ViewDefinition as a first-class resource type in code, with dedicated handlers and endpoints.

**Architecture**:
```
Request: POST /ViewDefinition
    ↓
ViewDefinitionEndpoints.cs (dedicated)
    ↓
CreateViewDefinitionHandler
    ↓
ViewDefinitionRepository (specialized)
    ↓
Storage: Same as other resources
```

**How It Works**:
```csharp
// File: src/Ignixa.Api/Endpoints/ViewDefinitionEndpoints.cs
public static class ViewDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapViewDefinitionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/ViewDefinition", CreateViewDefinitionAsync);
        endpoints.MapGet("/ViewDefinition/{id}", GetViewDefinitionAsync);
        endpoints.MapPut("/ViewDefinition/{id}", UpdateViewDefinitionAsync);
        endpoints.MapDelete("/ViewDefinition/{id}", DeleteViewDefinitionAsync);
        endpoints.MapGet("/ViewDefinition", SearchViewDefinitionsAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateViewDefinitionAsync(
        HttpContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var command = new CreateViewDefinitionCommand(/* ... */);
        var result = await mediator.SendAsync(command, cancellationToken);
        return Results.Created($"/ViewDefinition/{result.Id}", result);
    }
}

// File: src/Ignixa.Application/Features/ViewDefinition/CreateViewDefinitionHandler.cs
public class CreateViewDefinitionHandler : IRequestHandler<CreateViewDefinitionCommand, ViewDefinitionResult>
{
    public async Task<ViewDefinitionResult> HandleAsync(
        CreateViewDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Validate using ViewDefinitionExpressionParser
        var viewExpression = ViewDefinitionExpressionParser.Parse(request.ViewDefinitionNode);

        // 2. Store using repository
        var resourceKey = new ResourceKey("ViewDefinition", request.Id);
        await _repository.CreateAsync(resourceKey, request.JsonData, cancellationToken);

        // 3. Return result
        return new ViewDefinitionResult { Id = request.Id };
    }
}
```

**Feasibility**: ✅ Technically feasible but architecturally wrong

**Pros**:
- ✅ Type-safe (strongly-typed ViewDefinition models)
- ✅ Full control over validation
- ✅ Can add ViewDefinition-specific operations
- ✅ Easy to add to CapabilityStatement
- ✅ Can implement custom search parameters

**Cons**:
- ❌ Violates our dynamic routing architecture
- ❌ Creates special case (why ViewDefinition but not other custom resources?)
- ❌ Doesn't scale (what about other IGs?)
- ❌ High maintenance burden (must update code for each IG)
- ❌ Breaks multi-version elegance
- ❌ Requires duplicate endpoint logic
- ❌ Doesn't align with FHIR standards (custom resources should be dynamic)

**Complexity**: 4/5 (complex - lots of boilerplate code)

**Time Estimate**: 40-60 hours

**Implementation Tasks**:
1. Create ViewDefinition endpoints (8-12 hours)
2. Create ViewDefinition command/query handlers (12-16 hours)
3. Create ViewDefinition validator (4-6 hours)
4. Add search parameter support (8-12 hours)
5. Update CapabilityStatement (2-3 hours)
6. Testing and documentation (6-11 hours)

**File Modifications**:
- **Create**: `src/Ignixa.Api/Endpoints/ViewDefinitionEndpoints.cs` (~200 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/CreateViewDefinitionCommand.cs` (~50 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/CreateViewDefinitionHandler.cs` (~150 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/GetViewDefinitionQuery.cs` (~50 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/GetViewDefinitionHandler.cs` (~100 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/UpdateViewDefinitionCommand.cs` (~50 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/UpdateViewDefinitionHandler.cs` (~150 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/DeleteViewDefinitionCommand.cs` (~50 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/DeleteViewDefinitionHandler.cs` (~100 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/SearchViewDefinitionsQuery.cs` (~50 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/SearchViewDefinitionsHandler.cs` (~200 lines)
- **Create**: `src/Ignixa.Application/Features/ViewDefinition/ViewDefinitionValidator.cs` (~150 lines)
- **Modify**: `src/Ignixa.Api/Program.cs` - Register handlers and endpoints
- **Modify**: `src/Ignixa.Api/Infrastructure/CapabilityStatementBuilder.cs` - Add ViewDefinition
- **Create**: 6 test files (~1,200 lines total)

**Storage Architecture**: Same as Approach A (no changes)

**Validation Strategy**: Custom validator using `ViewDefinitionExpressionParser`

**Search Parameter Handling**: Hardcoded in SearchViewDefinitionsHandler

**Multi-Tenant Considerations**: ✅ Works (standard repository isolation)

**Multi-Version Considerations**: ✅ Works (ViewDefinition is version-agnostic)

**Recommendation**: ❌ Not recommended - violates architecture principles

---

### Approach D: Implementation Guide (IG) Loading ⭐ **RECOMMENDED**

**Description**: Build IG loading infrastructure to automatically support any FHIR Implementation Guide, including SQL-on-FHIR v2.

**Architecture**:
```
Admin: POST /$load-ig { "url": "hl7.fhir.uv.sql-on-fhir#2.0.0" }
    ↓
ImplementationGuideLoader
    ↓
Download from packages.fhir.org
    ↓
Parse package.json + StructureDefinitions + SearchParameters
    ↓
Register with CompositeSchemaProvider + SearchIndexer
    ↓
Auto-detect custom resources → Add to CapabilityStatement
    ↓
Result: ViewDefinition fully supported (CRUD, validation, search)
```

**How It Works**:
```csharp
// 1. Admin loads IG via REST API
POST /$load-ig
Content-Type: application/json
{
  "packageId": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "scope": "global" // or "tenant:{id}"
}

// 2. Server downloads FHIR package from packages.fhir.org
{
  "name": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "canonical": "http://hl7.org/fhir/uv/sql-on-fhir",
  "resources": [
    { "resourceType": "StructureDefinition", "id": "ViewDefinition", ... },
    { "resourceType": "SearchParameter", "id": "ViewDefinition-name", ... },
    { "resourceType": "SearchParameter", "id": "ViewDefinition-resource", ... }
  ]
}

// 3. Server automatically:
// - Registers StructureDefinitions (validation)
// - Registers SearchParameters (search)
// - Detects custom resources (ViewDefinition)
// - Updates CapabilityStatement

// 4. ViewDefinition now fully supported:
POST /ViewDefinition        // ✅ Create (validated using StructureDefinition)
GET /ViewDefinition/{id}    // ✅ Read (works)
PUT /ViewDefinition/{id}    // ✅ Update (validated)
DELETE /ViewDefinition/{id} // ✅ Delete (works)
GET /ViewDefinition?name=patient_demographics // ✅ Search (using SearchParameters)
```

**Key Components**:

**ImplementationGuideLoader**:
```csharp
public class ImplementationGuideLoader
{
    private readonly FhirPackageDownloader _packageDownloader;
    private readonly IFhirVersionContext _versionContext;
    private readonly ImplementationGuideRegistry _registry;

    public async Task<LoadResult> LoadImplementationGuideAsync(
        string packageId,
        string version,
        string scope, // "global" or "tenant:{id}"
        CancellationToken cancellationToken)
    {
        // 1. Download package from packages.fhir.org
        var package = await _packageDownloader.DownloadAsync(packageId, version, cancellationToken);

        // 2. Parse resources from package
        var structureDefinitions = package.GetStructureDefinitions();
        var searchParameters = package.GetSearchParameters();
        var customResources = DetectCustomResources(structureDefinitions);

        // 3. Register with schema provider
        var schemaProvider = GetSchemaProviderForScope(scope);
        foreach (var sd in structureDefinitions)
        {
            schemaProvider.AddStructureDefinition(sd);
        }

        // 4. Register with search indexer
        var searchIndexer = GetSearchIndexerForScope(scope);
        foreach (var sp in searchParameters)
        {
            searchIndexer.AddSearchParameter(sp);
        }

        // 5. Register in IG registry
        _registry.Register(packageId, version, scope, customResources);

        // 6. Update CapabilityStatement cache
        _capabilityStatementCache.Invalidate();

        return LoadResult.Success(customResources);
    }

    private List<string> DetectCustomResources(List<StructureDefinition> structureDefinitions)
    {
        // Custom resources are StructureDefinitions with:
        // - kind = "resource"
        // - baseDefinition NOT in core FHIR (Patient, Observation, etc.)
        // - OR baseDefinition = "http://hl7.org/fhir/StructureDefinition/DomainResource"

        var coreResources = new HashSet<string> { "Patient", "Observation", ... };

        return structureDefinitions
            .Where(sd => sd.Kind == "resource")
            .Where(sd => !coreResources.Contains(sd.Type))
            .Select(sd => sd.Type)
            .ToList();
    }
}
```

**FhirPackageDownloader**:
```csharp
public class FhirPackageDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;

    public async Task<FhirPackage> DownloadAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken)
    {
        // 1. Check local cache
        var cachedPath = Path.Combine(_cacheDirectory, $"{packageId}-{version}.tgz");
        if (File.Exists(cachedPath))
        {
            return await LoadFromCacheAsync(cachedPath, cancellationToken);
        }

        // 2. Download from packages.fhir.org
        var url = $"https://packages.fhir.org/{packageId}/{version}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        // 3. Save to cache
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        await File.WriteAllBytesAsync(cachedPath, bytes, cancellationToken);

        // 4. Extract and parse
        return await ExtractPackageAsync(cachedPath, cancellationToken);
    }

    private async Task<FhirPackage> ExtractPackageAsync(
        string tgzPath,
        CancellationToken cancellationToken)
    {
        // Extract .tgz file
        // Parse package.json
        // Load StructureDefinitions from package/StructureDefinition-*.json
        // Load SearchParameters from package/SearchParameter-*.json
        return new FhirPackage(...);
    }
}
```

**CompositeSchemaProvider Updates**:
```csharp
// Modify existing CompositeSchemaProvider to support dynamic additions
public class CompositeSchemaProvider : IStructureDefinitionSummaryProvider, IFhirSchemaProvider
{
    private readonly Dictionary<string, IStructureDefinitionSummary> _customStructures = new();

    public void AddStructureDefinition(ISourceNode structureDefinitionNode)
    {
        // Parse StructureDefinition
        var sd = StructureDefinitionParser.Parse(structureDefinitionNode);

        // Add to internal cache
        _customStructures[sd.Type] = sd;
    }

    public IStructureDefinitionSummary? Provide(string resourceType)
    {
        // Check custom structures first
        if (_customStructures.TryGetValue(resourceType, out var customSd))
        {
            return customSd;
        }

        // Fall back to generated providers
        return _generatedProvider.Provide(resourceType);
    }
}
```

**ImplementationGuideRegistry**:
```csharp
public class ImplementationGuideRegistry
{
    private readonly ConcurrentDictionary<string, LoadedImplementationGuide> _loadedIGs = new();

    public void Register(string packageId, string version, string scope, List<string> customResources)
    {
        var key = $"{packageId}#{version}@{scope}";
        _loadedIGs[key] = new LoadedImplementationGuide
        {
            PackageId = packageId,
            Version = version,
            Scope = scope,
            CustomResources = customResources,
            LoadedAt = DateTimeOffset.UtcNow
        };
    }

    public List<string> GetCustomResourcesForScope(string scope)
    {
        return _loadedIGs.Values
            .Where(ig => ig.Scope == scope || ig.Scope == "global")
            .SelectMany(ig => ig.CustomResources)
            .Distinct()
            .ToList();
    }

    public List<LoadedImplementationGuide> GetLoadedImplementationGuides(string scope)
    {
        return _loadedIGs.Values
            .Where(ig => ig.Scope == scope || ig.Scope == "global")
            .ToList();
    }
}

public record LoadedImplementationGuide
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public required string Scope { get; init; } // "global" or "tenant:{id}"
    public required List<string> CustomResources { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }
}
```

**Auto-Discovery in CapabilityStatement**:
```csharp
// Modify CapabilityStatementBuilder to include custom resources
public class CapabilityStatementBuilder
{
    private readonly ImplementationGuideRegistry _igRegistry;

    public CapabilityStatement Build(int tenantId, FhirSpecification fhirVersion)
    {
        var cs = new CapabilityStatement();

        // Add core resources (Patient, Observation, etc.)
        AddCoreResources(cs);

        // Add custom resources from loaded IGs
        var scope = $"tenant:{tenantId}";
        var customResources = _igRegistry.GetCustomResourcesForScope(scope);

        foreach (var resourceType in customResources)
        {
            cs.Rest[0].Resource.Add(new CapabilityStatement.ResourceComponent
            {
                Type = resourceType, // e.g., "ViewDefinition"
                Interaction = new List<CapabilityStatement.ResourceInteractionComponent>
                {
                    new() { Code = CapabilityStatement.TypeRestfulInteraction.Create },
                    new() { Code = CapabilityStatement.TypeRestfulInteraction.Read },
                    new() { Code = CapabilityStatement.TypeRestfulInteraction.Update },
                    new() { Code = CapabilityStatement.TypeRestfulInteraction.Delete },
                    new() { Code = CapabilityStatement.TypeRestfulInteraction.SearchType }
                },
                SearchParam = GetSearchParametersForResource(resourceType, scope)
            });
        }

        return cs;
    }
}
```

**Feasibility**: ✅ Highly feasible with significant architectural benefits

**Pros**:
- ✅ Industry standard approach (how Smile CDR, Firely, HAPI work)
- ✅ Automatic validation using StructureDefinitions from IG
- ✅ Automatic search using SearchParameters from IG
- ✅ Automatic CapabilityStatement updates
- ✅ Scales to any IG (US Core, Genomics, etc.)
- ✅ Future-proof for FHIR R6 "Additional Resources"
- ✅ Multi-tenant aware (load IGs per-tenant or globally)
- ✅ Supports IG versioning
- ✅ Aligns with FHIR best practices
- ✅ Discoverable (list loaded IGs via API)

**Cons**:
- ❌ Most complex implementation (significant upfront investment)
- ❌ Requires package management infrastructure
- ❌ Network dependency (packages.fhir.org)
- ❌ Storage overhead (cache downloaded packages)
- ❌ Learning curve for administrators

**Complexity**: 4/5 (complex but well-defined)

**Time Estimate**: 32-48 hours (4-6 days)

**Storage Architecture**:
```
FHIR Package Cache:
/data/packages/
  ├── hl7.fhir.uv.sql-on-fhir-2.0.0.tgz
  ├── hl7.fhir.us.core-6.1.0.tgz
  └── ...

IG Registry (in-memory or persistent):
{
  "loadedIGs": [
    {
      "packageId": "hl7.fhir.uv.sql-on-fhir",
      "version": "2.0.0",
      "scope": "global",
      "customResources": ["ViewDefinition"],
      "loadedAt": "2025-01-08T12:00:00Z"
    }
  ]
}

Resources (unchanged):
/data/tenant-1/R4/ViewDefinition/patient-demographics.json
```

**Validation Strategy**:
```csharp
// Tier 2 validation using StructureDefinitions from IG
var schemaProvider = _versionContext.GetSchemaProvider(fhirVersion);
// schemaProvider now includes ViewDefinition StructureDefinition from IG

var validator = new FhirValidator(schemaProvider);
var result = validator.Validate(viewDefinitionResource, ValidationTier.Spec);
```

**Search Parameter Handling**:
```csharp
// SearchParameters from IG automatically registered
GET /ViewDefinition?name=patient_demographics
GET /ViewDefinition?resource=Patient
GET /ViewDefinition?status=active

// SearchIndexer has ViewDefinition search parameters from IG
var searchIndexer = _versionContext.GetSearchIndexer(fhirVersion);
// searchIndexer now includes ViewDefinition search parameters
```

**Multi-Tenant Considerations**:
```csharp
// Load IG globally (all tenants)
POST /$load-ig { "packageId": "...", "scope": "global" }

// Load IG for specific tenant
POST /$load-ig { "packageId": "...", "scope": "tenant:1" }

// CapabilityStatement shows tenant-specific custom resources
GET /tenant/1/metadata → includes ViewDefinition
GET /tenant/2/metadata → does not include ViewDefinition (not loaded)
```

**Multi-Version Considerations**:
```csharp
// IGs target specific FHIR versions
// SQL-on-FHIR v2 targets R4/R4B/R5
// Server validates IG compatibility before loading

public async Task<LoadResult> LoadImplementationGuideAsync(...)
{
    // Check IG compatibility
    var igFhirVersion = package.FhirVersion; // e.g., "4.0.1"
    var tenantFhirVersion = tenantConfig.FhirVersion; // e.g., "4.0"

    if (!IsCompatible(igFhirVersion, tenantFhirVersion))
    {
        return LoadResult.Failure($"IG requires FHIR {igFhirVersion}, tenant uses {tenantFhirVersion}");
    }

    // Proceed with loading...
}
```

**Recommendation**: ✅ **HIGHLY RECOMMENDED** - Industry standard, future-proof, scalable

---

## Technical Deep Dive

### Storage Architecture

**ViewDefinition storage is identical to standard FHIR resources**:

```
FileSystem Storage (Development):
/data/
  ├── tenant-1/
  │   └── R4/
  │       ├── Patient/
  │       │   ├── 123.json
  │       │   └── 456.json
  │       ├── Observation/
  │       │   ├── obs-1.json
  │       │   └── obs-2.json
  │       └── ViewDefinition/  ← Same structure
  │           ├── patient-demographics.json
  │           └── observation-vitals.json
  └── tenant-2/
      └── R5/
          └── ViewDefinition/
              └── genomics-variant-view.json

SQL Server Storage (Production):
Resources Table:
┌──────────┬───────────────┬───────────────────────┬─────────────┬──────────────┬─────────────────────┐
│ TenantId │ ResourceType  │ ResourceId            │ VersionId   │ LastUpdated  │ JsonData            │
├──────────┼───────────────┼───────────────────────┼─────────────┼──────────────┼─────────────────────┤
│ 1        │ Patient       │ 123                   │ 1           │ 2025-01-01   │ {"resourceType":..} │
│ 1        │ ViewDef...    │ patient-demographics  │ 1           │ 2025-01-08   │ {"resourceType":..} │ ← Works
│ 2        │ ViewDef...    │ genomics-variant-view │ 1           │ 2025-01-08   │ {"resourceType":..} │
└──────────┴───────────────┴───────────────────────┴─────────────┴──────────────┴─────────────────────┘

Indexes (unchanged):
- PRIMARY KEY (TenantId, ResourceType, ResourceId)
- INDEX ON (TenantId, ResourceType, LastUpdated) ← ViewDefinition uses this
- FULLTEXT INDEX ON (JsonData) ← ViewDefinition searchable
```

**Key Insight**: No special storage needed. ViewDefinition is just another resource type.

### Validation Strategy

**Current State** (Approach A):
```csharp
// No validation at storage time
POST /ViewDefinition → Store directly (any JSON accepted)

// Validation only happens at evaluation time
var viewExpression = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);
// Parser throws if structure is invalid
```

**With IG Loading** (Approach D):
```csharp
// Tier 2 validation at storage time
POST /ViewDefinition
{
  "resourceType": "ViewDefinition",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [...]
}
    ↓
FhirValidator.Validate(resource, ValidationTier.Spec)
    ↓
Check against StructureDefinition from SQL-on-FHIR v2 IG:
- Required fields: name, resource, select (validated ✅)
- Data types: string, array, object (validated ✅)
- Cardinality: name is 0..1, select is 1..* (validated ✅)
- Constraints: FHIRPath invariants (validated ✅)
    ↓
If valid: Store
If invalid: Return OperationOutcome with errors
```

**Validation Tiers**:
```
Tier 1 (Fast): Universal checks only
  - JSON well-formed
  - resourceType present
  - Time: <25ms

Tier 2 (Spec): Tier 1 + StructureDefinition validation  ← IG Loading enables this
  - Required fields
  - Data types
  - Cardinality
  - Time: <200ms

Tier 3 (Profile): Tier 2 + Advanced profile validation
  - FHIRPath invariants
  - ValueSet bindings
  - Reference validation
  - Time: <1000ms
```

### Search Parameter Handling

**Current State** (Approach A):
```csharp
GET /ViewDefinition?name=patient_demographics
→ 404 Not Found (search parameters not registered)
```

**With IG Loading** (Approach D):
```csharp
// SQL-on-FHIR v2 IG defines SearchParameters:
{
  "resourceType": "SearchParameter",
  "id": "ViewDefinition-name",
  "url": "http://hl7.org/fhir/uv/sql-on-fhir/SearchParameter/ViewDefinition-name",
  "name": "name",
  "code": "name",
  "base": ["ViewDefinition"],
  "type": "string",
  "expression": "ViewDefinition.name"
}

// After loading IG:
GET /ViewDefinition?name=patient_demographics
    ↓
SearchOptionsBuilder recognizes "name" parameter (registered from IG)
    ↓
SearchIndexer indexes ViewDefinition.name field
    ↓
Query: SELECT * FROM Resources WHERE TenantId = 1 AND ResourceType = 'ViewDefinition' AND JSON_VALUE(JsonData, '$.name') = 'patient_demographics'
    ↓
Result: Returns matching ViewDefinition
```

**Common SearchParameters for ViewDefinition**:
```
name       : ViewDefinition.name (string)
resource   : ViewDefinition.resource (string)
status     : ViewDefinition.status (token)
url        : ViewDefinition.url (uri)
identifier : ViewDefinition.identifier (token)
```

### Multi-Tenant Considerations

**Global IG Loading**:
```csharp
// Load SQL-on-FHIR v2 IG globally (all tenants)
POST /$load-ig
{
  "packageId": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "scope": "global"
}

// All tenants now support ViewDefinition
GET /tenant/1/metadata → rest.resource includes "ViewDefinition"
GET /tenant/2/metadata → rest.resource includes "ViewDefinition"

// Each tenant stores their own ViewDefinitions (isolated)
/data/tenant-1/R4/ViewDefinition/patient-demographics.json
/data/tenant-2/R4/ViewDefinition/patient-demographics.json  ← Same ID, different tenant
```

**Tenant-Specific IG Loading**:
```csharp
// Load US Core IG only for tenant 1 (healthcare)
POST /$load-ig
{
  "packageId": "hl7.fhir.us.core",
  "version": "6.1.0",
  "scope": "tenant:1"
}

// Load Genomics IG only for tenant 2 (research)
POST /$load-ig
{
  "packageId": "hl7.fhir.uv.genomics-reporting",
  "version": "2.0.0",
  "scope": "tenant:2"
}

// Tenant 1 CapabilityStatement includes US Core profiles
GET /tenant/1/metadata → includes USCorePatientProfile, USCoreObservationProfile

// Tenant 2 CapabilityStatement includes Genomics profiles
GET /tenant/2/metadata → includes Variant, MolecularSequence
```

**IG Registry Queries**:
```csharp
// List loaded IGs for a tenant
GET /$loaded-igs?tenant=1
{
  "implementationGuides": [
    {
      "packageId": "hl7.fhir.uv.sql-on-fhir",
      "version": "2.0.0",
      "scope": "global",
      "customResources": ["ViewDefinition"],
      "loadedAt": "2025-01-08T12:00:00Z"
    },
    {
      "packageId": "hl7.fhir.us.core",
      "version": "6.1.0",
      "scope": "tenant:1",
      "customResources": [],
      "loadedAt": "2025-01-08T13:00:00Z"
    }
  ]
}
```

### Multi-Version Considerations

**ViewDefinition FHIR Version Compatibility**:
```json
// SQL-on-FHIR v2 package.json
{
  "name": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "canonical": "http://hl7.org/fhir/uv/sql-on-fhir",
  "fhirVersions": ["4.0.1", "4.3.0", "5.0.0"],  ← Compatible with R4, R4B, R5
  "dependencies": {
    "hl7.fhir.r4.core": "4.0.1"
  }
}
```

**Compatibility Checking**:
```csharp
public async Task<LoadResult> LoadImplementationGuideAsync(
    string packageId,
    string version,
    string scope,
    CancellationToken cancellationToken)
{
    var package = await _packageDownloader.DownloadAsync(packageId, version, cancellationToken);

    // Check IG FHIR version compatibility
    var tenantFhirVersion = await GetTenantFhirVersionAsync(scope, cancellationToken);
    if (!package.FhirVersions.Any(v => IsCompatible(v, tenantFhirVersion)))
    {
        return LoadResult.Failure(
            $"IG {packageId}#{version} requires FHIR {string.Join(", ", package.FhirVersions)}, " +
            $"but scope '{scope}' uses FHIR {tenantFhirVersion}");
    }

    // Proceed with loading...
}

private bool IsCompatible(string igFhirVersion, string tenantFhirVersion)
{
    // Major.minor version match
    var igMajorMinor = igFhirVersion.Substring(0, 3); // "4.0"
    var tenantMajorMinor = tenantFhirVersion.Substring(0, 3); // "4.0"
    return igMajorMinor == tenantMajorMinor;
}
```

**Version-Specific Schema Providers**:
```csharp
// Each FHIR version has its own schema provider
var r4SchemaProvider = _versionContext.GetSchemaProvider(FhirSpecification.R4);
var r5SchemaProvider = _versionContext.GetSchemaProvider(FhirSpecification.R5);

// IG StructureDefinitions added to appropriate schema provider
if (igFhirVersion == "4.0.1")
{
    r4SchemaProvider.AddStructureDefinition(viewDefinitionSD);
}
else if (igFhirVersion == "5.0.0")
{
    r5SchemaProvider.AddStructureDefinition(viewDefinitionSD);
}

// Tenant 1 (R4) gets R4 ViewDefinition support
// Tenant 2 (R5) gets R5 ViewDefinition support
```

---

## Recommended Approach

### Decision: Approach D - Implementation Guide (IG) Loading

**Why This is the Best Choice**:

1. **Industry Standard**: This is how major FHIR servers handle custom resources:
   - **Smile CDR**: IG loading via admin UI
   - **Firely Server**: Package management with IG support
   - **HAPI FHIR**: NPM package loading
   - **Microsoft FHIR Server**: IG validation support

2. **Future-Proof**: FHIR R6 introduces "Additional Resources" - formally registered resources outside core spec. Our IG loading pattern directly supports this future direction.

3. **Scalable**: Not limited to ViewDefinition. Works for:
   - **US Core IG**: Patient, Observation, Condition profiles
   - **Genomics IG**: Variant, MolecularSequence resources
   - **IPS IG**: International Patient Summary profiles
   - **Any custom IG**: Organization-specific resources

4. **Automatic Capabilities**:
   - **Validation**: StructureDefinitions from IG enable Tier 2 validation
   - **Search**: SearchParameters from IG enable resource discovery
   - **CapabilityStatement**: Custom resources auto-advertised
   - **Documentation**: IG documentation bundled with resources

5. **Aligns with Our Architecture**:
   - **Multi-tenant**: Load IGs globally or per-tenant
   - **Multi-version**: Compatible with R4/R4B/R5/R6
   - **Dynamic routing**: No code changes needed for new IGs
   - **Separation of concerns**: IGs defined externally, loaded dynamically

### Trade-offs

**What We Gain**:
- ✅ Proper FHIR compliance
- ✅ Automatic validation and search
- ✅ Scalability to any IG
- ✅ Future-proof architecture
- ✅ Industry alignment

**What We Sacrifice**:
- ❌ Implementation complexity (4-6 days vs. 0 days)
- ❌ Network dependency (packages.fhir.org)
- ❌ Storage overhead (cached packages)
- ❌ Admin learning curve (must understand IG loading)

**Assessment**: The trade-offs are acceptable for a production-ready FHIR server. The upfront investment (4-6 days) pays long-term dividends in flexibility and compliance.

---

## Implementation Plan

### Phase 1: Foundation (12-16 hours)

**Goal**: Build core IG loading infrastructure

**Tasks**:
1. **Create FhirPackageDownloader** (4-6 hours)
   - Download packages from packages.fhir.org
   - Local caching in `/data/packages/`
   - Extract .tgz files
   - Parse package.json

2. **Create ImplementationGuideLoader** (4-6 hours)
   - Parse StructureDefinitions from package
   - Parse SearchParameters from package
   - Detect custom resources
   - Register with schema provider and search indexer

3. **Update CompositeSchemaProvider** (2-3 hours)
   - Add `AddStructureDefinition(ISourceNode)` method
   - Support dynamic schema additions
   - Thread-safe registration

4. **Create ImplementationGuideRegistry** (2-3 hours)
   - Track loaded IGs (in-memory or persistent)
   - Support global and tenant-specific scopes
   - Provide query methods

**Deliverables**:
- `src/Ignixa.Application/Features/ImplementationGuide/FhirPackageDownloader.cs`
- `src/Ignixa.Application/Features/ImplementationGuide/ImplementationGuideLoader.cs`
- `src/Ignixa.Specification/CompositeSchemaProvider.cs` (modified)
- `src/Ignixa.Application/Features/ImplementationGuide/ImplementationGuideRegistry.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenSqlOnFhirPackage_WhenDownloading_ThenExtractsViewDefinitionStructureDefinition()
{
    // Arrange
    var downloader = new FhirPackageDownloader(httpClient, cacheDirectory);

    // Act
    var package = await downloader.DownloadAsync("hl7.fhir.uv.sql-on-fhir", "2.0.0", CancellationToken.None);

    // Assert
    package.StructureDefinitions.Should().Contain(sd => sd.Type == "ViewDefinition");
}

[Fact]
public async Task GivenSqlOnFhirIG_WhenLoading_ThenRegistersViewDefinitionInSchemaProvider()
{
    // Arrange
    var loader = new ImplementationGuideLoader(downloader, versionContext, registry);

    // Act
    var result = await loader.LoadImplementationGuideAsync("hl7.fhir.uv.sql-on-fhir", "2.0.0", "global", CancellationToken.None);

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.CustomResources.Should().Contain("ViewDefinition");

    var schemaProvider = versionContext.GetSchemaProvider(FhirSpecification.R4);
    var summary = schemaProvider.Provide("ViewDefinition");
    summary.Should().NotBeNull();
}
```

### Phase 2: CRUD Operations (8-12 hours)

**Goal**: Enable IG loading via REST API and auto-update CapabilityStatement

**Tasks**:
1. **Create LoadImplementationGuideCommand & Handler** (4-6 hours)
   - Medino command: `LoadImplementationGuideCommand`
   - Handler validates inputs and calls `ImplementationGuideLoader`
   - Returns success/failure result with loaded custom resources

2. **Create /$load-ig Endpoint** (2-3 hours)
   - POST `/$load-ig` endpoint
   - Accepts packageId, version, scope
   - Admin-only endpoint (authentication required)

3. **Update CapabilityStatement Builder** (2-3 hours)
   - Query `ImplementationGuideRegistry` for custom resources
   - Add custom resources to CapabilityStatement
   - Include search parameters from loaded IGs

4. **E2E ViewDefinition CRUD Tests** (2-3 hours)
   - Test loading SQL-on-FHIR v2 IG
   - Test creating ViewDefinition resource
   - Test reading, updating, deleting ViewDefinition
   - Test CapabilityStatement includes ViewDefinition

**Deliverables**:
- `src/Ignixa.Application/Features/ImplementationGuide/LoadImplementationGuideCommand.cs`
- `src/Ignixa.Application/Features/ImplementationGuide/LoadImplementationGuideHandler.cs`
- `src/Ignixa.Api/Endpoints/AdministrationEndpoints.cs` (modified - add `/$load-ig`)
- `src/Ignixa.Api/Infrastructure/CapabilityStatementBuilder.cs` (modified)
- `test/Ignixa.Api.Tests/ImplementationGuideLoadingTests.cs`
- `test/Ignixa.Api.Tests/ViewDefinitionCrudTests.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenSqlOnFhirIG_WhenLoadingViaApi_ThenViewDefinitionCrudWorks()
{
    // Arrange
    var client = _factory.CreateClient();

    // Act 1: Load IG
    var loadResponse = await client.PostAsync("/$load-ig", new StringContent(
        """
        {
          "packageId": "hl7.fhir.uv.sql-on-fhir",
          "version": "2.0.0",
          "scope": "global"
        }
        """, Encoding.UTF8, "application/json"));
    loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Act 2: Create ViewDefinition
    var createResponse = await client.PostAsync("/ViewDefinition", new StringContent(
        """
        {
          "resourceType": "ViewDefinition",
          "name": "patient_demographics",
          "resource": "Patient",
          "select": [...]
        }
        """, Encoding.UTF8, "application/fhir+json"));
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var location = createResponse.Headers.Location;

    // Act 3: Read ViewDefinition
    var readResponse = await client.GetAsync(location);
    readResponse.StatusCode.Should().Be(HttpStatusCode.OK);

    // Act 4: Search ViewDefinition
    var searchResponse = await client.GetAsync("/ViewDefinition?name=patient_demographics");
    searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var bundle = await searchResponse.Content.ReadFromJsonAsync<Bundle>();
    bundle.Entry.Should().HaveCount(1);

    // Act 5: Check CapabilityStatement
    var metadataResponse = await client.GetAsync("/metadata");
    var cs = await metadataResponse.Content.ReadFromJsonAsync<CapabilityStatement>();
    cs.Rest[0].Resource.Should().Contain(r => r.Type == "ViewDefinition");
}
```

### Phase 3: Validation & Search (8-12 hours)

**Goal**: Enable proper validation and search for custom resources

**Tasks**:
1. **SearchParameter Registration** (4-6 hours)
   - Parse SearchParameters from loaded IGs
   - Register with SearchIndexer
   - Update search parameter definition manager
   - Test search on ViewDefinition (name, resource, status)

2. **Tier 2 Validation Integration** (2-3 hours)
   - Ensure FhirValidator uses StructureDefinitions from loaded IGs
   - Test validation of ViewDefinition resources
   - Test invalid ViewDefinition returns proper OperationOutcome

3. **SearchParameter Indexing** (2-3 hours)
   - Ensure SearchIndexer indexes custom resources
   - Test search performance
   - Test search result accuracy

**Deliverables**:
- `src/Ignixa.Search/Indexing/CustomResourceSearchIndexer.cs` (modifications)
- `src/Ignixa.Validation/FhirValidator.cs` (ensure it uses CompositeSchemaProvider)
- `test/Ignixa.Api.Tests/ViewDefinitionValidationTests.cs`
- `test/Ignixa.Api.Tests/ViewDefinitionSearchTests.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenInvalidViewDefinition_WhenCreating_ThenReturnsOperationOutcome()
{
    // Arrange
    var client = _factory.CreateClient();
    await LoadSqlOnFhirIG(client);

    // Act
    var response = await client.PostAsync("/ViewDefinition", new StringContent(
        """
        {
          "resourceType": "ViewDefinition",
          "name": "invalid",
          // Missing required "resource" field
          "select": []
        }
        """, Encoding.UTF8, "application/fhir+json"));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var outcome = await response.Content.ReadFromJsonAsync<OperationOutcome>();
    outcome.Issue.Should().Contain(i => i.Diagnostics.Contains("resource"));
}

[Fact]
public async Task GivenLoadedSqlOnFhirIG_WhenSearchingByName_ThenReturnsMatchingViewDefinitions()
{
    // Arrange
    var client = _factory.CreateClient();
    await LoadSqlOnFhirIG(client);
    await CreateViewDefinition(client, "patient_demographics");
    await CreateViewDefinition(client, "observation_vitals");

    // Act
    var response = await client.GetAsync("/ViewDefinition?name=patient_demographics");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var bundle = await response.Content.ReadFromJsonAsync<Bundle>();
    bundle.Entry.Should().HaveCount(1);
    bundle.Entry[0].Resource.Should().BeOfType<ViewDefinition>();
    ((ViewDefinition)bundle.Entry[0].Resource).Name.Should().Be("patient_demographics");
}
```

### Phase 4: Testing & Documentation (4-8 hours)

**Goal**: Comprehensive testing and user documentation

**Tasks**:
1. **Integration Tests** (2-3 hours)
   - End-to-end IG loading workflow
   - Multi-tenant IG loading
   - IG unloading (if implemented)
   - Version compatibility checks

2. **Performance Tests** (1-2 hours)
   - IG loading time benchmarks
   - CRUD performance with custom resources
   - Search performance with custom search parameters

3. **Documentation** (2-3 hours)
   - Update `docs/rest/administration.http` with `/$load-ig` examples
   - Update `docs/rest/viewdefinition.http` with CRUD examples
   - Create user guide: "Loading Implementation Guides"
   - Update CapabilityStatement documentation

**Deliverables**:
- `test/Ignixa.Api.Tests/ImplementationGuideIntegrationTests.cs`
- `bench/Ignixa.Benchmarks/ImplementationGuideLoadingBenchmarks.cs`
- `docs/rest/administration.http` (modified)
- `docs/rest/viewdefinition.http` (created)
- `docs/user-guides/loading-implementation-guides.md` (created)

**Documentation Examples**:
```http
### Load SQL-on-FHIR v2 Implementation Guide (Global)
POST {{hostname}}/$load-ig
Content-Type: application/json

{
  "packageId": "hl7.fhir.uv.sql-on-fhir",
  "version": "2.0.0",
  "scope": "global"
}

### Load US Core Implementation Guide (Tenant-Specific)
POST {{hostname}}/$load-ig
Content-Type: application/json

{
  "packageId": "hl7.fhir.us.core",
  "version": "6.1.0",
  "scope": "tenant:1"
}

### Create ViewDefinition Resource
POST {{hostname}}/ViewDefinition
Content-Type: application/fhir+json

{
  "resourceType": "ViewDefinition",
  "name": "patient_demographics",
  "resource": "Patient",
  "select": [{
    "column": [
      { "name": "id", "path": "id", "type": "string" },
      { "name": "family_name", "path": "name.where(use='official').first().family", "type": "string" },
      { "name": "given_names", "path": "name.where(use='official').first().given.join(', ')", "type": "string" },
      { "name": "birth_date", "path": "birthDate", "type": "date" },
      { "name": "gender", "path": "gender", "type": "string" }
    ]
  }]
}

### Search ViewDefinitions by Name
GET {{hostname}}/ViewDefinition?name=patient_demographics

### Search ViewDefinitions by Resource Type
GET {{hostname}}/ViewDefinition?resource=Patient

### Use ViewDefinition in Export
POST {{hostname}}/tenant/1/$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics
Prefer: respond-async
```

**Total Estimated Effort**: 32-48 hours (4-6 days)

---

## Testing Strategy

### Unit Tests

**FhirPackageDownloader**:
```csharp
- GivenValidPackageId_WhenDownloading_ThenReturnsPackage
- GivenInvalidPackageId_WhenDownloading_ThenThrowsException
- GivenCachedPackage_WhenDownloading_ThenUsesCache
- GivenPackageWithStructureDefinitions_WhenExtracting_ThenParsesStructureDefinitions
- GivenPackageWithSearchParameters_WhenExtracting_ThenParsesSearchParameters
```

**ImplementationGuideLoader**:
```csharp
- GivenSqlOnFhirIG_WhenLoading_ThenDetectsViewDefinitionAsCustomResource
- GivenIncompatibleFhirVersion_WhenLoading_ThenReturnsFailure
- GivenGlobalScope_WhenLoading_ThenRegistersForAllTenants
- GivenTenantScope_WhenLoading_ThenRegistersForSpecificTenant
- GivenAlreadyLoadedIG_WhenLoadingAgain_ThenReturnsSuccess
```

**CompositeSchemaProvider**:
```csharp
- GivenCustomStructureDefinition_WhenAdding_ThenProvidesForResourceType
- GivenCoreResource_WhenProviding_ThenUsesGeneratedProvider
- GivenCustomResource_WhenProviding_ThenUsesCustomStructureDefinition
- GivenThreadSafety_WhenConcurrentAdds_ThenHandlesCorrectly
```

**ImplementationGuideRegistry**:
```csharp
- GivenLoadedIG_WhenQuerying_ThenReturnsCustomResources
- GivenGlobalScope_WhenQueryingForTenant_ThenIncludesGlobalIGs
- GivenTenantScope_WhenQueryingForOtherTenant_ThenExcludesTenantIGs
- GivenMultipleIGs_WhenQuerying_ThenReturnsAllCustomResources
```

### Integration Tests

**IG Loading Workflow**:
```csharp
- GivenSqlOnFhirIG_WhenLoadingViaApi_ThenSucceeds
- GivenLoadedIG_WhenCheckingCapabilityStatement_ThenIncludesViewDefinition
- GivenLoadedIG_WhenCreatingViewDefinition_ThenSucceeds
- GivenLoadedIG_WhenValidatingViewDefinition_ThenUsesStructureDefinition
- GivenLoadedIG_WhenSearchingViewDefinition_ThenUsesSearchParameters
```

**Multi-Tenant**:
```csharp
- GivenGlobalIG_WhenAccessingFromTenant1_ThenWorks
- GivenGlobalIG_WhenAccessingFromTenant2_ThenWorks
- GivenTenant1IG_WhenAccessingFromTenant2_ThenDoesNotWork
- GivenMultipleTenantsWithDifferentIGs_WhenCheckingCapabilityStatement_ThenShowsTenantSpecificResources
```

**ViewDefinition CRUD**:
```csharp
- GivenLoadedSqlOnFhirIG_WhenCreatingViewDefinition_ThenReturns201
- GivenCreatedViewDefinition_WhenReading_ThenReturnsResource
- GivenCreatedViewDefinition_WhenUpdating_ThenReturns200
- GivenCreatedViewDefinition_WhenDeleting_ThenReturns204
- GivenDeletedViewDefinition_WhenReading_ThenReturns410
```

**Validation**:
```csharp
- GivenValidViewDefinition_WhenCreating_ThenSucceeds
- GivenMissingRequiredField_WhenCreating_ThenReturns400
- GivenInvalidDataType_WhenCreating_ThenReturns400
- GivenInvalidCardinality_WhenCreating_ThenReturns400
```

**Search**:
```csharp
- GivenViewDefinition_WhenSearchingByName_ThenReturnsMatches
- GivenViewDefinition_WhenSearchingByResource_ThenReturnsMatches
- GivenViewDefinition_WhenSearchingByStatus_ThenReturnsMatches
- GivenMultipleViewDefinitions_WhenSearchingWithPagination_ThenReturnsPaginatedResults
```

### Performance Tests

**IG Loading**:
```csharp
Benchmark: Loading SQL-on-FHIR v2 IG
Expected: <5 seconds (first time), <100ms (cached)

Benchmark: Loading US Core IG (larger package)
Expected: <30 seconds (first time), <500ms (cached)
```

**CRUD Operations**:
```csharp
Benchmark: Creating ViewDefinition with validation
Expected: <200ms

Benchmark: Reading ViewDefinition
Expected: <50ms

Benchmark: Searching ViewDefinitions
Expected: <100ms
```

**Concurrent Loading**:
```csharp
Benchmark: 10 concurrent IG loads
Expected: No deadlocks, all succeed
```

---

## File Modifications

### Files to Create

**Application Layer** (~2,130 lines total):

1. **FhirPackageDownloader.cs** (~300 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\FhirPackageDownloader.cs
   ```
   - Downloads FHIR packages from packages.fhir.org
   - Caches packages locally
   - Extracts .tgz files
   - Parses package.json

2. **ImplementationGuideLoader.cs** (~400 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\ImplementationGuideLoader.cs
   ```
   - Loads IG resources (StructureDefinitions, SearchParameters)
   - Detects custom resources
   - Registers with schema provider and search indexer

3. **ImplementationGuideRegistry.cs** (~200 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\ImplementationGuideRegistry.cs
   ```
   - Tracks loaded IGs (global and tenant-specific)
   - Query methods for custom resources

4. **LoadImplementationGuideCommand.cs** (~80 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\LoadImplementationGuideCommand.cs
   ```
   - Medino command for loading IGs

5. **LoadImplementationGuideHandler.cs** (~150 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\LoadImplementationGuideHandler.cs
   ```
   - Handler that validates and delegates to ImplementationGuideLoader

6. **FhirPackage.cs** (~200 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\FhirPackage.cs
   ```
   - Model representing a downloaded FHIR package
   - Methods for extracting StructureDefinitions, SearchParameters

7. **LoadedImplementationGuide.cs** (~50 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\LoadedImplementationGuide.cs
   ```
   - Record type for tracking loaded IGs

8. **PackageManifest.cs** (~100 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\PackageManifest.cs
   ```
   - Model for package.json parsing

9. **LoadImplementationGuideResult.cs** (~100 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\LoadImplementationGuideResult.cs
   ```
   - Result type for IG loading operations

10. **ImplementationGuideExceptions.cs** (~50 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\ImplementationGuideExceptions.cs
    ```
    - Custom exceptions for IG loading errors

11. **GetLoadedImplementationGuidesQuery.cs** (~50 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\GetLoadedImplementationGuidesQuery.cs
    ```
    - Query for listing loaded IGs

12. **GetLoadedImplementationGuidesHandler.cs** (~100 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\GetLoadedImplementationGuidesHandler.cs
    ```
    - Handler for IG listing

13. **UnloadImplementationGuideCommand.cs** (~50 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\UnloadImplementationGuideCommand.cs
    ```
    - Command for unloading IGs (optional)

14. **UnloadImplementationGuideHandler.cs** (~100 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\UnloadImplementationGuideHandler.cs
    ```
    - Handler for IG unloading (optional)

15. **ImplementationGuideCompatibilityChecker.cs** (~200 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Application\Features\ImplementationGuide\ImplementationGuideCompatibilityChecker.cs
    ```
    - Validates IG FHIR version compatibility

**Specification Layer** (~450 lines total):

16. **CompositeSchemaProvider.cs** (MODIFY - add ~150 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Specification\CompositeSchemaProvider.cs
    ```
    - Add `AddStructureDefinition(ISourceNode)` method
    - Support dynamic schema additions
    - Thread-safe registration

17. **StructureDefinitionParser.cs** (~300 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Specification\StructureDefinitionParser.cs
    ```
    - Parse StructureDefinition resources into IStructureDefinitionSummary

**API Layer** (~300 lines total):

18. **AdministrationEndpoints.cs** (MODIFY - add ~150 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Api\Endpoints\AdministrationEndpoints.cs
    ```
    - Add `POST /$load-ig` endpoint
    - Add `GET /$loaded-igs` endpoint
    - Add `DELETE /$loaded-igs/{packageId}` endpoint (optional)

19. **CapabilityStatementBuilder.cs** (MODIFY - add ~150 lines)
    ```
    E:\data\src\fhir-server-contrib\src\Ignixa.Api\Infrastructure\CapabilityStatementBuilder.cs
    ```
    - Query ImplementationGuideRegistry for custom resources
    - Add custom resources to CapabilityStatement

**Test Layer** (~1,800 lines total):

20. **FhirPackageDownloaderTests.cs** (~300 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Application.Tests\Features\ImplementationGuide\FhirPackageDownloaderTests.cs
    ```

21. **ImplementationGuideLoaderTests.cs** (~400 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Application.Tests\Features\ImplementationGuide\ImplementationGuideLoaderTests.cs
    ```

22. **ImplementationGuideRegistryTests.cs** (~200 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Application.Tests\Features\ImplementationGuide\ImplementationGuideRegistryTests.cs
    ```

23. **ImplementationGuideLoadingTests.cs** (~300 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Api.Tests\ImplementationGuideLoadingTests.cs
    ```

24. **ViewDefinitionCrudTests.cs** (~300 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Api.Tests\ViewDefinitionCrudTests.cs
    ```

25. **ViewDefinitionValidationTests.cs** (~150 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Api.Tests\ViewDefinitionValidationTests.cs
    ```

26. **ViewDefinitionSearchTests.cs** (~150 lines)
    ```
    E:\data\src\fhir-server-contrib\test\Ignixa.Api.Tests\ViewDefinitionSearchTests.cs
    ```

**Documentation** (~800 lines total):

27. **administration.http** (CREATE - ~200 lines)
    ```
    E:\data\src\fhir-server-contrib\docs\rest\administration.http
    ```
    - Examples of `/$load-ig` endpoint usage

28. **viewdefinition.http** (CREATE - ~200 lines)
    ```
    E:\data\src\fhir-server-contrib\docs\rest\viewdefinition.http
    ```
    - Examples of ViewDefinition CRUD operations

29. **loading-implementation-guides.md** (CREATE - ~400 lines)
    ```
    E:\data\src\fhir-server-contrib\docs\user-guides\loading-implementation-guides.md
    ```
    - User guide for IG loading

### Files to Modify

1. **Program.cs** (add ~50 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Api\Program.cs
   ```
   - Register ImplementationGuideRegistry (singleton)
   - Register LoadImplementationGuideHandler
   - Register GetLoadedImplementationGuidesHandler

2. **CompositeSchemaProvider.cs** (add ~150 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Specification\CompositeSchemaProvider.cs
   ```
   - Add support for dynamic StructureDefinition registration

3. **CapabilityStatementBuilder.cs** (add ~150 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Api\Infrastructure\CapabilityStatementBuilder.cs
   ```
   - Include custom resources from loaded IGs

4. **AdministrationEndpoints.cs** (add ~150 lines)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Api\Endpoints\AdministrationEndpoints.cs
   ```
   - Add IG management endpoints

5. **SearchIndexer.cs** (add ~100 lines - if needed)
   ```
   E:\data\src\fhir-server-contrib\src\Ignixa.Search\Indexing\SearchIndexer.cs
   ```
   - Support dynamic SearchParameter registration

**Total New Code**: ~4,680 lines
**Total Modified Code**: ~500 lines
**Total**: ~5,180 lines

---

## ADR Recommendation

**Yes**, this warrants an Architecture Decision Record (ADR).

**Recommended ADR**:
```
File: docs/adr/ADR-2531-implementation-guide-loading.md

Title: ADR-2531: Implementation Guide (IG) Loading for Custom Resource Support

Status: Proposed → Accepted (after implementation)

Context:
- Need to support custom FHIR resources like ViewDefinition from SQL-on-FHIR v2 IG
- Current dynamic routing handles CRUD but lacks validation, search, and CapabilityStatement advertisement
- Four approaches considered: Dynamic-only, StructureDefinition-based, Hardcoded, IG Loading

Decision:
Implement Implementation Guide (IG) Loading infrastructure to:
1. Download FHIR packages from packages.fhir.org
2. Parse StructureDefinitions and SearchParameters
3. Automatically detect and register custom resources
4. Enable Tier 2 validation using StructureDefinitions
5. Enable search using SearchParameters
6. Update CapabilityStatement to advertise custom resources

Consequences:
Positive:
- Industry standard approach (aligns with Smile CDR, Firely, HAPI)
- Scales to any IG (US Core, Genomics, custom IGs)
- Future-proof for FHIR R6 Additional Resources
- Automatic validation, search, and discovery
- Multi-tenant and multi-version compatible

Negative:
- Implementation complexity (4-6 days)
- Network dependency (packages.fhir.org)
- Storage overhead (cached packages)
- Admin learning curve

Implementation:
- See docs/investigations/viewdefinition-custom-resource-support.md for full plan
- 4 phases over 32-48 hours
- Phased rollout to minimize risk

Alternatives Considered:
- Dynamic-only (insufficient for production)
- StructureDefinition-based (viable but incomplete)
- Hardcoded (violates architecture)

References:
- SQL-on-FHIR v2 IG: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/
- FHIR Package Registry: https://packages.fhir.org/
- FHIR R6 Additional Resources: http://hl7.org/fhir/additional-resources.html
```

---

## References

**SQL-on-FHIR v2**:
- IG Home: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/
- ViewDefinition StructureDefinition: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/StructureDefinition-ViewDefinition.html
- Package Registry: https://packages.fhir.org/hl7.fhir.uv.sql-on-fhir

**FHIR Package Specification**:
- NPM Package Spec: https://confluence.hl7.org/display/FHIR/NPM+Package+Specification
- Package Registry API: https://packages.fhir.org/
- IG Registry: https://registry.fhir.org/

**FHIR R6 Additional Resources**:
- Additional Resources: http://hl7.org/fhir/additional-resources.html
- Resource Registration: http://hl7.org/fhir/resource-registration.html

**Reference Implementations**:
- Smile CDR: IG upload via admin UI
- Firely Server: Package management with `fhir install`
- HAPI FHIR: JPA package loader
- Microsoft FHIR Server: Profile validation loader

**Codebase Documentation**:
- Dynamic Routing: `docs/investigations/dynamic-fhir-routing.md`
- Multi-Version Support: `docs/investigations/multi-tenant-providers.md`
- SQL-on-FHIR Implementation: `docs/investigations/sql-on-fhir-v2-implementation-analysis.md`

---

## Appendix

### Appendix A: Current ViewDefinition Implementation

**Already Implemented**:

1. **Parser** (`src/Ignixa.SqlOnFhir/Parsing/ViewDefinitionExpressionParser.cs`):
   - Parses ViewDefinition JSON → ViewDefinitionExpression AST
   - Validates structure during parsing
   - Compiles FHIRPath expressions
   - Handles: constants, where, select, forEach, forEachOrNull, repeat, unionAll, nested selects

2. **Evaluator** (`src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluator.cs`):
   - Transforms FHIR resources → tabular rows
   - Implements SQL-on-FHIR v2 semantics
   - Handles array unnesting (forEach)
   - Type conversions (FHIR → SQL types)

3. **Schema Evaluator** (`src/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirSchemaEvaluator.cs`):
   - Extracts output column schema from ViewDefinitions
   - Visitor pattern implementation
   - Handles nested selects, forEach, unionAll
   - Supports Parquet schema generation

4. **Loader** (`src/Ignixa.DataLayer.BlobStorage/ViewDefinitionLoader.cs`):
   - Loads ViewDefinition resources from datastore
   - Uses standard FHIR repository: `repository.GetAsync(new ResourceKey("ViewDefinition", id))`
   - Multi-tenant isolation built-in

5. **Export Integration** (`src/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs`):
   - `$export` operation with `_viewDefinition` parameter
   - `ViewDefinitionExportStreamWriter` applies transformations
   - Outputs Parquet files with ViewDefinition-defined schemas

**Test Coverage**:
- 154 tests in `test/Ignixa.SqlOnFhir.Tests/`
- Covers parsing, evaluation, schema extraction
- Uses official SQL-on-FHIR v2 test fixtures

**What's Missing**:
- FHIR REST API endpoints (POST, GET, PUT, DELETE, Search)
- StructureDefinition-based validation
- SearchParameter support
- CapabilityStatement advertisement

### Appendix B: Custom Resource Precedents

**How Other FHIR Servers Handle Custom Resources**:

1. **Smile CDR**:
   - Admin UI for IG upload
   - Automatic StructureDefinition registration
   - SearchParameter indexing
   - CapabilityStatement updates
   - Per-tenant IG loading

2. **Firely Server (Vonk)**:
   - `fhir install <package>` CLI command
   - Package cache in `~/.fhir/packages/`
   - Automatic validation using StructureDefinitions
   - Search using SearchParameters from package

3. **HAPI FHIR**:
   - `JpaPackageCache` for package management
   - `PackageInstallerSvc` for IG loading
   - Automatic ResourceProvider creation for custom resources
   - SearchParameter registration via `ISearchParamRegistry`

4. **Microsoft FHIR Server**:
   - Profile validation loader
   - StructureDefinition-based validation
   - Limited custom resource support (primarily profiles, not new resource types)

**Common Patterns**:
- All use FHIR package registry (packages.fhir.org)
- All support StructureDefinition-based validation
- All support SearchParameter registration
- All update CapabilityStatement dynamically

**Industry Alignment**: Approach D (IG Loading) aligns with these industry-standard implementations.

---

## Conclusion

**ViewDefinition custom resource support** can be achieved through Implementation Guide (IG) Loading. This approach:

1. **Solves the immediate problem**: ViewDefinition fully supported (CRUD, validation, search)
2. **Scales to any IG**: US Core, Genomics, custom IGs
3. **Future-proof**: Ready for FHIR R6 Additional Resources
4. **Industry standard**: Aligns with major FHIR servers
5. **Reasonable effort**: 4-6 days (32-48 hours)

**Next Steps**:
1. Create ADR-2531-implementation-guide-loading.md
2. Begin Phase 1 implementation (Foundation)
3. Iterate through phases 2-4
4. Deploy and document

**Decision**: Proceed with Approach D - Implementation Guide (IG) Loading.

---

**End of Investigation**
