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
```

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

    // Whether this parameter is searchable
    public bool IsSearchable { get; }

    // Whether this parameter is supported
    public bool IsSupported { get; }
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

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters)
- [FHIRPath](/docs/core-sdk/fhirpath)
