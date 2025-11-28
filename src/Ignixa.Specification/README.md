# Ignixa.Specification

FHIR specification data and structure providers for R4, R4B, R5, and STU3. Provides schema definitions, type information, and structural metadata for all FHIR versions.

## Why Use This Package?

- **Multi-version support**: R4, R4B, R5, and STU3 in a single package
- **Complete metadata**: All StructureDefinitions, elements, types, and constraints
- **High performance**: Generated code with zero runtime reflection
- **ISchema implementation**: Works seamlessly with Ignixa.Abstractions

## Installation

```bash
dotnet add package Ignixa.Specification
```

## Quick Start

### Loading a FHIR Schema

```csharp
using Ignixa.Specification.Generated;
using Ignixa.Abstractions;

// Get the R4 schema provider
ISchema schema = new R4CoreSchemaProvider();

// Look up a type definition
ITypeExtended? patientType = schema.GetTypeByName("Patient");

if (patientType != null)
{
    Console.WriteLine($"Type: {patientType.Name}");
    Console.WriteLine($"Base: {patientType.BaseTypeName}");
    Console.WriteLine($"Abstract: {patientType.IsAbstract}");

    // Enumerate elements
    foreach (var element in patientType.Elements)
    {
        Console.WriteLine($"  {element.Name}: {element.DefaultTypeName} [{element.Min}..{element.Max}]");
    }
}
```

### Working with Elements

```csharp
using Ignixa.Specification.Generated;

var schema = new R4CoreSchemaProvider();
var patient = schema.GetTypeByName("Patient");

// Get a specific element
var nameElement = patient.Elements.FirstOrDefault(e => e.Name == "name");

Console.WriteLine($"Name: {nameElement?.Name}");
Console.WriteLine($"Type: {nameElement?.DefaultTypeName}");  // "HumanName"
Console.WriteLine($"Cardinality: {nameElement?.Min}..{nameElement?.Max}");  // "0..*"
Console.WriteLine($"Short: {nameElement?.Short}");  // "A name associated with the patient"
```

### Checking Constraints

```csharp
var schema = new R4CoreSchemaProvider();
var patient = schema.GetTypeByName("Patient");

// Get FHIRPath constraints
foreach (var constraint in patient.Constraints)
{
    Console.WriteLine($"{constraint.Key}: {constraint.Human}");
    Console.WriteLine($"  Expression: {constraint.Expression}");
    Console.WriteLine($"  Severity: {constraint.Severity}");
}
```

### All Available Schemas

```csharp
using Ignixa.Specification.Generated;

// FHIR R4 (v4.0.1)
var r4Schema = new R4CoreSchemaProvider();

// FHIR R4B (v4.3.0)
var r4bSchema = new R4BSchemaProvider();

// FHIR R5 (v5.0.0)
var r5Schema = new R5SchemaProvider();

// FHIR STU3 (v3.0.2)
var stu3Schema = new STU3SchemaProvider();
```

## Common Use Cases

### Validation

```csharp
using Ignixa.Specification.Generated;
using Ignixa.Abstractions;

// Use with Ignixa.Validation
var schema = new R4CoreSchemaProvider();
var validator = new MyValidator(schema);

IElement element = GetPatientElement();
var result = validator.Validate(element);
```

### Building FHIRPath Evaluators

```csharp
using Ignixa.Specification.Generated;

var schema = new R4CoreSchemaProvider();

// Get type info for FHIRPath evaluation
var observationType = schema.GetTypeByName("Observation");
var valueElement = observationType.Elements.FirstOrDefault(e => e.Name.StartsWith("value"));

// valueElement.IsChoiceElement == true
// valueElement.Name == "value[x]"
```

### Exploring the Type System

```csharp
using Ignixa.Specification.Generated;

var schema = new R4CoreSchemaProvider();

// Get all resource types
var resourceType = schema.GetTypeByName("Resource");
var allResources = schema.GetAllTypes()
    .Where(t => t.BaseTypeName == "Resource" || t.BaseTypeName == "DomainResource");

foreach (var resource in allResources)
{
    Console.WriteLine(resource.Name);
}
// Output: Patient, Observation, Condition, etc.

// Get all primitive types
var primitives = schema.GetAllTypes()
    .Where(t => t.BaseTypeName == "PrimitiveType");

foreach (var primitive in primitives)
{
    Console.WriteLine(primitive.Name);
}
// Output: string, integer, boolean, date, dateTime, etc.
```

## Version-Specific Features

### R4 vs R4B vs R5 Differences

```csharp
// R4: Patient.contact.organization is Reference(Organization)
var r4Schema = new R4CoreSchemaProvider();
var r4Patient = r4Schema.GetTypeByName("Patient");

// R4B: Added Patient.contact.period
var r4bSchema = new R4BSchemaProvider();
var r4bPatient = r4bSchema.GetTypeByName("Patient");

// R5: New resources and elements
var r5Schema = new R5SchemaProvider();
var subscriptionTopic = r5Schema.GetTypeByName("SubscriptionTopic");  // R5 only
```

## Performance

- **Generated code**: All schema data is pre-compiled, no runtime parsing
- **Lazy loading**: Type definitions loaded on-demand
- **Cached lookups**: Fast O(1) type name lookups

## Integration with Other Packages

- **Ignixa.Abstractions**: Implements `ISchema` and `ITypeExtended`
- **Ignixa.Validation**: Uses schemas for structure validation
- **Ignixa.Search**: Uses element metadata for search parameter definitions
- **Ignixa.FhirPath**: Uses type info for expression evaluation

## How It's Generated

This package is code-generated from official FHIR StructureDefinitions using the Ignixa code generator. To regenerate (for contributors):

```bash
cd codegen
./generate.ps1 -FhirVersion All  # Windows
./generate.sh all                # Linux/Mac
```

## FHIR Specification Compliance

This package includes complete metadata from:
- [FHIR R4 (v4.0.1)](https://hl7.org/fhir/R4/)
- [FHIR R4B (v4.3.0)](https://hl7.org/fhir/R4B/)
- [FHIR R5 (v5.0.0)](https://hl7.org/fhir/R5/)
- [FHIR STU3 (v3.0.2)](https://hl7.org/fhir/STU3/)

## License

MIT License - see LICENSE file in repository root
