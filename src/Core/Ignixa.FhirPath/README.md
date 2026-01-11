# Ignixa.FhirPath

FHIRPath expression evaluation engine for FHIR resources. Provides a high-performance implementation of the FHIRPath specification for querying and navigating FHIR data structures.

## Why Use This Package?

- **Standards-compliant**: Implements the FHIRPath specification
- **High-performance**: Visitor pattern architecture with compile-time optimization
- **Compile-time optimizations**: Constant folding, short-circuiting, algebraic simplification
- **Expression caching**: Compiled expressions are cached for repeated use
- **Modern architecture**: Works with `IElement` from Ignixa.Abstractions
- **Extensible**: Add custom functions via attributes and source generators

## Installation

```bash
dotnet add package Ignixa.FhirPath
```

## Quick Start

### Evaluating FHIRPath Expressions

```csharp
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;

// Parse a FHIRPath expression with compile-time optimization
var parser = new FhirPathParser();
var options = new CompilationOptions { Optimize = true };
var expression = parser.Parse("Patient.name.family", options);

// Evaluate against a resource
var evaluator = new FhirPathEvaluator();
IElement patientElement = GetYourPatientElement();

IEnumerable<IElement> results = evaluator.Evaluate(patientElement, expression);

// Extract values
foreach (var result in results)
{
    Console.WriteLine(result.Value); // Prints family names
}
```

### Compile-Time Optimization Examples

The parser can optimize expressions at compile time:

```csharp
var options = new CompilationOptions { Optimize = true };

// Constant folding
parser.Parse("1 + 1", options);              // Optimized to: 2
parser.Parse("'hello' + 'world'", options);  // Optimized to: 'helloworld'

// Short-circuit evaluation
parser.Parse("false and X", options);        // Optimized to: false (X not evaluated)
parser.Parse("true or X", options);          // Optimized to: true (X not evaluated)

// Algebraic simplification
parser.Parse("X + 0", options);              // Optimized to: X
parser.Parse("X * 1", options);              // Optimized to: X
parser.Parse("X and true", options);         // Optimized to: X
```

### Using the Compiled Delegate Mode (Faster)

For better performance on repeated evaluations:

```csharp
using Ignixa.FhirPath.Evaluation;

// Set up compiler with fallback evaluator
var evaluator = new FhirPathEvaluator();
var compiler = new FhirPathDelegateCompiler(evaluator);

// Parse expression
var expression = parser.Parse("telecom.where(system='phone').value");

// Try to compile (falls back to interpreter if not supported)
var compiled = compiler.TryCompile(expression);

if (compiled != null)
{
    // Use compiled delegate (80% faster for common patterns)
    var context = new EvaluationContext();
    var results = compiled(patientElement, context);
}
else
{
    // Fallback to interpreter
    var results = evaluator.Evaluate(patientElement, expression);
}
```

### Common FHIRPath Patterns

```csharp
// Simple path navigation
"Patient.name.family"                    // Get all family names

// Filtering with where()
"telecom.where(system='phone')"          // Get phone telecoms
"name.where(use='official').family"      // Get official family name

// Existence checks
"identifier.exists()"                    // Has any identifiers?
"name.family.exists()"                   // Has family name?

// First/single element
"name.first().family"                    // First family name
"identifier.where(system='SSN').value"   // SSN value

// Boolean expressions
"active = true"                          // Is patient active?
"birthDate < @2000-01-01"               // Born before 2000
```

## Working with Evaluation Context

```csharp
using Ignixa.FhirPath.Evaluation;

var context = new EvaluationContext();

// Set variables for use in expressions
context.SetVariable("minDate", someDate);

// Evaluate expression using context
var expression = parser.Parse("birthDate > %minDate");
var results = evaluator.Evaluate(patientElement, expression, context);
```

## Supported FHIRPath Features

### Compiled Mode (High Performance)
- Simple paths: `name`, `identifier`
- Two-level paths: `name.family`, `identifier.value`
- Where clauses: `telecom.where(system='phone')`
- Common functions: `first()`, `exists()`

### Interpreter Mode (Full Compatibility)
- All FHIRPath 2.0 operators
- All standard functions
- Complex nested expressions
- Type checking and conversions

## Performance Notes

The `FhirPathDelegateCompiler` compiles ~80% of common search parameter patterns to optimized delegates:
- **Simple paths**: 30% of patterns (e.g., `name`)
- **Two-level paths**: 40% of patterns (e.g., `name.family`)
- **Where clauses**: 15% of patterns (e.g., `telecom.where(system='phone')`)
- **Functions**: 10% of patterns (e.g., `name.first()`)

Unsupported expressions automatically fall back to the interpreted mode.

## Advanced Usage

### Custom Function Registration

```csharp
// Extend FhirPathEvaluator with custom functions
public class MyCustomEvaluator : FhirPathEvaluator
{
    protected override IEnumerable<IElement> EvaluateFunctionCall(
        IEnumerable<IElement> focus,
        FunctionCallExpression func,
        EvaluationContext context)
    {
        if (func.FunctionName == "myCustomFunc")
        {
            // Implement custom logic
            return focus.Where(e => /* your logic */);
        }

        return base.EvaluateFunctionCall(focus, func, context);
    }
}
```

## Related Packages

- **Ignixa.Abstractions**: Provides `IElement` and `IType` interfaces
- **Ignixa.Serialization**: Parse JSON/XML to `IElement` trees
- **Ignixa.Search**: Uses FHIRPath for search parameter extraction
- **Ignixa.Validation**: Uses FHIRPath for constraint evaluation

## Specification Compliance

This implementation follows the [FHIRPath N1 specification](http://hl7.org/fhirpath/N1/).

## License

MIT License - see LICENSE file in repository root
