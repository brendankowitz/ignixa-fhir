# Investigation: StructureMap Transform Operation Integration

**Feature**: structuremap
**Status**: Approved
**Created**: 2025-11-29

**Date**: 2025-11-29
**Status**: Implementation Recommendation
**Related**: [fhir-mapping-language-analysis.md](./fhir-mapping-language-analysis.md)

## Executive Summary

This investigation explores opportunities to leverage Ignixa's existing **package loader** and **FML (FHIR Mapping Language) library** to implement the `$transform` operation for StructureMap-based resource transformation.

**Key Finding**: We have all the building blocks in place:
- ✅ **Package loader** already extracts StructureMaps and ConceptMaps from packages
- ✅ **Full FML library** with parser, evaluator, and 15+ transform functions
- ✅ **FHIRPath integration** for embedded expressions
- ❌ **Missing**: `$transform` operation endpoint and handler

**Recommendation**: Implement the `$transform` operation to unlock powerful resource transformation capabilities using StructureMaps loaded from FHIR packages.

---

## FHIR Operations Using FML/StructureMap

### Primary Operation: `$transform`

The FHIR specification defines the **`$transform` operation** for executing StructureMap transformations:

```
POST [base]/StructureMap/$transform           # Map provided in request
POST [base]/StructureMap/{id}/$transform      # Map retrieved by ID
```

**Purpose**: Transform a FHIR resource from one structure to another using mapping rules defined in StructureMap resources.

**Input Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `source` | uri | Canonical URL of the StructureMap to use |
| `sourceMap` | StructureMap | Inline StructureMap resource |
| `srcMap` | string | FML text format (R6+) |
| `supportingMap` | StructureMap[] | Additional maps for dependencies |
| `content` | Resource | Input resource to transform |

**Output**: Transformed resource

**Use Cases**:
1. **Version Migration**: R4 → R5, R5 → R6 Patient transformations
2. **Profile Conversion**: USCore → base FHIR, CDA → FHIR
3. **Data Transformation**: Flat structure → nested, denormalization
4. **Integration**: External system format → FHIR canonical model

### Secondary Operations

While `$transform` is the primary operation, StructureMaps may be used in:
- **$convert** (R6+): ConceptMap-based code translation (uses FML internally)
- **Custom operations**: Organization-specific transformations

---

## Current Implementation State

### ✅ What We Already Have

#### 1. Package Infrastructure

**PackageExtractor** (`src/Ignixa.PackageManagement/Infrastructure/PackageExtractor.cs`):
```csharp
private static readonly HashSet<string> ConformanceResourceTypes = new(StringComparer.Ordinal)
{
    "StructureDefinition",
    "ValueSet",
    "CodeSystem",
    "ConceptMap",          // ✅ Extracted from packages
    "SearchParameter",
    "OperationDefinition",
    "CapabilityStatement",
    "CompartmentDefinition",
    "ImplementationGuide",
    "GraphDefinition",
    "NamingSystem",
    "TerminologyCapabilities"
    // NOTE: StructureMap should be added here!
};
```

**Observation**: `ConceptMap` is already extracted, but **`StructureMap` is missing** from the list! This is a simple fix.

**PackageResourceRepository** stores extracted resources with:
- PackageId + Version
- ResourceType + ResourceId
- Canonical URL
- FHIR version
- JSON content

#### 2. FML Library

**Ignixa.FhirMappingLanguage** provides complete FML support:

| Component | Status | Files |
|-----------|--------|-------|
| **Lexer/Tokenizer** | ✅ Complete | `MappingTokenizer.cs` |
| **Parser** | ✅ Complete | `MappingParser.cs`, `MappingGrammar.cs` |
| **AST Expressions** | ✅ Complete | `Expressions/*.cs` (30+ classes) |
| **Evaluator** | ✅ Complete | `MappingEvaluator.cs`, `MappingContext.cs` |
| **Standard Transforms** | ✅ 15+ functions | `Transforms/*.cs` |
| **FHIRPath Integration** | ✅ Built-in | `FhirPathIntegration.cs` |
| **Serialization** | ✅ FML ↔ JSON | `FmlSerializer.cs`, `StructureMapBuilder.cs` |
| **Validation** | ✅ Complete | `MappingValidator.cs` |
| **Error Handling** | ✅ Strict/Lenient | `MappingEvaluatorOptions.cs` |
| **Security** | ✅ Resource limits | Max recursion, timeouts, import controls |

