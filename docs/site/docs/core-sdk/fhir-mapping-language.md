---
sidebar_position: 10
title: FHIR Mapping Language
description: Execute FHIR Mapping Language (FML) transformations
---

# FHIR Mapping Language

The `Ignixa.FhirMappingLanguage` package provides execution of FHIR Mapping Language (FML) transformations via a compiled expression AST.

## Installation

```bash
dotnet add package Ignixa.FhirMappingLanguage
```

## Quick Start

```csharp
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Abstractions;

// Define the mapping context
var context = new MappingContext(sourceElement, targetElement);

// Create the evaluator
var options = new MappingEvaluatorOptions();
var evaluator = new MappingEvaluator(options);

// Execute the mapping (assuming a pre-parsed StructureMap)
var result = await evaluator.EvaluateAsync(structureMap, context, cancellationToken);

if (result.IsSuccessful)
{
    Console.WriteLine("Mapping succeeded");
    Console.WriteLine($"Output: {result.Value}");
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error: {error.Message}");
    }
}
```

## Mapping Evaluation

### MappingEvaluator

Executes pre-parsed StructureMap transformations using FHIRPath expressions.

```csharp
var evaluator = new MappingEvaluator(options);

var result = await evaluator.EvaluateAsync(
    structureMap,     // Pre-parsed StructureMap
    context,          // MappingContext with source/target elements
    cancellationToken
);

// Check result
if (result.IsSuccessful)
{
    var output = result.Value;  // Transformed element
}
else
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"Error at {error.Location}: {error.Message}");
        if (error.InnerException != null)
        {
            Console.WriteLine($"Details: {error.InnerException.Message}");
        }
    }
}
```

### MappingContext

```csharp
public class MappingContext : ITransformContext
{
    // Create context from source and target elements
    public MappingContext(IElement source, IElement target)
    {
        Source = source;
        Target = target;
    }

    // Source element being transformed
    public IElement Source { get; }

    // Target element being populated
    public IElement Target { get; }

    // Custom variables
    public Dictionary<string, object> Variables { get; }

    // Pass-through metadata
    public Dictionary<string, object> Metadata { get; }
}
```

## Execution Results

### ExecutionResult\<T\>

```csharp
public class ExecutionResult<T>
{
    // True if evaluation succeeded
    public bool IsSuccessful { get; }

    // The result value (on success)
    public T? Value { get; }

    // Execution errors (if any)
    public IReadOnlyList<ExecutionError> Errors { get; }
}
```

### ExecutionError

```csharp
public class ExecutionError
{
    // Error message
    public string Message { get; }

    // Source location information
    public ISourcePositionInfo? Location { get; }

    // Underlying exception (if any)
    public Exception? InnerException { get; }
}
```

## Error Handling

### Error Mode

```csharp
var options = new MappingEvaluatorOptions
{
    // Stop on first error
    ErrorMode = ErrorMode.FailFast,
    
    // Or: collect all errors
    // ErrorMode = ErrorMode.Collect
};

var result = await evaluator.EvaluateAsync(structureMap, context, cancellationToken);

if (!result.IsSuccessful)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.Message} at line {error.Location?.StartLine}");
    }
}
```

## Exceptions

### MappingExecutionException

Thrown for fatal mapping execution errors:

```csharp
try
{
    var result = await evaluator.EvaluateAsync(structureMap, context, cancellationToken);
}
catch (MappingExecutionException ex)
{
    Console.WriteLine($"Mapping failed: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Cause: {ex.InnerException.Message}");
    }
}
```

## Expression Types

The StructureMap AST includes the following expression types:

```csharp
// Mapping rule (source → target transform)
public class RuleExpression : Expression { }

// FHIRPath expression
public class FhirPathExpression : Expression { }

// Literal value
public class LiteralExpression : Expression { }

// Identifier reference
public class IdentifierExpression : Expression { }

// Qualified identifier (e.g., "Resource.field")
public class QualifiedIdentifierExpression : Expression { }

// Group invocation
public class GroupInvocationExpression : Expression { }

// Transform function call
public class TransformExpression : Expression { }

// ConceptMap reference
public class ConceptMapCodeMapExpression : Expression { }
```

## FHIRPath Integration

The evaluator integrates with FHIRPath for expression evaluation:

```csharp
var context = new MappingContext(sourceElement, targetElement);

// FHIRPath expressions are evaluated with source element as context
// e.g., "Patient.name.family" extracts family name from Patient
var result = await evaluator.EvaluateAsync(structureMap, context, cancellationToken);
```

## Parsing Note

This package executes pre-compiled StructureMap resources. For parsing FHIR Mapping Language source code into AST, consult the FHIR specification parser documentation.

## Related Documentation

- [FHIR Mapping Language Specification](https://hl7.org/fhir/mapping-language.html)
- [FHIRPath](/docs/core-sdk/fhirpath)
