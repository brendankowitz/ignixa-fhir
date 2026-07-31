---
sidebar_position: 6
title: Search
description: FHIR search parameter definitions and management
---

# Ignixa.Search

Search parameter definitions, indexing, and compartment management for FHIR resources.

## Installation

```bash
dotnet add package Ignixa.Search
```

## Quick Start

```csharp
using Ignixa.Search.Definition;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;

// Create search parameter definition manager
var schemaProvider = FhirVersion.R4.GetSchemaProvider();
var manager = new SearchParameterDefinitionManager(schemaProvider, logger);

// Get parameters for a resource type
var patientParams = manager.GetSearchParameters("Patient");

foreach (var param in patientParams)
{
    Console.WriteLine($"{param.Code}: {param.Type}");
}
```

## Search Parameter Management

### ISearchParameterDefinitionManager

```csharp
public interface ISearchParameterDefinitionManager
{
    // All search parameters across all resource types
    IEnumerable<SearchParameterInfo> AllSearchParameters { get; }

    // Get search parameters for a resource type
    IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType);

    // Try to get search parameters (returns false if resource type unknown)
    bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters);

    // Get specific parameter by resource type and code
    SearchParameterInfo GetSearchParameter(string resourceType, string code);

    // Try to get specific parameter
    bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter);

    // Get parameter by definition URL
    SearchParameterInfo GetSearchParameter(Uri definitionUri);

    // Add custom search parameters at runtime
    void AddNewSearchParameters(IReadOnlyCollection<IElement> searchParameters, bool calculateHash = true);

    // Remove custom search parameter
    void DeleteSearchParameter(string url, bool calculateHash = true);
}
```

### Filtered Managers

```csharp
// Only returns parameters marked as supported
var supportedManager = new SupportedSearchParameterDefinitionManager(manager);
var supported = supportedManager.GetSearchParameters("Patient");

// Only returns parameters marked as searchable
var searchableManager = new SearchableSearchParameterDefinitionManager(manager);
var searchable = searchableManager.GetSearchParameters("Patient");

// Optionally admit parameters that are registered but not yet reindexed ("partially indexed").
// Each accessor invokes the delegate exactly once and applies that answer to every result it
// returns; omitting the delegate defaults to refusing them.
var partial = new SearchableSearchParameterDefinitionManager(manager, () => true);
```

:::warning Partially indexed parameters

Opting in makes results **wrong in both directions**, not merely incomplete. A resource that has not been
reindexed yet has no index rows for the parameter, so a positive filter omits it, while a negation
(`:not`, `:missing=true`) lowers to `Except(every resource of the type, the inner match)` and hands that
same resource back as a match. Nothing in the response bundle distinguishes such a result from a complete
one, so only enable this when the caller has explicitly asked for partial results.

:::

## SearchParameterInfo

```csharp
public class SearchParameterInfo
{
    // Parameter name (e.g., "family")
    public string Name { get; }

    // Parameter code used in queries (e.g., "family")
    public string Code { get; }

    // Parameter type (string, token, reference, date, etc.)
    public SearchParamType Type { get; }

    // Canonical URL
    public Uri Url { get; }

    // FHIRPath expression for extraction
    public string Expression { get; }

    // Description
    public string Description { get; }

    // Base resource types this parameter applies to
    public IReadOnlyList<string> BaseResourceTypes { get; }

    // Target resource types (for reference parameters)
    public IReadOnlyList<string> TargetResourceTypes { get; }

    // Components (for composite parameters)
    public IReadOnlyList<SearchParameterComponentInfo> Component { get; }

    // Whether this parameter is searchable (mutated in place as reindexing progresses)
    public bool IsSearchable { get; set; }

    // Whether this parameter is supported (mutated in place as reindexing progresses)
    public bool IsSupported { get; set; }
}
```

## Search Parameter Types

