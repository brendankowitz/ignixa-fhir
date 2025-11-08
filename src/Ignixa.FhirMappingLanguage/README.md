# Ignixa.FhirMappingLanguage

A .NET implementation of the FHIR Mapping Language (FML) based on the FHIR StructureMap specification. This library provides parsing, compilation, and execution of FHIR mapping expressions to transform FHIR resources.

## Architecture

This library follows the same architecture patterns as `Ignixa.FhirPath`:

- **Lexer**: Tokenizes mapping language text using Superpower
- **Parser**: Converts token streams into an abstract syntax tree (AST)
- **Expression Tree**: Strongly-typed expression classes representing mapping constructs
- **Evaluator**: Visitor pattern for executing mappings

## Features

### Supported Language Constructs

- **Map declarations**: Top-level map structure with URL and identifier
- **Uses declarations**: Declare source and target structure definitions
- **Imports**: Import other mapping definitions
- **Groups**: Logical grouping of transformation rules with parameters
- **Rules**: Transformation rules with sources, targets, and dependencies
- **Sources**: Input data patterns with conditions, checks, and logging
- **Targets**: Output data patterns with transforms and list modes
- **Transforms**: Built-in and custom transformation functions
- **Embedded FHIRPath**: Conditions and expressions using FHIRPath syntax

### Lexer Features

- **Keywords**: map, uses, as, alias, imports, group, extends, default, where, check, log, then, source, target, etc.
- **Operators**: `=`, `->`, `::`, `.`, `,`, `;`
- **Literals**: Strings, numbers, booleans, URLs
- **Comments**: Line comments (`//`) and block comments (`/* */`)
- **Trivia mode**: Preserve whitespace and comments for round-tripping

## Usage

### Basic Example

```csharp
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;

// Parse a mapping
var compiler = new MappingCompiler();
var mapping = @"
map 'http://example.org/fhir/StructureMap/PatientTransform' = 'PatientTransform'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src.name as vn -> bundle.entry as entry;
}
";

var map = compiler.Parse(mapping);

// Create evaluation context
var context = new MappingContext();
context.SetSource("src", sourcePatient);
context.SetTarget("bundle", targetBundle);

// Execute the mapping
var evaluator = compiler.CreateEvaluator();
evaluator.Execute(map, context);
```

### Compiled Mapping

```csharp
// Compile once, execute many times
var compiled = compiler.Compile(mappingText);

// Set sources and targets
compiled.Context.SetSource("src", sourcePatient);
compiled.Context.SetTarget("bundle", targetBundle);

// Execute
compiled.Execute();

// Or execute a specific group
compiled.ExecuteGroup("PatientToBundle");
```

### Working with FHIRPath Expressions

```csharp
// Configure FHIRPath evaluator
context.FhirPathEvaluator = (expression, element) =>
{
    var fhirPathCompiler = new FhirPathCompiler();
    var parsed = fhirPathCompiler.Parse(expression);
    var evaluator = new FhirPathEvaluator();
    return evaluator.Evaluate(element, parsed);
};

// Now embedded FHIRPath expressions will be evaluated
// Example: src.name where name.exists() -> bundle.entry
```

### Custom Transform Functions

```csharp
// Register custom transform functions
context.TransformResolver = (functionName, arguments) =>
{
    return functionName switch
    {
        "create" => CreateResource(arguments[0].ToString()!),
        "translate" => TranslateCode(arguments),
        "dateOp" => ParseDate(arguments[0].ToString()!),
        _ => throw new NotSupportedException($"Transform '{functionName}' not supported")
    };
};
```

## FHIR Mapping Language Syntax

### Map Structure

```
map "url" = "identifier"
[uses "structure-url" alias Name as source|target|queried|produced]*
[imports "map-url"]*
[group definitions]*
```

### Group Definition

```
group Name(source src : Type, target tgt : Type) [extends OtherGroup] {
  [rules]*
}
```

### Rule Syntax

```
[name:] source [, source]* [-> target [, target]*] [then { [rule]* }];
```

### Source Syntax

```
context [as variable] [: type] [where (condition)] [check (condition)] [log (message)]
```

### Target Syntax

```
[context] [as variable] [= transform] [list-mode]
```

### Transform Functions

Common transform functions include:
- `create(type)` - Create a new resource of the specified type
- `translate(source, map, code)` - Translate codes using a ConceptMap
- `truncate(string, length)` - Truncate a string
- `dateOp(value)` - Parse and convert dates

## Implementation Notes

### Similar to FhirPath Library

This library follows the exact same architectural patterns as `Ignixa.FhirPath`:

1. **Lexer/Tokenizer**: Uses Superpower for high-performance tokenization
2. **Expression Tree**: Strongly-typed AST classes for type-safe manipulation
3. **Parser Grammar**: Declarative parser using Superpower's parser combinators
4. **Evaluator**: Visitor pattern for executing the AST

### Dependencies

- **Superpower**: Parser combinator library for lexing and parsing
- **Ignixa.Domain**: Core domain abstractions
- **Ignixa.FhirPath**: FHIRPath evaluation for embedded expressions

## Testing

The library includes comprehensive unit tests in `Ignixa.FhirMappingLanguage.Tests`:

```bash
dotnet test test/Ignixa.FhirMappingLanguage.Tests/
```

## Specification Compliance

This implementation is based on the FHIR Mapping Language specification:
- [FHIR R5 Mapping Language](https://hl7.org/fhir/mapping-language.html)
- [FHIR R6 Mapping Language (Development)](https://build.fhir.org/mapping-language.html)
- [StructureMap Resource](https://hl7.org/fhir/structuremap.html)

## Future Enhancements

- Complete implementation of all standard transform functions
- Support for type inheritance and polymorphism
- Optimization for large-scale transformations
- Debugging and tracing capabilities
- Integration with FHIR validation engine

## License

Copyright (c) 2025, Ignixa Contributors
Licensed under the BSD 3-Clause license.
