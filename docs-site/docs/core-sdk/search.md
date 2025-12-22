---
sidebar_position: 6
title: Search
description: FHIR search parameter indexing and extraction
---

# Ignixa.Search

Search parameter definitions, indexing, and value extraction for FHIR resources.

## Installation

```bash
dotnet add package Ignixa.Search
```

## Quick Start

```csharp
using Ignixa.Search;

// Get search parameters for Patient
var searchParams = SearchParameterRegistry.GetParameters("Patient");

// Extract search values from a resource
var extractor = new SearchValueExtractor();
var values = extractor.Extract(patient, searchParams);
```

## Search Parameters

### Get Parameters by Resource

```csharp
// All parameters for a resource type
var patientParams = SearchParameterRegistry.GetParameters("Patient");

// Specific parameter
var nameParam = SearchParameterRegistry.GetParameter("Patient", "name");
```

### Parameter Types

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

### Parameter Definition

```csharp
public class SearchParameter
{
    public string Name { get; }
    public string Code { get; }
    public SearchParamType Type { get; }
    public string Expression { get; }
    public IReadOnlyList<string> Base { get; }
    public IReadOnlyList<SearchModifier> Modifiers { get; }
}
```

## Value Extraction

### Extract All Values

```csharp
var extractor = new SearchValueExtractor();
var values = extractor.Extract(patient);

foreach (var (param, value) in values)
{
    Console.WriteLine($"{param.Code}: {value}");
}
```

### Extract Specific Parameter

```csharp
var nameValues = extractor.Extract(patient, "name");
// Returns: ["Smith", "John", "William"]
```

### Indexed Values

```csharp
public class SearchIndexValue
{
    public string ParameterCode { get; }
    public SearchParamType Type { get; }
    public object Value { get; }
    
    // Type-specific properties
    public string? StringValue { get; }
    public string? System { get; }
    public string? Code { get; }
    public decimal? NumberValue { get; }
    public DateTimeOffset? DateValue { get; }
    public string? ReferenceValue { get; }
}
```

## Search Value Types

### String Values

```csharp
// name = "Smith"
var values = extractor.Extract(patient, "name");
// ["Smith", "John", "William"]
```

### Token Values

```csharp
// identifier = "http://hospital.org|MRN123"
var values = extractor.Extract(patient, "identifier");
// [{ System: "http://hospital.org", Code: "MRN123" }]
```

### Reference Values

```csharp
// subject = Patient/123
var values = extractor.Extract(observation, "subject");
// [{ Reference: "Patient/123", Type: "Patient", Id: "123" }]
```

### Date Values

```csharp
// birthdate = 1990-05-15
var values = extractor.Extract(patient, "birthdate");
// [{ Start: 1990-05-15, End: 1990-05-15 }]
```

### Quantity Values

```csharp
// value-quantity = 75|http://unitsofmeasure.org|kg
var values = extractor.Extract(observation, "value-quantity");
// [{ Value: 75, System: "http://unitsofmeasure.org", Code: "kg" }]
```

## Custom Search Parameters

### Define Custom Parameter

```csharp
var customParam = new SearchParameter
{
    Name = "myExtension",
    Code = "my-extension",
    Type = SearchParamType.Token,
    Expression = "Patient.extension.where(url='http://example.org/ext').value",
    Base = ["Patient"]
};

SearchParameterRegistry.Register(customParam);
```

### Expression-Based Extraction

```csharp
var extractor = new SearchValueExtractor();
var values = extractor.ExtractWithExpression(
    resource, 
    "Observation.code.coding.where(system='http://loinc.org')"
);
```

## Indexing

### Build Search Index

```csharp
var indexer = new SearchIndexer();

// Index a single resource
var index = indexer.Index(patient);

// Bulk indexing
var indices = resources.Select(r => indexer.Index(r)).ToList();
```

### Index Structure

```csharp
public class SearchIndex
{
    public string ResourceType { get; }
    public string ResourceId { get; }
    public string ResourceVersionId { get; }
    public IReadOnlyList<SearchIndexValue> Values { get; }
}
```

## Query Building

### Build Query from Parameters

```csharp
var queryBuilder = new SearchQueryBuilder();

// Parse search parameters
var query = queryBuilder.Parse("Patient", "name=Smith&gender=male&birthdate=gt1980");

// Get structured query
foreach (var clause in query.Clauses)
{
    Console.WriteLine($"{clause.Parameter}: {clause.Modifier} {clause.Value}");
}
```

### Query Structure

```csharp
public class SearchQuery
{
    public string ResourceType { get; }
    public IReadOnlyList<SearchClause> Clauses { get; }
    public IReadOnlyList<IncludeClause> Includes { get; }
    public SortClause? Sort { get; }
    public int? Count { get; }
}
```

## Performance

### Caching

```csharp
var options = new SearchOptions
{
    CacheExpressions = true,
    CacheSize = 1000
};

var extractor = new SearchValueExtractor(options);
```

### Parallel Extraction

```csharp
var results = await Parallel.ForEachAsync(resources, async (resource, ct) =>
{
    return await Task.Run(() => extractor.Extract(resource), ct);
});
```

## Related Documentation

- [Search Parameters](/docs/server/fhir/search-parameters)
- [FHIRPath](/docs/core-sdk/fhirpath)