| Type | Description | Example |
|------|-------------|---------|
| `string` | Text search | `name`, `address` |
| `token` | Coded values | `identifier`, `code` |
| `reference` | Resource references | `subject`, `patient` |
| `date` | Date/DateTime | `birthdate`, `date` |
| `number` | Numeric values | `length` |
| `quantity` | Value with unit | `value-quantity` |
| `uri` | URI values | `url` |
| `composite` | Multiple values | `component-code-value-quantity` |

## Compartment Support

### CompartmentDefinitionManager

```csharp
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;

var compartmentManager = new CompartmentDefinitionManager(FhirVersion.R4);

// Get search params for a resource in a compartment
if (compartmentManager.TryGetSearchParams("Observation", CompartmentType.Patient, out var searchParams))
{
    Console.WriteLine($"Patient compartment search params for Observation:");
    foreach (var param in searchParams)
    {
        Console.WriteLine($"  - {param}");
    }
}

// Get all resource types in a compartment
if (compartmentManager.TryGetResourceTypes(CompartmentType.Patient, out var resourceTypes))
{
    Console.WriteLine($"Resources in Patient compartment: {string.Join(", ", resourceTypes)}");
}
```

### CompartmentType

Available compartment types from the FHIR specification:

- `CompartmentType.Patient`
- `CompartmentType.Encounter`
- `CompartmentType.RelatedPerson`
- `CompartmentType.Practitioner`
- `CompartmentType.Device`

## Parameter Conflict Resolution

When multiple IGs define SearchParameters with the same code, use conflict resolution:

```csharp
using Ignixa.Search.Definition;

var options = new SearchParameterResolutionOptions
{
    // Higher priority packages win (first = highest priority)
    PackagePriorityOrder = ["hl7.fhir.us.core", "hl7.fhir.r4.core"],
    UseSemanticVersioning = true,
    LogConflicts = true
};

var resolver = new SearchParameterConflictResolver(options, logger);

// Resolve conflict among candidates with same code for a resource type
var winner = resolver.ResolveConflict(
    candidates: conflictingParams,
    code: "identifier",
    resourceType: "Patient",
    packageMetadata: packageMetadataLookup
);
```

### Resolution Strategy

1. **Explicit priority** - Packages listed in `PackagePriorityOrder` win (first = highest)
2. **Semantic versioning** - Highest version wins when no priority configured
3. **Alphabetical** - Package ID sort for deterministic ordering when versions equal

## Search expression parsing

`IExpressionParser` remains the entry point used by `SearchOptionsBuilder`. Parser instances are created per tenant and FHIR version, so `SearchParameterInfo` lookup and reference-target validation use the active definition manager and schema.

Handwritten syntax scanners parse ordinary parameters, modifiers, typed forward chains, nested `_has`, include/revinclude forms, `_not-referenced`, escaped separators (`\,`, `\$`, `\|`, `\\`), comma alternatives, dollar composites, comparator prefixes, `:missing`, `:text`, and `:of-type`. The scanners emit immutable syntax records; semantic binders remain the only schema-aware layer.

Malformed key or value syntax raises `InvalidSearchOperationException` with a positioned line/column diagnostic. Semantic failures retain the existing `SearchParameterNotSupportedException`, `BadSearchRequestException`, and resource-backed `InvalidSearchOperationException` messages. Atomic date, number, quantity, reference, string, token, and URI conversion continues to use the existing `*SearchValue.Parse` implementations.

The mandatory BenchmarkDotNet result and acceptance decision are recorded in [the handwritten syntax parser comparison](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/features/search/benchmarks/2026-07-11-handwritten-syntax-parser-comparison.md). The comparison uses the unchanged public-facade harness and six inputs against the original handwritten baseline. The replacement was classified as **Mixed**, with a -6.31% geometric-mean time change, and all ratified performance limits passed. It was not classified as **Faster**; no speedup is claimed.

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters)
- [FHIRPath](/docs/core-sdk/fhirpath)
