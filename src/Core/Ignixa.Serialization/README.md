# Ignixa.Serialization

High-performance FHIR JSON serialization library. Provides utilities for working with FHIR resources using System.Text.Json.Nodes, including source node navigation and enum literal conversions.

## Why Use This Package?

- **JsonNode-based**: Modern JSON handling using `System.Text.Json.Nodes`
- **ISourceNavigator support**: Create source navigators from JSON for validation and FHIRPath evaluation
- **Enum utilities**: Convert between FHIR enum codes and .NET enums

## Installation

```bash
dotnet add package Ignixa.Serialization
```

## Quick Start

### Creating Source Nodes from JSON

```csharp
using Ignixa.Serialization;
using Ignixa.Abstractions;

// Parse JSON string to ResourceJsonNode
var json = """
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{
    "family": "Doe",
    "given": ["John"]
  }]
}
""";

// Create ISourceNavigator for navigation
var sourceNode = JsonSourceNodeFactory.Parse(json).ToSourceNavigator();

// Navigate the source node
Console.WriteLine(sourceNode.Name);  // "root"
Console.WriteLine(sourceNode.ResourceType);  // "Patient"

foreach (var child in sourceNode.Children())
{
    Console.WriteLine($"{child.Name}: {child.Text}");
}
// Output:
// resourceType: Patient
// id: 123
// name: (complex)
```

### Working with Enum Literals

FHIR uses string codes for enums. This package provides utilities to convert them:

```csharp
using Ignixa.Serialization;
using Ignixa.Specification.ValueSets.Normative;

// FHIR enum with EnumLiteral attributes
public enum AdministrativeGender
{
    [EnumLiteral("male")]
    Male,

    [EnumLiteral("female")]
    Female,

    [EnumLiteral("other")]
    Other,

    [EnumLiteral("unknown")]
    Unknown
}

// Convert code string to enum
var gender = EnumUtility.ParseLiteral<AdministrativeGender>("male");
// gender == AdministrativeGender.Male

// Convert enum to code string
var code = gender.GetLiteral();
// code == "male"
```

## Integration with Other Packages

- **Ignixa.Abstractions**: Implements `ISourceNavigator` for JSON data
- **Ignixa.Specification**: Provides enum types with `[EnumLiteral]` attributes
- **Ignixa.Validation**: Uses `ISourceNavigator` for validation
- **Ignixa.FhirPath**: Can evaluate expressions against source navigators

## Performance Notes

- **JsonNode**: Uses modern System.Text.Json for best performance
- **Source nodes**: Lazy evaluation - children only materialized when accessed
- **Enum lookup**: Uses cached dictionaries for O(1) lookup

## License

MIT License - see LICENSE file in repository root
