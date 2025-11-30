# Ignixa.Search

FHIR search parameter indexing and query infrastructure. Provides comprehensive support for FHIR search operations including parameter definitions, indexing, and query parsing.

## Why Use This Package?

- **Full FHIR search support**: Implements all FHIR search parameter types (string, token, reference, date, quantity, number, composite)
- **High-performance indexing**: Extract search values from FHIR resources using FHIRPath
- **Query parsing**: Parse FHIR search queries from URL parameters
- **Custom search parameters**: Support for custom SearchParameter definitions from Implementation Guides

## Installation

```bash
dotnet add package Ignixa.Search
```

## Quick Start

### Creating a Search Indexer (Easy Way)

For a specific FHIR version, use the factory:

```csharp
using Ignixa.Search.Indexing;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;

// Create logger factory
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

// Get FHIR schema provider for your version
var schemaProvider = new R4CoreSchemaProvider();
// Also available: R4BCoreSchemaProvider, R5CoreSchemaProvider, STU3CoreSchemaProvider

// Create indexer with all dependencies automatically configured
var indexer = SearchIndexerFactory.CreateInstance(
    schemaProvider,
    loggerFactory);

// That's it! Now you can index resources
```

### Indexing a Resource

Extract search values from a FHIR resource:

```csharp
using Ignixa.Abstractions;

// Index a Patient resource
IElement patientElement = GetPatientElement();
var searchIndexEntries = indexer.Extract(patientElement);

// searchIndexEntries contains:
// - name -> "John", "Doe" (string search params)
// - birthdate -> DateTime (date search params)
// - identifier -> token values (token search params)
// - etc.

foreach (var entry in searchIndexEntries)
{
    Console.WriteLine($"{entry.SearchParameter.Code}: {entry.Value}");
}
```

### Parsing Search Queries

Parse FHIR search queries from URL parameters:

```csharp
using Ignixa.Search.Parsing;

// Parse query string into parameters
var parameterParser = new QueryParameterParser();
var parameters = parameterParser.Parse("name=John&birthdate=gt2000-01-01&_count=50");

// Build search options
var expressionParser = new SearchParameterExpressionParser(...); // from DI
var builder = new SearchOptionsBuilder(expressionParser, searchParameterDefinitionManager);

var searchOptions = builder.Build("Patient", parameters);

// searchOptions contains parsed search criteria:
// - Expression: parsed search conditions
// - MaxItemCount: 50
// - ContinuationToken: for pagination
```

## Search Parameter Types

### String Search

Matches partial strings (case-insensitive, ignoring accents):

```
GET /Patient?name=John
GET /Patient?address=Boston
```

### Token Search

Exact match on codes, identifiers, booleans:

```
GET /Patient?identifier=123456
GET /Patient?gender=male
GET /Patient?active=true

// Token with system
GET /Patient?identifier=http://hospital.org|123456
```

### Reference Search

```
// Reference to another resource
GET /Observation?patient=Patient/123
GET /Observation?subject=Patient/456

// Reference by identifier (chaining)
GET /Observation?patient.identifier=123456
```

### Date/DateTime Search

```
// Date comparisons
GET /Patient?birthdate=2000-01-01         // equals
GET /Patient?birthdate=gt2000-01-01       // greater than
GET /Patient?birthdate=lt2010-12-31       // less than
GET /Patient?birthdate=ge2000-01-01       // greater or equal
GET /Patient?birthdate=le2010-12-31       // less or equal

// Date ranges
GET /Patient?birthdate=ge2000-01-01&birthdate=lt2001-01-01
```

### Quantity Search

```
// Quantity with value and unit
GET /Observation?value-quantity=5.4|http://unitsofmeasure.org|mg

// Quantity comparisons
GET /Observation?value-quantity=gt100|http://unitsofmeasure.org|mg
```

### Number Search

```
// Numeric values
GET /RiskAssessment?probability=0.8
GET /RiskAssessment?risk=gt0.5
```

## Search Modifiers

```
// String modifiers
GET /Patient?name:exact=John              // Exact match
GET /Patient?name:contains=oh             // Contains
GET /Patient?address-city:exact=Boston

// Token modifiers
GET /Patient?identifier:of-type=MR|12345  // Identifier of type

// Reference modifiers
GET /Observation?subject:Patient=123      // Type modifier
```

## Common Scenarios

### Building Search Indices for Storage

```csharp
// Index all patients in database
foreach (var patient in patients)
{
    var entries = indexer.Extract(patient);

    foreach (var entry in entries)
    {
        // Store in search parameter table
        await SaveSearchIndexEntry(
            patient.Id,
            entry.SearchParameter.Code,
            entry.SearchParameter.Type,
            entry.Value);
    }
}
```

### Implementing FHIR Search API

```csharp
using Ignixa.Search.Parsing;
using Microsoft.AspNetCore.Http;

// Parse incoming HTTP query string
var parameterParser = new QueryParameterParser();
var parameters = parameterParser.Parse(httpContext.Request.Query);

// Build search options
var searchOptions = searchOptionsBuilder.Build(resourceType, parameters);

// Execute search against database
var results = await ExecuteSearch(resourceType, searchOptions);
```

### Custom Search Parameters

```csharp
// Load custom SearchParameter from Implementation Guide
var customSearchParam = new SearchParameterInfo
{
    Code = "custom-identifier",
    Type = SearchParamType.Token,
    Expression = "Patient.extension.where(url='http://example.org/custom').value as Identifier",
    Url = "http://example.org/SearchParameter/custom-identifier"
};

// Register with definition manager
searchParameterDefinitionManager.AddSearchParameter(customSearchParam);

// Now indexer will extract values for custom-identifier
var entries = indexer.Extract(patient);
```

## Integration with Other Packages

- **Ignixa.Specification**: Provides default SearchParameter definitions for R4/R4B/R5
- **Ignixa.FhirPath**: Used to evaluate SearchParameter expressions
- **Ignixa.PackageManagement**: Load custom SearchParameters from Implementation Guides
- **Ignixa.DataLayer.***: Storage implementations use search indices for querying

## Performance Considerations

- **Compiled FHIRPath**: Search parameter expressions are compiled for faster indexing
- **Incremental indexing**: Only re-index changed resources
- **Selective indexing**: Skip indexing for search parameters not used in your queries

## FHIR Specification Compliance

This implementation follows:
- [FHIR R4 Search](https://hl7.org/fhir/R4/search.html)
- [FHIR Search Parameter Registry](https://hl7.org/fhir/R4/searchparameter-registry.html)

## License

MIT License - see LICENSE file in repository root