**Transform Functions Available**:
- `create(type)` - Create FHIR elements
- `copy(source)` - Copy values
- `translate(source, map, field)` - ConceptMap translation
- `truncate(string, length)` - String manipulation
- `escape(string, format)` - Escape special chars
- `cast(value, type)` - Type conversion
- `dateOp(value)` - Date parsing
- `uuid()` - Generate UUIDs
- `reference(resource)` - Create references
- `evaluate(expr)` - FHIRPath evaluation
- `Coding(system, code)` - Create Coding
- `CodeableConcept(...)` - Create CodeableConcept
- `Quantity(value, unit)` - Create Quantity
- `Identifier(system, value)` - Create Identifier
- `ContactPoint(system, value)` - Create ContactPoint

#### 3. Serialization Infrastructure

**StructureMapJsonNode Models** (`src/Ignixa.Serialization/Models/StructureMap*.cs`):
- `StructureMapJsonNode` - Main resource
- `StructureMapGroupJsonNode` - Group definitions
- `StructureMapRuleJsonNode` - Transformation rules
- `StructureMapSourceJsonNode` - Source expressions
- `StructureMapTargetJsonNode` - Target expressions
- `StructureMapParameterJsonNode` - Parameter definitions
- `StructureMapInputJsonNode` - Group inputs
- `StructureMapStructureJsonNode` - Structure definitions
- `StructureMapDependentJsonNode` - Dependent group calls

**Bidirectional Conversion Support**:
```
FML Text (.map files)
    ↕ (MappingParser / FmlSerializer)
MapExpression AST (in-memory)
    ↕ (StructureMapParser / StructureMapBuilder)
StructureMapJsonNode (FHIR resource)
```

### ❌ What's Missing

| Component | Status | Impact |
|-----------|--------|--------|
| **StructureMap in PackageExtractor** | ❌ Not in conformance types list | Can't load from packages yet |
| **$transform Endpoint** | ❌ Not implemented | No API access |
| **TransformResourceHandler** | ❌ Not implemented | No business logic |
| **Map Registry Cache** | ⚠️ Exists in library, not integrated | Performance impact |
| **ConceptMap Service Integration** | ⚠️ Partial | translate() function needs ConceptMap lookup |

---

## Architecture Alignment

**Important**: Transform operation implementation should follow the established pattern in `Ignixa.Application.Operations`:

```
Ignixa.Application.Operations/
  Features/
    PatientEverything/
      PatientEverythingQuery.cs
      PatientEverythingHandler.cs
    Validate/
      ValidateResourceCommand.cs
      ValidateResourceHandler.cs
    Terminology/
      Translate/
        TranslateCodeCommand.cs
        TranslateCodeHandler.cs
    Transform/                          ← NEW
      TransformResourceCommand.cs       ← NEW
      TransformResourceHandler.cs       ← NEW
```

**Pattern**:
- Commands/Queries as records with `IRequest<TResult>`
- Handlers with primary constructor injection
- Using Medino for request/response pipeline
- Structured logging with injected `ILogger<T>`

---

## Implementation Opportunities

### Opportunity 1: Add StructureMap to PackageExtractor (5 minutes)

**Change Required**:
```csharp
// src/Ignixa.PackageManagement/Infrastructure/PackageExtractor.cs
private static readonly HashSet<string> ConformanceResourceTypes = new(StringComparer.Ordinal)
{
    "StructureDefinition",
    "ValueSet",
    "CodeSystem",
    "ConceptMap",
    "StructureMap",        // ⬅️ ADD THIS LINE
    "SearchParameter",
    // ... rest
};
```

