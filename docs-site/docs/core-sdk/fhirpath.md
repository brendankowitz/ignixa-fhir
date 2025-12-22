---
sidebar_position: 4
title: FHIRPath
description: Compiled FHIRPath expression engine
---

# Ignixa.FhirPath

A high-performance FHIRPath implementation with expression compilation and caching, implementing the [FHIRPath N1 (Normative) specification](http://hl7.org/fhirpath/N1/).

## Installation

```bash
dotnet add package Ignixa.FhirPath
```

## Quick Start

```csharp
using Ignixa.FhirPath.Evaluation;

// Parse FHIR JSON
var sourceNode = JsonSourceNavigator.Parse(patientJson);
var element = sourceNode.ToElement(schema);

// Evaluate FHIRPath
var names = element.Select("name.given");
var isActive = element.IsTrue("active = true");
```

## Evaluation Methods

### Select

Returns a collection of matching elements:

```csharp
// Single path
var names = element.Select("name.given");

// Union paths
var identifiers = element.Select("identifier.value | id");

// With predicates
var activeContacts = element.Select("contact.where(active = true)");
```

### Scalar

Returns a single scalar value:

```csharp
var birthDate = element.Scalar("birthDate");
var age = element.Scalar("age()");
var count = element.Scalar("name.count()");
```

### IsTrue / IsBoolean

Returns boolean evaluation:

```csharp
// Check if expression evaluates to true
var isActive = element.IsTrue("active = true");

// Check specific boolean value
var isInactive = element.IsBoolean("active", false);
```

## Path Syntax

### Navigation

```fhirpath
Patient.name                    // Direct child
Patient.name.family             // Nested path
Patient.name[0]                 // Index access
Patient.contact.name            // Through arrays
```

### Filtering

```fhirpath
// Where clause
name.where(use = 'official')

// First/last
name.first()
name.last()

// Existence
name.exists()
name.empty()
```

### Operators

```fhirpath
// Comparison
birthDate < @2000-01-01
age > 18

// Boolean
active and deceased.exists().not()
gender = 'male' or gender = 'female'

// String
name.family.startsWith('Sm')
name.family.contains('ith')
```

### Functions

| Function | Description | Example |
|----------|-------------|---------|
| `exists()` | Element exists | `name.exists()` |
| `empty()` | No elements | `name.empty()` |
| `count()` | Element count | `name.count()` |
| `first()` | First element | `name.first()` |
| `last()` | Last element | `name.last()` |
| `single()` | Exactly one | `identifier.single()` |
| `where()` | Filter | `name.where(use='official')` |
| `select()` | Project | `name.select(family)` |
| `all()` | All match | `name.all(family.exists())` |
| `any()` | Any matches | `name.any(use='official')` |
| `contains()` | String contains | `name.family.contains('th')` |
| `startsWith()` | String starts | `id.startsWith('pat')` |
| `matches()` | Regex match | `name.family.matches('^Sm.*')` |
| `ofType()` | Type filter | `value.ofType(Quantity)` |
| `as()` | Cast | `value.as(string)` |
| `resolve()` | Resolve reference | `subject.resolve()` |

## Compilation & Caching

### Automatic Caching

The `Select()` extension method automatically caches compiled expressions:

```csharp
// First call: parse + compile + cache
var result1 = element.Select("name.family");

// Second call: cached delegate
var result2 = element.Select("name.family");
```

### Pre-Compilation

For known expressions, pre-compile for best performance:

```csharp
var compiler = new FhirPathCompiler();

// Compile once
var compiled = compiler.Compile("name.where(use='official').family");

// Reuse many times
foreach (var patient in patients)
{
    var names = compiled.Evaluate(patient);
}
```

### Cache Configuration

```csharp
var options = new FhirPathOptions
{
    CacheSize = 1000,
    EnableCompilation = true
};

var evaluator = new FhirPathEvaluator(options);
```

## Variables & Context

### Built-in Variables

```fhirpath
%resource          // Current resource
%rootResource      // Root resource
%context           // Evaluation context
%ucum              // UCUM unit system
```

### Custom Variables

```csharp
var context = new EvaluationContext
{
    Variables = new Dictionary<string, IElement>
    {
        ["today"] = FhirDateTime.Parse(DateTime.Today.ToString("yyyy-MM-dd"))
    }
};

var result = element.Select("birthDate < %today", context);
```

## Reference Resolution

### With Resolver

```csharp
var resolver = new BundleResolver(bundle);
var context = new EvaluationContext { Resolver = resolver };

// Resolve references
var patient = element.Select("subject.resolve()", context).First();
```

### Custom Resolver

```csharp
public class MyResolver : IFhirPathResolver
{
    public IElement? Resolve(string reference)
    {
        // Fetch from database, cache, etc.
        return repository.Read(reference);
    }
}
```

## Error Handling

### Parse Errors

```csharp
try
{
    var compiled = compiler.Compile("invalid[[[path");
}
catch (FhirPathException ex)
{
    Console.WriteLine($"Parse error: {ex.Message}");
}
```

### Evaluation Errors

```csharp
try
{
    var result = element.Select("name.family / 0");
}
catch (FhirPathException ex)
{
    Console.WriteLine($"Evaluation error: {ex.Message}");
}
```

## Performance Tips

1. **Reuse compiled expressions** for repeated evaluations
2. **Use specific paths** instead of wildcards
3. **Cache results** when evaluating same expression on same data
4. **Avoid `resolve()`** in hot paths without caching

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [Validation](/docs/core-sdk/validation)
