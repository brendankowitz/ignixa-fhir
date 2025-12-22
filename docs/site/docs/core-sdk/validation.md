---
sidebar_position: 5
title: Validation
description: Three-tier FHIR validation engine
---

# Ignixa.Validation

Three-tier validation system supporting Minimal, Spec, and Full validation depths.

## Installation

```bash
dotnet add package Ignixa.Validation
```

## Quick Start

```csharp
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Ignixa.Specification.Generated;
using Ignixa.Serialization.SourceNodes;

// Get schema provider for your FHIR version
var schemaProvider = new R4CoreSchemaProvider();

// Create schema resolver
var schemaResolver = new StructureDefinitionSchemaResolver(schemaProvider);
var cachedResolver = new CachedValidationSchemaResolver(schemaResolver);

// Get validation schema - accepts type name or canonical URL
var schema = cachedResolver.GetSchema("Patient");
// Or: cachedResolver.GetSchema("http://hl7.org/fhir/StructureDefinition/Patient");

// Convert resource to IElement
var sourceNode = JsonNodeSourceNode.Create(resourceJsonNode);
var element = sourceNode.ToElement(schemaProvider);

// Validate the resource
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
var state = new ValidationState();
var result = schema.Validate(element, settings, state);

if (!result.IsValid)
{
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"{issue.Severity}: {issue.Message}");
    }
}
```

## Validation Depths

### Minimal

Structural validation only - fastest option:

```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Minimal };
```

Checks:
- JSON structure validity
- ID format validation
- Narrative structure
- Basic resource type validation

### Spec

FHIR specification compliance:

```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
```

Checks (includes Minimal, plus):
- Cardinality constraints (min/max)
- Type checking
- Reference format validation
- Choice element validation
- Required terminology bindings
- Fixed value constraints
- Pattern constraints

### Full

Full profile-based validation:

```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Full };
```

Checks (includes Spec, plus):
- FHIRPath invariants
- Slice matching
- Extension validation
- Extensible terminology bindings
- Display name validation
- Advanced profile constraints

## Validation Settings

```csharp
var settings = new ValidationSettings
{
    // Validation depth (Minimal/Spec/Full)
    Depth = ValidationDepth.Full,

    // Skip terminology validation if needed
    SkipTerminologyValidation = false,

    // How to handle terminology service failures
    TerminologyFailureMode = TerminologyFailureMode.Warning,

    // Optional terminology service for code validation
    TerminologyService = new InMemoryTerminologyService()
};
```

## Validation Results

Validation returns a `ValidationResult`:

```csharp
public sealed record ValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public bool HasErrors { get; }
    public bool HasWarnings { get; }

    // Convert to FHIR OperationOutcome
    public OperationOutcomeJsonNode ToOperationOutcome();
}

public sealed record ValidationIssue
{
    public IssueSeverity Severity { get; }
    public string Code { get; }
    public string Path { get; }
    public string Message { get; }
    public CodeableConceptJsonNode? Details { get; }
}
```

### Severity Levels

| Severity | Description | Valid Resource? |
|----------|-------------|-----------------|
| `Fatal` | Cannot process | ❌ |
| `Error` | FHIR violation | ❌ |
| `Warning` | Best practice | ✅ |
| `Information` | Advisory | ✅ |

## Profile Validation

### Against Specific Profile

```csharp
// Get schema for a specific profile
var profileUrl = "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient";
var schema = cachedResolver.GetSchema(profileUrl);

// Validate against the profile
var settings = new ValidationSettings { Depth = ValidationDepth.Full };
var state = new ValidationState();
var result = schema.Validate(element, settings, state);
```

### Using Custom Schema Resolvers

```csharp
// Create a custom resolver that combines multiple sources
public class CustomSchemaResolver : IValidationSchemaResolver
{
    private readonly IValidationSchemaResolver _baseResolver;
    private readonly Dictionary<string, ValidationSchema> _customSchemas = new();

    public CustomSchemaResolver(IValidationSchemaResolver baseResolver)
    {
        _baseResolver = baseResolver;
    }

    public void AddCustomSchema(string canonicalUrl, ValidationSchema schema)
    {
        _customSchemas[canonicalUrl] = schema;
    }

    public ValidationSchema? GetSchema(string canonicalUrl)
    {
        return _customSchemas.TryGetValue(canonicalUrl, out var schema)
            ? schema
            : _baseResolver.GetSchema(canonicalUrl);
    }
}
```

## Custom Validation Checks

### Implementing Custom Checks

```csharp
public class BusinessRuleCheck : IValidationCheck
{
    public ValidationResult Validate(
        IElement element,
        ValidationSettings settings,
        ValidationState state)
    {
        var issues = new List<ValidationIssue>();

        // Example: Require Patient to have either name or identifier
        if (element.Name == "Patient")
        {
            var hasName = element.Children("name").Any();
            var hasIdentifier = element.Children("identifier").Any();

            if (!hasName && !hasIdentifier)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "business-rule-1",
                    "Patient",
                    "Patient must have either name or identifier"));
            }
        }

        return issues.Any()
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }
}
```

### Adding Checks to Schema