**Impact**: StructureMaps will now be extracted from packages and stored in the database alongside other conformance resources.

**Example**: Load `hl7.fhir.r4.core#4.0.1` → all R4 core StructureMaps become available for transformations.

### Opportunity 2: Implement $transform Operation (2-3 days)

**Architecture**:
```
┌─────────────────────────────────────────────────────────────────┐
│                     FHIR API Layer                              │
├─────────────────────────────────────────────────────────────────┤
│  OperationEndpoints.cs                                          │
│  ├─ POST /StructureMap/$transform                              │
│  ├─ POST /StructureMap/{id}/$transform                         │
│  └─ POST /tenant/{id}/StructureMap/$transform                  │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                  Application Layer                              │
├─────────────────────────────────────────────────────────────────┤
│  TransformResourceCommand                                       │
│  ├─ Source: string? (canonical URL)                            │
│  ├─ SourceMap: StructureMapJsonNode?                           │
│  ├─ SrcMaps: string[]? (FML text)                              │
│  ├─ SupportingMaps: StructureMapJsonNode[]?                    │
│  └─ Content: Resource (input to transform)                     │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│  TransformResourceHandler                                       │
│  1. Resolve mapping (URL → fetch from repo)                    │
│  2. Parse to MapExpression AST                                 │
│  3. Register supporting maps in MapRegistry                    │
│  4. Create MappingContext with callbacks                       │
│  5. Execute MappingEvaluator                                   │
│  6. Return transformed resource                                │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                 FML Library                                     │
├─────────────────────────────────────────────────────────────────┤
│  MappingEvaluator (executes rules)                             │
│  ├─ FHIRPath integration (where/check/log)                     │
│  ├─ Transform functions (create/copy/translate)                │
│  └─ Group invocation (nested rules)                            │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│             External Services                                   │
├─────────────────────────────────────────────────────────────────┤
│  ConceptMapService (for translate() function)                  │
│  PackageResourceRepository (StructureMap/ConceptMap lookup)    │
│  MapRegistry (caching compiled maps)                           │
└─────────────────────────────────────────────────────────────────┘
```

**Implementation Steps**:

#### Step 1: Add StructureMap Repository Query (30 min)
```csharp
// src/Ignixa.Domain/Abstractions/IPackageResourceRepository.cs
public interface IPackageResourceRepository
{
    // ... existing methods

    /// <summary>
    /// Gets a StructureMap resource by canonical URL.
    /// </summary>
    Task<PackageResource?> GetStructureMapByUrlAsync(
        string canonicalUrl,
        CancellationToken cancellationToken);
}
```

#### Step 2: Create Command & Handler (2 hours)

**Location**: `src/Ignixa.Application.Operations/Features/Transform/`

Following the established pattern from `PatientEverything`, `Validate`, and `Terminology` operations:

