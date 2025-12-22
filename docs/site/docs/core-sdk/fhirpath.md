---
sidebar_position: 4
title: FHIRPath
description: Compiled FHIRPath expression engine
---

# Ignixa.FhirPath

A high-performance FHIRPath implementation with expression compilation and caching, implementing the [FHIRPath N1 (Normative) specification](http://hl7.org/fhirpath/N1/).

Built using the [Superpower](https://github.com/datalust/superpower) parser combinator library (based on [Sprache](https://github.com/sprache/Sprache)), which provides token-driven parsing with friendly, human-readable error messages for invalid FHIRPath expressions.

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

```text
Patient.name                    // Direct child
Patient.name.family             // Nested path
Patient.name[0]                 // Index access
Patient.contact.name            // Through arrays
```

### Filtering

```text
name.where(use = 'official')    // Where clause
name.first()                    // First element
name.last()                     // Last element
name.exists()                   // Existence check
name.empty()                    // Empty check
```

### Operators

```text
birthDate < @2000-01-01         // Date comparison
age > 18                        // Numeric comparison
active and deceased.exists().not()   // Boolean logic
gender = 'male' or gender = 'female' // Boolean logic
name.family.startsWith('Sm')    // String operations
name.family.contains('ith')     // String operations
```

### Functions

See the [FHIRPath N1 specification](http://hl7.org/fhirpath/N1/) for the complete function reference. Commonly used functions include:

**Collection**: `exists()`, `empty()`, `count()`, `first()`, `last()`, `single()`, `where()`, `select()`, `all()`, `any()`

**String**: `contains()`, `startsWith()`, `endsWith()`, `matches()`, `replace()`, `substring()`, `length()`

**Type**: `ofType()`, `as()`, `is()`

**FHIR-specific**: `resolve()`, `extension()`, `memberOf()`

## Compilation & Caching

### Automatic Caching

The `Select()` extension method automatically caches both the parsed AST and compiled delegates:

```csharp
// First call: parse + compile + cache
var result1 = element.Select("name.family");

// Second call: uses cached compiled delegate
var result2 = element.Select("name.family");
```

**How it works:**

1. **AST Caching**: Expression string is parsed once and cached
2. **Delegate Compilation**: AST is compiled to a delegate if the pattern is supported
3. **Fallback**: Complex expressions fall back to interpreter automatically

The caching is automatic and internal - no configuration needed.

## Variables & Context

### Built-in Variables

```fhirpath
%resource          // Current resource (set via context.Resource)
%rootResource      // Root resource (set via context.RootResource)
```

### Custom Variables

Custom environment variables can be added to the evaluation context:

```csharp
var context = new EvaluationContext();
context.Resource = patientElement;  // Sets %resource variable

// Add custom variables
context.Environment["today"] = new[] { todayElement };

var result = element.Select("birthDate < %today", context);
```

## Error Handling

### Parse Errors

Invalid FHIRPath expressions throw `FormatException` when parsed:

```csharp
try
{
    var result = element.Select("invalid[[[path");
}
catch (FormatException ex)
{
    // "Tokenization failed: ..." or "Parsing failed: ..."
    Console.WriteLine($"Parse error: {ex.Message}");
}
```

### Evaluation Errors

Evaluation errors throw specific exceptions:

```csharp
try
{
    // single() throws when collection has multiple items
    var result = element.Select("name.single()");
}
catch (InvalidOperationException ex)
{
    // "single() called on collection with multiple items"
    Console.WriteLine($"Evaluation error: {ex.Message}");
}

try
{
    // Unsupported functions throw NotSupportedException
    var result = element.Select("customFunction()");
}
catch (NotSupportedException ex)
{
    // "Function 'customFunction' is not yet implemented"
    Console.WriteLine($"Unsupported: {ex.Message}");
}
```

:::note
FHIRPath follows propagation semantics for empty collections - operations on empty values typically return empty rather than throwing exceptions. Only constraint violations (like `single()` on multiple items) throw.
:::

## Architecture

The FHIRPath engine uses a three-stage pipeline:

```
Expression String → Parser → AST → Compiler/Evaluator → Results
```

### Components

**FhirPathParser**: Tokenizes and parses expression strings into an Abstract Syntax Tree (AST) using the [Superpower](https://github.com/datalust/superpower) parser combinator library. Provides human-readable error messages for invalid expressions.

**FhirPathDelegateCompiler**: Compiles common AST patterns to executable delegates for improved performance. Supports approximately 80% of typical search parameter patterns:
- Simple paths: `name`, `identifier`
- Two-level paths: `name.family`, `identifier.value`
- Where clauses: `telecom.where(system='phone')`
- Collection functions: `name.first()`, `identifier.exists()`

**FhirPathEvaluator**: Tree-walking interpreter that handles all expressions. Used as fallback when the compiler doesn't support a pattern.

### Direct API Access

For advanced scenarios, you can access the components directly:

```csharp
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;

// Parse to AST
var parser = new FhirPathParser();
Expression ast = parser.Parse("name.where(use = 'official').family");

// Create evaluator
var evaluator = new FhirPathEvaluator();

// Optionally compile to delegate
var compiler = new FhirPathDelegateCompiler(evaluator);
var compiled = compiler.TryCompile(ast);

// Execute
var context = new EvaluationContext { Resource = element };
IEnumerable<IElement> results = compiled != null
    ? compiled(element, context)
    : evaluator.Evaluate(ast, element, context);
```

:::note
Most applications should use the `Select()`, `Scalar()`, `IsTrue()` extension methods which handle caching automatically. Direct API access is only needed for custom caching strategies or AST inspection.
:::

## Performance Tips

1. **Automatic caching works best with literal expressions** - use the same string repeatedly to benefit from cached compiled delegates
2. **Use specific paths** instead of wildcards - simpler expressions compile better
3. **Cache evaluation results** when evaluating same expression on same data multiple times
4. **Prefer simple patterns** - path navigation and basic predicates compile to fast delegates; complex expressions fall back to interpreter

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [Validation](/docs/core-sdk/validation)
