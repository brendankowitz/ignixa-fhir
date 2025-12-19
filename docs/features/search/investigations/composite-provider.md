# Investigation: Search Parameter Composite Provider

**Feature**: search
**Status**: Viable
**Created**: 2025-11-15
**Original ADR**: N/A

## Summary

Current: Search parameters are pre-generated for base FHIR spec only  
Proposal: Apply composite provider pattern (proven with StructureDefinitions)  
Effort: 2-3 weeks | Complexity: Medium | Risk: Low

## 1. Current Architecture

### Search Parameter Definitions (Compile-Time Generated)

**Location**: `src/Ignixa.Search/Definition/`

- **Interface**: `ISearchParameterDefinitionManager.cs`
- **Base Implementation**: `SearchParameterDefinitionManager.cs` loads pre-generated definitions
- **Generated Files**: `R4SearchParameterDefinitions.g.cs`, `R5SearchParameterDefinitions.g.cs`, etc.
- **Builder**: `SearchParameterDefinitionBuilder.cs` for runtime additions

### Data Structures

```
TypeLookup: ConcurrentDictionary<resourceType, ConcurrentDictionary<code, SearchParameterInfo>>
UrlLookup: ConcurrentDictionary<Uri, SearchParameterInfo>
SearchParameterHashMap: Dictionary<resourceType, hash>
```

### Pre-Generated Parameters

Each FHIR version (R4, R4B, R5, R6, STU3) has ~200+ search parameters pre-generated  
Initialization time: <5ms (vs 50-200ms for runtime parsing)

## 2. How StructureDefinitions Use Composite Pattern

**File**: `src/Ignixa.Specification/CompositeStructureDefinitionSummaryProvider.cs`

```
Lookup Chain:
  1. Cache (per-tenant)
  2. Packages (dbo.PackageResource)
  3. Base FHIR spec
  4. Return null/not found
```

Key Features:
- Fallback chain (packages first, then base)
- Per-tenant caching (ConcurrentDictionary)
- Graceful degradation
- Version filtering optional

## 3. Package Sources

**Database**: `dbo.PackageResource` (Already exists)
**Entity**: `src/Ignixa.DataLayer.SqlEntityFramework/Entities/PackageResourceEntity.cs`

FHIR npm packages contain:
- StructureDefinitions (profiles, extensions, logical models)
- SearchParameters (custom search parameters for the IG)

**Current Gap**: SearchParameters stored in dbo.PackageResource but NOT loaded into SearchParameterDefinitionManager

## 4. Proposed Solution

### New Classes

1. **IPackageSearchParameterProvider** (interface)
   - Convert JSON SearchParameter to SearchParameterInfo
   - Mirrors PackageResourceProvider pattern

2. **PackageSearchParameterProvider** (implementation)
   - Parse SearchParameter JSON from package resources
   - Handle errors gracefully

3. **CompositeSearchParameterDefinitionManager** (decorator)
   - Wraps base SearchParameterDefinitionManager
   - Implements ISearchParameterDefinitionManager
   - Fallback chain: cache → packages → base

### Repository Enhancement

Add to `IPackageResourceRepository`:
```csharp
Task<IReadOnlyList<PackageResource>> GetSearchParametersByCanonicalAsync(
    string canonical, string? fhirVersion = null, CancellationToken cancellationToken = default);
```

## 5. Integration Points

### Point 1: DI Registration (Program.cs)
```csharp
containerBuilder.RegisterType<PackageSearchParameterProvider>()
    .As<IPackageSearchParameterProvider>()
    .SingleInstance();
```

### Point 2: FhirVersionContext Enhancement
```csharp
// Create base manager
var baseManager = new SearchParameterDefinitionManager(schemaProvider, logger);

// Wrap with composite
return new CompositeSearchParameterDefinitionManager(
    baseManager, packageRepository, packageProvider, fhirVersion: null, logger);
```

### Point 3: Package Load/Unload
```csharp
// In LoadPackageHandler and UnloadPackageHandler
var manager = _versionContext.GetSearchParameterDefinitionManager(fhirVersion);
if (manager is CompositeSearchParameterDefinitionManager composite)
    composite.ClearCache();
```

### Point 4: Search Indexing
No changes needed - TypedElementSearchIndexer already uses GetSearchParameters()  
IG-defined parameters will be automatically included

## 6. Implementation Phases

### Phase 1: Foundation (Week 1)
- Create IPackageSearchParameterProvider interface
- Create PackageSearchParameterProvider implementation
- Add GetSearchParametersByCanonicalAsync() to repository
- Unit tests

### Phase 2: Composite Manager (Week 2)
- Create CompositeSearchParameterDefinitionManager
- Implement all interface methods
- Handle hash map calculation (include packages)
- Cache clearing logic
- Unit tests with mocks

### Phase 3: Integration (Week 3)
- Register in DI container (Program.cs)
- Modify FhirVersionContext
- Update Load/UnloadPackageHandlers
- Integration & E2E tests
- Documentation

## 7. Files to Create

```
NEW: src/Ignixa.Search/Definition/CompositeSearchParameterDefinitionManager.cs
NEW: src/Ignixa.Abstractions/IPackageSearchParameterProvider.cs
NEW: src/Ignixa.Specification/PackageSearchParameterProvider.cs
```

## 8. Files to Modify

```
src/Ignixa.Domain/Abstractions/IPackageResourceRepository.cs
src/Ignixa.DataLayer.SqlEntityFramework/Features/PackageManagement/SqlPackageResourceRepository.cs
src/Ignixa.Search/Infrastructure/FhirVersionContext.cs
src/Ignixa.Api/Program.cs
src/Ignixa.Application/Features/Admin/LoadPackageHandler.cs
src/Ignixa.Application/Features/Admin/UnloadPackageHandler.cs
```

## 9. Key Design Decisions

1. **Composite Pattern**: Proven with StructureDefinitions, less risky than alternatives
2. **No Breaking Changes**: Backward compatible interface
3. **No New Migrations**: PackageResource table already supports SearchParameters
4. **Per-Tenant Cache**: Allows different tenants to load different IGs
5. **Fallback Chain**: Packages first (allow overrides), then base spec

## 10. Success Criteria

- IG-defined search parameters accessible via GetSearchParameter()
- Search queries using IG-defined parameters work end-to-end
- Search indexing includes IG-defined parameters
- Capability statement reports IG-defined parameters
- No performance regression (<5ms for known parameters)
- >90% test coverage

## 11. References

See `src/Ignixa.Specification/CompositeStructureDefinitionSummaryProvider.cs` for the proven pattern
See `src/Ignixa.Search/Definition/SearchParameterDefinitionManager.cs` for current implementation