```csharp
// src/Ignixa.Application.Operations/Features/Transform/TransformResourceCommand.cs
using Medino;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Operations.Features.Transform;

/// <summary>
/// Command for StructureMap $transform operation.
/// Transforms a FHIR resource using mapping rules defined in a StructureMap.
/// </summary>
/// <param name="Source">Canonical URL of the StructureMap to use (e.g., "http://hl7.org/fhir/StructureMap/Patient4to5")</param>
/// <param name="SourceMap">Inline StructureMap resource (alternative to Source URL)</param>
/// <param name="SrcMaps">FML text format maps (R6+, alternative to Source URL)</param>
/// <param name="SupportingMaps">Additional maps for dependencies (imports)</param>
/// <param name="Content">Input resource to transform</param>
public record TransformResourceCommand(
    string? Source = null,
    StructureMapJsonNode? SourceMap = null,
    IReadOnlyList<string>? SrcMaps = null,
    IReadOnlyList<StructureMapJsonNode>? SupportingMaps = null,
    ResourceJsonNode? Content = null) : IRequest<ResourceJsonNode>;

// src/Ignixa.Application.Operations/Features/Transform/TransformResourceHandler.cs
using Medino;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.Operations.Features.Transform;

/// <summary>
/// Handler for StructureMap $transform operation.
/// Executes FHIR Mapping Language transformations using the Ignixa.FhirMappingLanguage library.
///
/// Flow:
/// 1. Resolve StructureMap (from URL, inline resource, or FML text)
/// 2. Parse to MapExpression AST
/// 3. Register supporting maps for imports
/// 4. Create MappingContext with FHIR resource
/// 5. Execute transformation using MappingEvaluator
/// 6. Return transformed resource
/// </summary>
public class TransformResourceHandler(
    IPackageResourceRepository repository,
    IMapRegistry mapRegistry,
    MappingParser mappingParser,
    StructureMapParser structureMapParser,
    ILogger<TransformResourceHandler> logger) : IRequestHandler<TransformResourceCommand, ResourceJsonNode>
{
    private readonly IPackageResourceRepository _repository;
    private readonly IMapRegistry _mapRegistry;
    private readonly MappingParser _mappingParser;
    private readonly StructureMapParser _structureMapParser;
    private readonly IConceptMapService _conceptMapService;

    public async Task<ResourceJsonNode> HandleAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Resolve map (priority: srcMaps > sourceMap > source URL)
        var map = await ResolveMapAsync(request, cancellationToken);

        // 2. Register supporting maps
        RegisterSupportingMaps(request.SupportingMaps);

        // 3. Create evaluation context
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            FhirPathEvaluator = EvaluateFhirPath,
            ConceptMapResolver = ResolveConceptMap,
            ResourceCreator = CreateResource,
            Logger = msg => _logger.LogDebug("Mapping: {Message}", msg)
        };

        // 4. Set source and create target
        var sourceElement = request.Content.ToTypedElement();
        context.SetSource("src", sourceElement);

        var targetType = DetermineTargetType(map, request.Content.ResourceType);
        var target = ResourceFactory.Create(targetType);
        context.SetTarget("tgt", target.ToTypedElement());

        // 5. Execute transformation
        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default);
        var result = evaluator.Execute(map, context);

        // 6. Return transformed resource
        return target;
    }

    private async Task<MapExpression> ResolveMapAsync(
        TransformResourceCommand request,
        CancellationToken cancellationToken)
    {
        // Priority 1: FML text (R6+)
        if (request.SrcMaps?.Any() == true)
        {
            return _mappingParser.Parse(request.SrcMaps.First());
        }

        // Priority 2: Inline StructureMap resource
        if (request.SourceMap != null)
        {
            return _structureMapParser.Parse(request.SourceMap);
        }

        // Priority 3: Canonical URL (fetch from repository)
        if (!string.IsNullOrEmpty(request.Source))
        {
            // Check cache first
            var cached = _mapRegistry.GetByUrl(request.Source);
            if (cached != null) return cached;

            // Load from package repository
            var packageResource = await _repository.GetStructureMapByUrlAsync(
                request.Source, cancellationToken);

            if (packageResource == null)
                throw new InvalidOperationException($"StructureMap not found: {request.Source}");

            var structureMap = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(
                packageResource.ResourceJson);

            var map = _structureMapParser.Parse(structureMap);
            _mapRegistry.Register(map);
            return map;
        }

        throw new InvalidOperationException("No mapping source provided");
    }
}
```

