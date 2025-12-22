---
sidebar_position: 5
title: Validation
description: Three-tier FHIR validation engine
---

# Ignixa.Validation

Three-tier validation system supporting Fast, Spec, and Profile validation levels.

## Installation

```bash
dotnet add package Ignixa.Validation
```

## Quick Start

```csharp
using Ignixa.Validation;

// Create validator
var validator = new FhirValidator(ValidationLevel.Spec);

// Validate a resource
var outcome = await validator.ValidateAsync(sourceNode);

if (!outcome.Success)
{
    foreach (var issue in outcome.Issues)
    {
        Console.WriteLine($"{issue.Severity}: {issue.Diagnostics}");
    }
}
```

## Validation Levels

### Fast

Structural validation only - fastest option:

```csharp
var validator = new FhirValidator(ValidationLevel.Fast);
```

Checks:
- JSON structure validity
- Required element presence
- Array vs single element correctness
- Basic type validity

### Spec

FHIR specification compliance:

```csharp
var validator = new FhirValidator(ValidationLevel.Spec);
```

Checks (includes Fast, plus):
- Cardinality constraints (min/max)
- Reference format validation
- Primitive value domains
- CodeableConcept structure
- Binding strength (required/extensible)

### Profile

Full profile-based validation:

```csharp
var validator = new FhirValidator(ValidationLevel.Profile);
```

Checks (includes Spec, plus):
- StructureDefinition constraints
- Extension validation
- Slice matching
- Terminology bindings (with external lookup)
- FHIRPath invariants

## Validation Options

```csharp
var options = new ValidationOptions
{
    Level = ValidationLevel.Profile,
    
    // Behavior
    ValidateReferences = true,
    ResolveExternalReferences = false,
    
    // Terminology
    ValidateTerminology = true,
    TerminologyServer = "https://tx.fhir.org/r4",
    
    // Performance
    MaxIssues = 100,
    TimeoutSeconds = 30
};

var validator = new FhirValidator(options);
```

## OperationOutcome

Validation returns an OperationOutcome:

```csharp
public class ValidationResult
{
    public bool Success { get; }
    public IReadOnlyList<ValidationIssue> Issues { get; }
    public ISourceNode? OperationOutcome { get; }
}

public class ValidationIssue
{
    public IssueSeverity Severity { get; }
    public IssueType Code { get; }
    public string Diagnostics { get; }
    public string? Location { get; }
    public string? Expression { get; }
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
var outcome = await validator.ValidateAsync(
    sourceNode, 
    profile: "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient"
);
```

### Against Multiple Profiles

```csharp
var profiles = new[] 
{
    "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
    "http://example.org/StructureDefinition/my-patient"
};

var outcome = await validator.ValidateAsync(sourceNode, profiles);
```

### With Profile Resolution

```csharp
var resolver = new PackageProfileResolver(packageManager);
var validator = new FhirValidator(options, resolver);
```

## Custom Validation Rules

### FHIRPath Invariants

```csharp
var customInvariant = new Invariant
{
    Key = "my-rule-1",
    Severity = IssueSeverity.Error,
    Human = "Patient must have either name or identifier",
    Expression = "name.exists() or identifier.exists()"
};

options.CustomInvariants.Add(customInvariant);
```

### Custom Validators

```csharp
public class MyCustomValidator : IValidator
{
    public async Task<ValidationResult> ValidateAsync(
        ISourceNode resource, 
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        
        // Custom validation logic
        if (SomeCondition(resource))
        {
            issues.Add(new ValidationIssue
            {
                Severity = IssueSeverity.Warning,
                Code = IssueType.BusinessRule,
                Diagnostics = "Custom validation message"
            });
        }
        
        return new ValidationResult(issues);
    }
}

// Register custom validator
options.CustomValidators.Add(new MyCustomValidator());
```

## Terminology Validation

### With External Server

```csharp
var options = new ValidationOptions
{
    ValidateTerminology = true,
    TerminologyServer = "https://tx.fhir.org/r4"
};
```

### With Local ValueSets

```csharp
var termService = new LocalTerminologyService();
termService.LoadValueSet(myValueSet);

var validator = new FhirValidator(options, terminologyService: termService);
```

## Batch Validation

### Multiple Resources

```csharp
var results = new List<ValidationResult>();

await Parallel.ForEachAsync(resources, async (resource, ct) =>
{
    var result = await validator.ValidateAsync(resource, ct);
    lock (results)
    {
        results.Add(result);
    }
});
```

### Bundle Validation

```csharp
// Validate entire bundle
var bundleResult = await validator.ValidateAsync(bundle);

// Validate each entry
foreach (var entry in bundle["entry"].Children())
{
    var resource = entry["resource"];
    var result = await validator.ValidateAsync(resource);
}
```

## Performance Considerations

| Level | Relative Speed | Use Case |
|-------|---------------|----------|
| Fast | 1x (baseline) | Bulk ingestion |
| Spec | 2-3x | Standard API |
| Profile | 5-10x | Compliance testing |

### Optimization Tips

1. **Cache StructureDefinitions** when validating against profiles
2. **Use Fast level** for initial ingestion, validate later
3. **Limit MaxIssues** for early termination
4. **Batch validation** with parallelism

## Related Documentation

- [Server Validation](/docs/server/features/validation)
- [Abstractions](/docs/core-sdk/abstractions)
