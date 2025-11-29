# Ignixa.FhirMappingLanguage

A .NET implementation of the FHIR Mapping Language (FML). This library provides parsing, compilation, and execution of FHIR mapping expressions to transform FHIR resources.

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
using Ignixa.FhirMappingLanguage.Parser;

// Parse a mapping
var parser = new MappingParser();
var mapping = @"
map 'http://example.org/fhir/StructureMap/PatientTransform' = 'PatientTransform'

uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source
uses 'http://hl7.org/fhir/StructureDefinition/Bundle' alias Bundle as target

group PatientToBundle(source src : Patient, target bundle : Bundle) {
  src.name as vn -> bundle.entry as entry;
}
";

var map = parser.Parse(mapping);

// Create evaluator with security options
var options = MappingEvaluatorOptions.Default; // Recommended security settings
var evaluator = new MappingEvaluator(options);

// Create evaluation context
var context = new MappingContext();
context.SetSource("src", sourcePatient);
context.SetTarget("bundle", targetBundle);

// Execute the mapping
evaluator.Execute(map, context);
```

### Compiled Mapping

```csharp
// Compile once, execute many times
var parser = new MappingParser();
var compiled = parser.Compile(mappingText);

// Set sources and targets
compiled.Context.SetSource("src", sourcePatient);
compiled.Context.SetTarget("bundle", targetBundle);

// Execute the first (default) group
compiled.Execute();

// Or execute a specific group by name
compiled.ExecuteGroup("PatientToBundle");
```

### Working with FHIRPath Expressions

**FHIRPath support is built-in and enabled by default.** The evaluator automatically uses `Ignixa.FhirPath` for all embedded FHIRPath expressions like `where`, `check`, and `log` clauses.

```csharp
// FHIRPath expressions work out of the box - no configuration needed!
var mapping = @"
map 'http://example.org/map' = 'Example'
group Transform(source src : Patient, target tgt : Bundle) {
  src.name where (use = 'official') -> tgt.entry;
  src.identifier check (system.exists()) -> tgt.id;
}
";

var parser = new MappingParser();
var map = parser.Parse(mapping);
var evaluator = new MappingEvaluator(); // FHIRPath enabled by default
evaluator.Execute(map, context);
```

**Custom FHIRPath Evaluator** (optional - only if you need custom behavior):

```csharp
// Disable built-in integration and provide your own
var evaluator = new MappingEvaluator(enableFhirPath: false);

context.FhirPathEvaluator = (expression, element) =>
{
    // Your custom FHIRPath evaluation logic
    return MyCustomFhirPathEvaluator(expression, element);
};
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

## Security and Error Handling

### Security Configuration

`MappingEvaluatorOptions` provides comprehensive security controls to prevent resource exhaustion attacks:

```csharp
var options = new MappingEvaluatorOptions
{
    // Resource limits
    MaxRecursionDepth = 50,              // Prevent infinite recursion
    MaxElementsCreated = 100_000,        // Prevent memory exhaustion
    MaxMapSizeBytes = 50_000_000,        // 50 MB max map size
    MaxInputResourceSizeBytes = 10_000_000, // 10 MB max input size

    // Timeouts
    TransformTimeout = TimeSpan.FromSeconds(30),
    FhirPathTimeout = TimeSpan.FromSeconds(5),

    // Import security
    AllowFileSystemImports = false,      // Disable file:// imports by default
    AllowedImportDomains = { "hl7.org", "fhir.org" },

    // ConceptMap security
    AllowedConceptMapTargetSystems = { "http://snomed.info/sct", "http://loinc.org" },
    MaxCodeLength = 100,

    // Error handling
    ErrorMode = ErrorMode.Strict,        // Fail fast on errors
    MaxErrorsCollected = 100             // Limit errors in Lenient mode
};

var evaluator = new MappingEvaluator(options);
```

**Default Configuration**: `MappingEvaluatorOptions.Default` provides recommended security settings suitable for production use.

### Error Handling Modes

The library supports two error handling modes:

#### Strict Mode (Default)

Throws `MappingExecutionException` on the first error:

```csharp
var context = new MappingContext
{
    ErrorMode = ErrorMode.Strict
};

try
{
    evaluator.Execute(map, context);
}
catch (MappingExecutionException ex)
{
    Console.WriteLine($"Mapping failed: {ex.Message}");
}
```

#### Lenient Mode

Collects all errors and continues execution where possible:

```csharp
var options = new MappingEvaluatorOptions
{
    ErrorMode = ErrorMode.Lenient,
    MaxErrorsCollected = 100  // Throw if too many errors
};

var context = new MappingContext
{
    ErrorMode = ErrorMode.Lenient
};

evaluator.Execute(map, context);

// Check for errors after execution
if (context.Errors.Any())
{
    foreach (var error in context.Errors)
    {
        Console.WriteLine($"Rule '{error.RuleName}': {error.Message}");
        Console.WriteLine($"Location: {error.Location}");
        Console.WriteLine($"Path: {error.ElementPath}");

        if (error.AvailableElements != null)
        {
            Console.WriteLine($"Available elements: {string.Join(", ", error.AvailableElements)}");
        }
    }
}
```

### Enhanced Error Messages

Error messages include rich contextual information:

- **RuleName**: Name of the failing rule
- **GroupName**: Name of the group containing the rule
- **RuleIndex**: Index of the rule within the group
- **ElementPath**: Path to the element that caused the error (e.g., `src.name.family`)
- **AvailableElements**: List of valid child elements when accessing non-existent elements

Example error output:
```
Rule 'copyName' in group 'PatientTransform' at index 0
Source element 'middleName' not found (cardinality 1..1 requires at least 1)
Path: Patient.name.middleName
Available elements: family, given, prefix, suffix, use
Location: StructureMap.group[PatientTransform].rule[0]
```

### Cardinality Constraints

Source expressions support cardinality constraints to validate element counts:

```csharp
// Require exactly one element
src.identifier : Identifier 1..1 -> tgt.id;

// Require at least one element
src.name : HumanName 1..* -> tgt.entry;

// Allow zero or one element
src.telecom : ContactPoint 0..1 -> tgt.contact;

// Allow any number of elements (default)
src.address : Address 0..* -> tgt.addresses;
```

When cardinality constraints are violated, the evaluator:
- **Strict mode**: Throws `MappingExecutionException` immediately
- **Lenient mode**: Collects error and continues with next rule

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
context [as variable] [: type] [cardinality] [where (condition)] [check (condition)] [log (message)] [default expression]
```

Where `cardinality` is optional and follows the pattern `min..max` (e.g., `1..1`, `0..*`, `1..*`)

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
- **Ignixa.Abstractions**: Core abstractions
- **Ignixa.FhirPath**: FHIRPath evaluation for embedded expressions
- **Ignixa.Serialization**: FHIR Serialization and Deserialization

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

## License

MIT License - see LICENSE file in repository root