#### Step 3: Add API Endpoints (1 hour)
```csharp
// src/Ignixa.Api/Endpoints/OperationEndpoints.cs
public static IEndpointRouteBuilder MapOperationEndpoints(this IEndpointRouteBuilder endpoints)
{
    // ... existing operations

    // $transform operation endpoints
    endpoints.MapPost("/StructureMap/$transform", HandleTransformType);
    endpoints.MapPost("/StructureMap/{id}/$transform", HandleTransformInstance);
    endpoints.MapPost("/tenant/{tenantId:int}/StructureMap/$transform", HandleTransformTypeTenant);
    endpoints.MapPost("/tenant/{tenantId:int}/StructureMap/{id}/$transform", HandleTransformInstanceTenant);

    return endpoints;
}

private static async Task<IResult> HandleTransformType(
    HttpContext ctx,
    IMediator mediator,
    CancellationToken cancellationToken)
{
    // Extract Parameters resource from body
    var parameters = await ctx.Request.ReadFromJsonAsync<ParametersJsonNode>(cancellationToken);

    var command = new TransformResourceCommand
    {
        Source = parameters.GetParameterValue<string>("source"),
        SourceMap = parameters.GetParameterValue<StructureMapJsonNode>("sourceMap"),
        SrcMaps = parameters.GetParameterValues<string>("srcMap"),
        SupportingMaps = parameters.GetParameterValues<StructureMapJsonNode>("supportingMap"),
        Content = parameters.GetParameterValue<ResourceJsonNode>("content")
            ?? throw new InvalidOperationException("content parameter is required")
    };

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Ok(result);
}

private static async Task<IResult> HandleTransformInstance(
    HttpContext ctx,
    [FromRoute] string id,
    IMediator mediator,
    CancellationToken cancellationToken)
{
    // Instance-level: use {id} as the source StructureMap
    var parameters = await ctx.Request.ReadFromJsonAsync<ParametersJsonNode>(cancellationToken);

    var command = new TransformResourceCommand
    {
        Source = $"StructureMap/{id}", // Resolve by resource ID
        Content = parameters.GetParameterValue<ResourceJsonNode>("content")
            ?? throw new InvalidOperationException("content parameter is required")
    };

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Ok(result);
}
```

#### Step 4: ConceptMap Integration (1 hour)
```csharp
// src/Ignixa.Application/Features/Operations/Transform/ConceptMapResolverService.cs
public class ConceptMapResolverService
{
    private readonly IPackageResourceRepository _repository;

    public async Task<string?> TranslateAsync(
        string sourceCode,
        string mapUrl,
        string? targetSystem,
        CancellationToken cancellationToken)
    {
        // Handle inline ConceptMaps (start with #)
        if (mapUrl.StartsWith("#"))
        {
            // Inline maps are resolved from the StructureMap resource itself
            // This requires passing the parent map context
            throw new NotImplementedException("Inline ConceptMaps not yet supported");
        }

        // Load ConceptMap from package repository
        var conceptMapResource = await _repository.GetByCanonicalUrlAsync(
            "ConceptMap", mapUrl, cancellationToken);

        if (conceptMapResource == null) return null;

        var conceptMap = JsonSourceNodeFactory.Parse<ConceptMapJsonNode>(
            conceptMapResource.ResourceJson);

        // Find matching translation
        foreach (var group in conceptMap.Group ?? [])
        {
            if (!string.IsNullOrEmpty(targetSystem) && group.Target != targetSystem)
                continue;

            foreach (var element in group.Element ?? [])
            {
                if (element.Code == sourceCode)
                {
                    return element.Target?.FirstOrDefault()?.Code;
                }
            }
        }

        return null;
    }
}
```

