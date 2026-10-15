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

### How values are indexed

The converters in `Ignixa.Search.Indexing.Converters` follow the search page's mapping for each data type; the SQL rows, the in-memory index and FileSystem sidecars all store what they produce.

| Element | Indexed as | So |
|---------|-----------|----|
| `ContactPoint` | token with `ContactPoint.value` as the code and no system (R4 §3.1.1.4.10, R5 §3.2.1.5.13: the pipe "SHALL NOT be used") | `telecom=555-1234` matches; `telecom=home\|555-1234` and `telecom=phone\|555-1234` do not. Use `phone` / `email` to select by `ContactPoint.system`. |
| `Quantity` | `system\|code`, plus a second value with the stated `unit` as the code and no system when it differs from the code (R4 §3.1.1.4.11) | `value-quantity=185\|\|lbs` matches code `[lb_av]` with unit `lbs`; `185\|http://unitsofmeasure.org\|lbs` does not. |
| `Range` | number range (`low.value`..`high.value`) or quantity range with the bound's system, code and unit; an open bound stays open | R4 `probability=lt0.3` matches `probabilityRange` 0.2..0.5. |
| `canonical` under a reference parameter | reference parsed like a literal reference, with any `\|version` removed (no reference column holds a business version) | R5 `_profile=http://example.org/fhir/StructureDefinition/p` matches `meta.profile` with or without a version; a search naming a version matches every version. |
| `Reference` / `uri` to `urn:` | untyped reference whose id is the whole urn (contained `#` references stay unindexed) | `subject=urn:uuid:...` matches and `subject:missing=true` does not. |

`CompactSearchIndexConverter` (FileSystem sidecars) and `CompactSearchValueConverter` (one value) write each value as an object with a kind discriminator `k` and only its non-default fields, and read it back exactly: token text case, identifier type, reference kind, min/max sort flags, canonical version and fragment, empty-versus-absent system and code, and composites component by component. Reading anything else (including a sidecar written by the earlier, lossy format) throws `JsonException`; the FileSystem data layer logs the sidecar's path and fails the read, search or write rather than skipping the resource.

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

## In-memory search

`Ignixa.Search.InMemory` evaluates parsed searches over index entries held in memory (the FileSystem data layer and `cli-on-fhir` use it).

`SearchQueryInterpreter` turns `SearchOptions.Expression` into a predicate over `(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)` rows. Each parameter is compiled into a test of one index entry, so every field of a criterion is matched on the same entry: `code=http://loinc.org|8867-4` needs one coding with both, and a composite needs one value of each component. Date, number and quantity prefixes compare the bound that `DateRangeComparisonSemantics` and `NumericRangeComparisonSemantics` (the tables the SQL backend renders) name; a bound the value lacks never matches. String search folds case and accents, `|code` matches a code without a system, and `_id` / `_type` read the resource key.

`InMemorySearchEngine` adds the features that read other resource types, each opted into with `InMemorySearchFeatures`. A feature that is not enabled throws `SearchOperationNotSupportedException`; the default interpreter enables none.

:::caution Experimental API
`IInMemorySearchSource`, `InMemorySearchEngine`, `InMemorySearchFeatures`, `LastUpdatedSearchIndexer` and the `SearchQueryInterpreter(source, features)` constructor are marked `[Experimental("IGNIXAEXP001")]`: Ignixa.Search is a stable package, and these may still change shape. Using them is a compile error until you opt in, for example with `<NoWarn>$(NoWarn);IGNIXAEXP001</NoWarn>` in the consuming project.
:::

A query shape the interpreter cannot evaluate (a field the parameter's definition can never hold, an unknown operator) is rejected when the predicate is built, before any entry is read. So is a shipped composite whose component expressions select elements that index as another kind than the component declares, since no query could ever match it: R5 `Encounter-location-period` and R5/R6 `Composition-section-code-text`. They are listed in a static table, which a census test checks against the FHIRPath analyzer for every shipped version, so loading and searching pay nothing for it; composites registered later from packages are not checked. Today the table is a backstop: both are refused before the interpreter reads it (`Composition-section-code-text` by the binder, because its second component is `Special`, a Narrative with no value grammar, which fails as `SearchOperationNotSupportedException` naming the parameter and component; `Encounter-location-period` by the options builder, which records it as unsupported because a component does not resolve). R6 `TestScript-scope-artifact-*` left the table when canonicals began indexing under reference parameters. `DocumentReference-relationship`, which every version from STU3 to R6 publishes with its two component expressions swapped, is repaired when the definitions load (component 0, `relatesto`, selects `relatesTo.target`; component 1, `relation`, selects `relatesTo.code`). That reorders the composite's values in the in-memory and FileSystem compact indexes; SQL rows are unchanged, because the reference-token table has one column set per kind, and the search parameter hash does not change. A query, though, is positional in every backend: SQL takes each reference-token slot's value from the position that declares its kind, so `relationship=<system>|replaces$DocumentReference/dr0` (the values swapped) matches nothing in SQL, as in memory. An index entry of an unexpected kind, such as a composite component the indexer typed as a Quantity under a token component, simply does not match. Results are combined by `ResourceKey`, so a source may return fresh index collections on every enumeration.

```csharp
using Ignixa.Search.InMemory;

// The caller implements IInMemorySearchSource: enumerate a type's indexed resources, resolve one by type and id.
var engine = new InMemorySearchEngine(source, InMemorySearchFeatures.All);

var matches = engine.Search(options);                  // filter (chains, _has) and _sort
var page = matches.Take(options.MaxItemCount).ToList(); // paging is the caller's
var included = engine.Include(page, options, 51);      // _include / _revinclude (with :iterate) for that page, at most 51
```

| Feature | Behavior |
|---------|----------|
| `Chains` | Forward chains to any depth; a reference with a base URI, without a type, or to a resource the source does not hold matches nothing |
| `ReverseChains` | `_has`, including nested and chained forms |
| `Sort` | Date, number, quantity, string, token, uri and `_id`; lowest value ascending, highest descending; missing values last; ties by `_lastUpdated`, then id |
| `Include` / `RevInclude` | Resolved for the page passed in, in discovery order, each resource once and never a match; `:iterate` repeats until nothing new is added or the caller's maximum is reached; a reverse include must name its parameter |

`ElementSearchIndexer` emits no `_lastUpdated` entry because a database reads it from a column. Wrap an in-memory index's indexer in `LastUpdatedSearchIndexer` to add one from `meta.lastUpdated`; only a FHIR instant (with a time zone) is indexed, and anything else is logged as a warning.

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters)
- [FHIRPath](/docs/core-sdk/fhirpath)