```csharp
// Build custom schema with additional checks
var baseSchema = cachedResolver.GetSchema(canonicalUrl);
var customChecks = new List<IValidationCheck>
{
    new BusinessRuleCheck(),
    new OrganizationPolicyCheck()
};

// Combine base checks with custom checks
var allChecks = baseSchema.Checks.Concat(customChecks).ToList();

var customSchema = new ValidationSchema(
    baseSchema.CanonicalUrl,
    baseSchema.ResourceType,
    universalChecks: allChecks.Where(c => /* categorize */).ToList(),
    specChecks: new List<IValidationCheck>(),
    profileChecks: customChecks);
```

## Terminology Validation

### Using InMemoryTerminologyService

```csharp
using Ignixa.Validation.Services;

// Create an in-memory terminology service
var termService = new InMemoryTerminologyService();

// Configure validation settings
var settings = new ValidationSettings
{
    Depth = ValidationDepth.Full,
    SkipTerminologyValidation = false,
    TerminologyService = termService,
    TerminologyFailureMode = TerminologyFailureMode.Warning
};
```

### Implementing Custom Terminology Service

```csharp
public class CustomTerminologyService : ITerminologyService
{
    public async Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        // Implement code validation logic
        // e.g., check against external terminology server
        var isValid = await CheckCodeAgainstServer(system, code, valueSetUrl);

        return new TerminologyValidationResult(
            IsValid: isValid,
            Severity: isValid ? IssueSeverity.Information : IssueSeverity.Error,
            Message: isValid ? null : $"Code {system}#{code} not found in {valueSetUrl}");
    }

    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        // Validate based on binding strength
        // Required → Error, Extensible → Warning, Preferred → Info
        var validationResult = await ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

        return new BindingValidationResult(
            IsValid: validationResult.IsValid,
            Strength: strength,
            Severity: DetermineSeverity(validationResult.IsValid, strength),
            Message: validationResult.Message,
            SuggestedDisplay: null);
    }

    // Implement other ITerminologyService methods (LookupCodeAsync, ExpandValueSetAsync, etc.)...
}
```

## Batch Validation

### Multiple Resources

```csharp
// Validate multiple resources in parallel
var schemaProvider = new R4CoreSchemaProvider();
var schemaResolver = new CachedValidationSchemaResolver(
    new StructureDefinitionSchemaResolver(schemaProvider));

var results = new ConcurrentBag<ValidationResult>();
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };

await Parallel.ForEachAsync(resources, async (resourceNode, ct) =>
{
    var sourceNode = JsonNodeSourceNode.Create(resourceNode.MutableNode);
    var element = sourceNode.ToElement(schemaProvider);
    var resourceType = resourceNode.ResourceType;

    var schema = schemaResolver.GetSchema(
        $"http://hl7.org/fhir/StructureDefinition/{resourceType}");

    if (schema != null)
    {
        var state = new ValidationState();
        var result = schema.Validate(element, settings, state);
        results.Add(result);
    }
});
```

### Bundle Validation

```csharp
// Validate entire bundle as a resource
var bundleSchema = schemaResolver.GetSchema(
    "http://hl7.org/fhir/StructureDefinition/Bundle");

var bundleElement = JsonNodeSourceNode.Create(bundle.MutableNode)
    .ToElement(schemaProvider);

var bundleResult = bundleSchema.Validate(bundleElement, settings, new ValidationState());

// Validate each entry resource individually
if (bundle.Entry != null)
{
    foreach (var entry in bundle.Entry)
    {
        if (entry.Resource != null)
        {
            var entryElement = JsonNodeSourceNode.Create(entry.Resource.MutableNode)
                .ToElement(schemaProvider);
            var entrySchema = schemaResolver.GetSchema(
                $"http://hl7.org/fhir/StructureDefinition/{entry.Resource.ResourceType}");
            var entryResult = entrySchema?.Validate(entryElement, settings, new ValidationState());
        }
    }
}
```

## CLI Tool

The `ignixa-validator` tool validates FHIR resources from the command line.

### Installation

```bash
dotnet tool install --global Ignixa.Validation.Cli
```

### Usage

```bash
# Validate a file
ignixa-validator r4 --input patient.json --console

# Validate inline JSON
ignixa-validator r4 --json '{"resourceType":"Patient","id":"123"}' --console

# Output OperationOutcome to file
ignixa-validator r4 --input patient.json --out result.json

# Use different FHIR versions
ignixa-validator r5 --input patient.json --console
ignixa-validator stu3 --input patient.json --console
```

### Options

| Option | Description |
|--------|-------------|
| `--input <file>` | Path to JSON file to validate |
| `--json <string>` | Inline JSON string to validate |
| `--out <file>` | Output file for OperationOutcome JSON |
| `--console` | Display formatted results in console |

## Usage Guidelines

| Depth | Use Case |
|-------|----------|
| Minimal | Bulk ingestion, high throughput scenarios |
| Spec | Standard API operations, general-purpose validation |
| Full | Compliance testing, IG validation, profile conformance |

### Optimization Tips

1. **Use CachedValidationSchemaResolver** - Caches compiled schemas to avoid rebuilding checks
2. **Choose appropriate depth** - Use Minimal for bulk ingestion, Full only when needed
3. **Reuse ValidationSettings** - Create once and reuse across validations
4. **Parallel validation** - Validate multiple resources concurrently (schemas are thread-safe)
5. **Skip terminology when possible** - Set `SkipTerminologyValidation = true` for performance

## Related Documentation

- [Server Validation](/docs/server/features/validation)
- [Abstractions](/docs/core-sdk/abstractions)