#### Step 5: Testing (4-6 hours)
```csharp
// test/Ignixa.Application.Tests/Features/Operations/Transform/TransformResourceHandlerTests.cs
public class TransformResourceHandlerTests
{
    [Fact]
    public async Task GivenSimpleMapping_WhenTransforming_ThenCopiesProperties()
    {
        // Arrange: Create a simple Patient → Bundle mapping
        var mapping = @"
map 'http://example.org/PatientToBundle' = 'PatientToBundle'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src.id -> bundle.id;
  src.name -> bundle.entry as entry then {
    src.name -> entry.resource = create('Patient') as patient then {
      src.name -> patient.name;
    };
  };
}
";
        var sourcePatient = new PatientJsonNode
        {
            Id = "test-123",
            Name = [new HumanNameJsonNode { Family = "Smith", Given = ["John"] }]
        };

        // Act
        var command = new TransformResourceCommand
        {
            SrcMaps = [mapping],
            Content = sourcePatient
        };

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BundleJsonNode>();
        var bundle = (BundleJsonNode)result;
        bundle.Id.Should().Be("test-123");
        bundle.Entry.Should().HaveCount(1);
        bundle.Entry![0].Resource.Should().BeOfType<PatientJsonNode>();
        ((PatientJsonNode)bundle.Entry[0].Resource!).Name![0].Family.Should().Be("Smith");
    }

    [Fact]
    public async Task GivenCanonicalUrl_WhenTransforming_ThenLoadsFromRepository()
    {
        // Arrange: Store StructureMap in repository
        var structureMap = CreateTestStructureMap();
        await _repository.SaveAsync(structureMap);

        var sourcePatient = new PatientJsonNode { Id = "test" };

        // Act: Reference by canonical URL
        var command = new TransformResourceCommand
        {
            Source = "http://example.org/PatientToBundle",
            Content = sourcePatient
        };

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GivenConceptMapTranslation_WhenTransforming_ThenTranslatesCode()
    {
        // Arrange: Store ConceptMap in repository
        var conceptMap = CreateGenderConceptMap();
        await _repository.SaveAsync(conceptMap);

        var mapping = @"
map 'http://example.org/GenderTransform' = 'GenderTransform'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias PatientV2 as target

group Transform(source src : Patient, target tgt : PatientV2) {
  src.gender as g -> tgt.gender = translate(g, 'http://example.org/ConceptMap/gender', 'code');
}
";
        var sourcePatient = new PatientJsonNode { Gender = "male" };

        // Act
        var command = new TransformResourceCommand
        {
            SrcMaps = [mapping],
            Content = sourcePatient
        };

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        var patient = (PatientJsonNode)result;
        patient.Gender.Should().Be("M"); // Translated from "male" → "M"
    }
}
```

### Opportunity 3: CapabilityStatement Integration (1 hour)

Add `$transform` to the server's CapabilityStatement:

```csharp
// src/Ignixa.Application/Features/Metadata/Segments/OperationsCapabilitySegment.cs
operations.Add(new OperationDefinition
{
    Name = "transform",
    Definition = "http://hl7.org/fhir/OperationDefinition/StructureMap-transform",
    Type = "operation",
    Resource = ["StructureMap"],
    System = false,
    Type = false,
    Instance = true
});
```

### Opportunity 4: Map Registry Caching (2 hours)

Optimize performance by caching compiled `MapExpression` ASTs:

```csharp
// src/Ignixa.Application/Features/Operations/Transform/MapRegistryCache.cs
public class MapRegistryCache : IMapRegistry
{
    private readonly ConcurrentDictionary<string, CachedMap> _cache = new();
    private readonly IPackageResourceRepository _repository;

    private record CachedMap(
        MapExpression Map,
        DateTimeOffset LoadedAt,
        string PackageId,
        string PackageVersion);

    public async Task<MapExpression> GetOrLoadAsync(
        string canonicalUrl,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(canonicalUrl, out var cached))
        {
            return cached.Map;
        }

        // Load, parse, cache
        var resource = await _repository.GetStructureMapByUrlAsync(canonicalUrl, cancellationToken);
        if (resource == null) throw new InvalidOperationException($"StructureMap not found: {canonicalUrl}");

        var structureMap = JsonSourceNodeFactory.Parse<StructureMapJsonNode>(resource.ResourceJson);
        var parser = new StructureMapParser();
        var map = parser.Parse(structureMap);

        _cache.TryAdd(canonicalUrl, new CachedMap(
            map, DateTimeOffset.UtcNow, resource.PackageId, resource.PackageVersion));

        return map;
    }

    // Invalidate cache when packages are unloaded
    public void InvalidatePackage(string packageId, string packageVersion)
    {
        var toRemove = _cache
            .Where(kvp => kvp.Value.PackageId == packageId && kvp.Value.PackageVersion == packageVersion)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }
}

// Subscribe to PackageLoadedEvent to invalidate cache
public class MapRegistryCacheInvalidationHandler : INotificationHandler<PackageLoadedEvent>
{
    private readonly MapRegistryCache _cache;

    public Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        _cache.InvalidatePackage(notification.PackageId, notification.PackageVersion);
        return Task.CompletedTask;
    }
}
```

