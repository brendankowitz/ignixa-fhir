# Ignixa.SqlOnFhir

SQL on FHIR v2 implementation for analytics queries. Enables SQL-like querying over FHIR resources by flattening hierarchical FHIR data into tabular views.

## Why Use This Package?

- **SQL on FHIR v2 spec compliance**: Implements the official specification
- **FHIRPath-based column extraction**: Uses FHIRPath for flexible data access
- **Array unnesting**: Handle FHIR arrays (name, identifier, etc.) with forEach
- **Type conversion**: Automatic conversion of FHIR types to SQL types

## Installation

```bash
dotnet add package Ignixa.SqlOnFhir
```

## Quick Start

### Define a View

```csharp
using Ignixa.SqlOnFhir.Models;

// Create a view definition for patient demographics
var viewDefinition = new ViewDefinition
{
    Resource = "Patient",
    Select = new List<SelectGroup>
    {
        new SelectGroup
        {
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "id" },
                new() { Name = "birth_date", Path = "birthDate", Type = "date" },
                new() { Name = "gender", Path = "gender", Type = "string" }
            }
        }
    }
};
```

### Execute View Against Resources

```csharp
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.Abstractions;

// Get FHIR resources
IEnumerable<IElement> patients = GetPatientElements();

// Create evaluator
var evaluator = new SqlOnFhirEvaluator(schema);

// Execute view
var rows = evaluator.Evaluate(viewDefinition, patients);

// Process results
foreach (var row in rows)
{
    var id = row["id"];
    var birthDate = row["birth_date"];
    var gender = row["gender"];

    Console.WriteLine($"Patient {id}: {gender}, born {birthDate}");
}
```

## Advanced Features

### Array Unnesting with forEach

Extract values from FHIR arrays:

```csharp
var viewDefinition = new ViewDefinition
{
    Resource = "Patient",
    Select = new List<SelectGroup>
    {
        // Base columns (one row per patient)
        new SelectGroup
        {
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "id" }
            }
        },
        // Name columns (one row per name)
        new SelectGroup
        {
            ForEach = "name",  // Unnest name array
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "name_use", Path = "use" },
                new() { Name = "family", Path = "family" },
                new() { Name = "given", Path = "given.first()" }
            }
        }
    }
};

// Result: One row per patient name
// id | name_use | family | given
// 123| official | Doe    | John
// 123| maiden   | Smith  | Jane
```

### Filtering with WHERE

```csharp
var viewDefinition = new ViewDefinition
{
    Resource = "Patient",
    Where = new List<WhereClause>
    {
        new() { Path = "active = true" },
        new() { Path = "birthDate > @2000-01-01" }
    },
    Select = new List<SelectGroup>
    {
        new SelectGroup
        {
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "id" },
                new() { Name = "name", Path = "name.first().family" }
            }
        }
    }
};

// Only evaluates active patients born after 2000
```

### Using Constants

Parameterize your views:

```csharp
var viewDefinition = new ViewDefinition
{
    Resource = "Observation",
    Constant = new List<ViewConstant>
    {
        new() { Name = "minValue", Value = "100" }
    },
    Where = new List<WhereClause>
    {
        new() { Path = "value.value >= %minValue" }
    },
    Select = new List<SelectGroup>
    {
        new SelectGroup
        {
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "id" },
                new() { Name = "value", Path = "value.value" }
            }
        }
    }
};
```

### Multiple Select Groups

Create multiple row groups from a single resource:

```csharp
var viewDefinition = new ViewDefinition
{
    Resource = "Patient",
    Select = new List<SelectGroup>
    {
        // First SELECT: Names (one row per name)
        new SelectGroup
        {
            ForEach = "name",
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "%resource.id" },  // Use %resource to access root
                new() { Name = "family", Path = "family" }
            }
        },
        // Second SELECT: Identifiers (one row per identifier)
        new SelectGroup
        {
            ForEach = "identifier",
            Column = new List<ViewColumnDefinition>
            {
                new() { Name = "id", Path = "%resource.id" },
                new() { Name = "identifier_value", Path = "value" }
            }
        }
    }
};

// Result: Multiple row groups
// Group 1 (names):
// id | family
// 123| Doe
// 123| Smith
//
// Group 2 (identifiers):
// id | identifier_value
// 123| MRN123
// 123| SSN456
```

## SQL Type Conversions

Supported column types:

```csharp
new ViewColumnDefinition
{
    Name = "string_col",
    Path = "someString",
    Type = "string"
};

new ViewColumnDefinition
{
    Name = "int_col",
    Path = "someInteger",
    Type = "integer"
};

new ViewColumnDefinition
{
    Name = "decimal_col",
    Path = "someDecimal",
    Type = "decimal"
};

new ViewColumnDefinition
{
    Name = "bool_col",
    Path = "someBoolean",
    Type = "boolean"
};

new ViewColumnDefinition
{
    Name = "date_col",
    Path = "someDate",
    Type = "date"
};

new ViewColumnDefinition
{
    Name = "datetime_col",
    Path = "someDatetime",
    Type = "datetime"
};
```

## Integration with Other Packages

- **Ignixa.FhirPath**: Used to evaluate column Path expressions
- **Ignixa.Abstractions**: Works with `IElement` representations
- **Ignixa.Specification**: Uses schema for type information

## Use Cases

### Analytics and Reporting

Flatten FHIR resources for BI tools:

```csharp
// Create views for your BI dashboard
var patientView = CreatePatientDemographicsView();
var observationView = CreateVitalsView();
var conditionView = CreateDiagnosesView();

// Export to SQL database
var patientRows = evaluator.Evaluate(patientView, patients);
await BulkInsertToSql("patient_demographics", patientRows);
```

### Data Export for Research

Extract specific data elements for research datasets:

```csharp
var researchView = new ViewDefinition
{
    Resource = "Patient",
    Where = new List<WhereClause>
    {
        new() { Path = "meta.tag.code contains 'research-approved'" }
    },
    Select = new List<SelectGroup> { /* De-identified columns */ }
};
```

## Specification Compliance

This implementation follows the [SQL on FHIR v2 specification](https://sql-on-fhir.org/).

## License

MIT License - see LICENSE file in repository root