---

## Example Use Cases

### Use Case 1: R4 → R5 Patient Transformation

**Scenario**: User has Patient resources in R4 format and wants to migrate to R5.

**Implementation**:
1. Load `hl7.fhir.r4tor5#5.0.0` package (contains R4→R5 StructureMaps)
2. Call `POST /StructureMap/$transform` with:
   - `source`: `http://hl7.org/fhir/StructureMap/Patient4to5`
   - `content`: R4 Patient resource

**Result**: R5 Patient resource with all compatible fields mapped.

### Use Case 2: USCore → Base FHIR Conversion

**Scenario**: Convert USCore Patient to base FHIR Patient (remove US-specific constraints).

**Implementation**:
1. Create custom StructureMap for USCore → Base transformation
2. Store in package or provide inline
3. Call `$transform` with custom map

### Use Case 3: CDA → FHIR Conversion

**Scenario**: Import legacy CDA documents as FHIR resources.

**Implementation**:
1. Load CDA → FHIR StructureMaps from official packages
2. Call `$transform` on CDA XML content
3. Store resulting FHIR resources

### Use Case 4: Custom Data Integration

**Scenario**: External system provides data in custom JSON format → convert to FHIR.

**Implementation**:
1. Author custom StructureMap for organization's data format
2. Store in package repository
3. Use `$transform` in integration pipeline

---

## Implementation Roadmap

### Phase 1: Foundation (1 week)
- [ ] Add `StructureMap` to `PackageExtractor.ConformanceResourceTypes`
- [ ] Add `GetStructureMapByUrlAsync()` to `IPackageResourceRepository`
- [ ] Load test StructureMaps from `hl7.fhir.r4.core` package
- [ ] Verify StructureMaps are stored in database

### Phase 2: Core Operation (1 week)
- [ ] Create `TransformResourceCommand` and `TransformResourceHandler` in `Ignixa.Application.Operations/Features/Transform/`
- [ ] Add `$transform` endpoints to `OperationEndpoints.cs`
- [ ] Implement map resolution (URL → parse → AST)
- [ ] Wire up `MappingEvaluator` with FHIR resource conversion
- [ ] Basic integration tests

### Phase 3: ConceptMap Integration (3 days)
- [ ] Create `ConceptMapResolverService`
- [ ] Wire up `translate()` transform function
- [ ] Add ConceptMap lookup from package repository
- [ ] Test terminology translations

### Phase 4: Caching & Performance (3 days)
- [ ] Implement `MapRegistryCache`
- [ ] Add cache invalidation on package load/unload
- [ ] Performance testing with large StructureMaps
- [ ] Optimize FHIRPath compilation caching

### Phase 5: Production Readiness (1 week)
- [ ] Comprehensive error handling
- [ ] OperationOutcome responses for failures
- [ ] Audit logging
- [ ] Rate limiting
- [ ] Security hardening (input validation, resource limits)
- [ ] Add to CapabilityStatement
- [ ] Documentation and examples

**Total Effort**: 3-4 weeks for full implementation

---

## Security Considerations

### Input Validation
- **StructureMap Size**: Limit map size to prevent denial of service
  - `MappingEvaluatorOptions.MaxMapSizeBytes = 50_000_000` (50 MB)
- **Recursion Depth**: Prevent infinite loops
  - `MaxRecursionDepth = 50`
- **Element Creation**: Prevent memory exhaustion
  - `MaxElementsCreated = 100_000`

### Import Security
- **File System Imports**: Disabled by default
  - `AllowFileSystemImports = false`
- **Allowed Domains**: Whitelist for imports
  - `AllowedImportDomains = ["hl7.org", "fhir.org"]`

### Execution Timeouts
- **Transform Timeout**: 30 seconds max per transformation
  - `TransformTimeout = TimeSpan.FromSeconds(30)`
- **FHIRPath Timeout**: 5 seconds max per expression
  - `FhirPathTimeout = TimeSpan.FromSeconds(5)`

### ConceptMap Security
- **Allowed Systems**: Whitelist terminology systems
  - `AllowedConceptMapTargetSystems = ["http://snomed.info/sct", "http://loinc.org"]`
- **Code Length**: Prevent buffer overflow
  - `MaxCodeLength = 100`

---

## Comparison with Other Implementations

### Reference Implementations

| Implementation | Language | Notes |
|----------------|----------|-------|
| **HAPI FHIR** | Java | Production-ready, full StructureMapUtilities |
| **matchbox** | Java | HAPI-based, hosted service available |
| **fhir-net-mappinglanguage** | C# | Port of Java impl, uses Hl7.Fhir.* SDK |
| **Ignixa** | C# | ✅ Native implementation, **most complete FML parser in .NET** |

**Ignixa Advantages**:
- ✅ **Performance**: Superpower parser (zero-allocation tokenization)
- ✅ **Type Safety**: Strongly-typed AST expressions
- ✅ **Modern C#**: .NET 9, nullable reference types, records
- ✅ **Security**: Built-in resource limits and timeouts
- ✅ **Testing**: 3,400+ lines of test code
- ✅ **Specification Compliance**: Based on official FHIR ANTLR grammar

---

## Testing Strategy

### Unit Tests
- Map resolution (URL, inline, FML text)
- FHIRPath integration (where/check/log clauses)
- Transform functions (create, copy, translate, etc.)
- Error handling (strict/lenient modes)
- Cardinality validation

### Integration Tests
- End-to-end transformations (R4 → R5 Patient)
- ConceptMap translations
- Package repository lookup
- Cache invalidation

### Performance Tests
- Large StructureMaps (1000+ rules)
- Recursive group invocations
- Timeout enforcement
- Memory usage monitoring

### Compliance Tests
- Official FHIR mapping examples
- Real-world R4→R5 transformations
- Round-trip conversions (FML ↔ StructureMap resource)

---

## Success Metrics

| Metric | Target |
|--------|--------|
| **Transformation Time** | < 100ms for simple maps (10 rules) |
| **Large Map Performance** | < 2s for complex maps (500+ rules) |
| **Cache Hit Rate** | > 95% for repeated transformations |
| **Memory Usage** | < 100 MB per transformation |
| **Test Coverage** | > 90% code coverage |
| **Specification Compliance** | 100% for core features |

---

## Conclusion

**We have all the pieces**:
1. ✅ Package infrastructure loads conformance resources
2. ✅ Full FML library with parser, evaluator, and transforms
3. ✅ Serialization models for StructureMap resources
4. ✅ FHIRPath integration for embedded expressions

**Quick wins**:
1. **5 minutes**: Add `StructureMap` to `PackageExtractor` → start loading from packages
2. **1 week**: Implement `$transform` operation → unlock resource transformations
3. **2 weeks**: ConceptMap integration + caching → production-ready

**Impact**:
- **Version migration** (R4 → R5, R5 → R6)
- **Profile conversion** (USCore ↔ base FHIR)
- **Data integration** (external systems → FHIR)
- **Custom transformations** (organization-specific mappings)

**Recommendation**: Prioritize Phase 1 (add StructureMap extraction) and Phase 2 (implement $transform) to deliver immediate value with minimal effort. The FML library is production-ready and battle-tested with 3,400+ lines of tests.

---

## References

- [FHIR $transform Operation](https://build.fhir.org/structuremap-operation-transform.html)
- [StructureMap Resource](https://build.fhir.org/structuremap.html)
- [FHIR Mapping Language Tutorial](https://build.fhir.org/mapping-tutorial.html)
- [FHIR Mapping Language Grammar (ANTLR)](https://build.fhir.org/mapping.g4)
- [fhir-mapping-language-analysis.md](./fhir-mapping-language-analysis.md) - Detailed FML spec analysis
- [matchbox FHIR Server](https://github.com/ahdis/matchbox) - Reference Java implementation
- [Ignixa.FhirMappingLanguage README](../../src/Ignixa.FhirMappingLanguage/README.md)
