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

// Omit the state and the schema roots one at this element for you
var result = schema.Validate(element, settings);

if (!result.IsValid)
{
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"{issue.Severity}: {issue.Message}");
    }
}
```

## Validation State

`ValidationState` carries the resource a validation is rooted at. That root is what `%resource` and
`%rootResource` resolve to, so every state must have one — there is no public constructor, and
`ValidationState.ForRoot(element)` is the only way to make one. The two-argument `Validate` overload
calls it for you, which is why most callers never name the type.

:::warning A state is rooted at one resource
Never share one `ValidationState` across several resources — `ForRoot` binds `%resource` to the element
you pass it, and nothing re-roots it for you. The two-argument overload avoids this by construction (it
roots a fresh state per call), so this only matters if you implement a custom `IValidationCheck` (below)
that builds or forwards a `ValidationState` itself. Get it wrong and invariants evaluate against the
*wrong* resource silently, and only at `ValidationDepth.Full`, where invariants actually run.
:::

### When to Provide ValidationState

Never, from outside the SDK. `ValidationSchema.Validate(element, settings, state)` — the overload that
takes an explicit `ValidationState` — is `internal`. External callers only ever see the two-argument
overload shown above, which roots a fresh state at the element you pass it; there is no public way to
supply your own state, and so no public way to mis-root one.

The internal overload exists so validation of a nested element can carry its enclosing validation's scope
down, and different checks need different disciplines: `ContainedResourceCheck` re-roots — `%resource`
moves to the contained resource while `%rootResource` stays on the container — while
`NestedComplexTypeCheck` and `ChoiceVariantNestedCheck` deliberately do not re-root, because a nested
datatype is not a resource and `%resource` must keep pointing at the one enclosing it. An external caller
has no way to know which discipline a given case wants, which is why the overload isn't exposed rather
than documented with a warning.

To validate many resources, root a fresh state per resource by calling the two-argument overload once per
resource — it does this for you:

```csharp
foreach (var resource in resources)
{
    var element = resource.ToElement(schemaProvider);
    var result = schema.Validate(element, settings);
}
```

:::note No run-level caching today
`ValidationState.Global` exposes a `Cache` dictionary and a `ResourcesValidated` counter, but nothing
in the SDK currently reads or writes either — sharing a `GlobalState` across validations buys you
nothing at present. Each constraint parses its FHIRPath expression once per check instance and the
checks live on the schema, so reuse comes from caching the *schema* (see `CachedSchemaResolver` above),
not from reusing a state.
:::

### State Levels

ValidationState has three context levels:

| Level | Purpose | Example Use |
|-------|---------|-------------|
| `Global` | Shared across entire validation run | Cache, resource counter |
| `Instance` | Current resource being validated | Resource type, ID |
| `Location` | Current element path | FHIRPath location, definition path |

```csharp
// Access state information in custom checks
public class CustomCheck : IValidationCheck
{
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Access global cache
        if (!state.Global.Cache.TryGetValue("myKey", out var cached))
        {
            cached = ExpensiveComputation();
            state.Global.Cache["myKey"] = cached;
        }

        // Access current resource info
        var resourceType = state.Instance.ResourceType;
        var resourceId = state.Instance.ResourceId;

        // Access current location
        var path = state.Location.InstancePath;

        return ValidationResult.Success();
    }
}
```

## Validation Depths

### Depth policy

An element is validated according to its **type**, not according to how it is declared. A complex datatype is
validated through its own schema at Spec and above, whether it is a plain element (`Observation.referenceRange.low`), a
complex choice variant (`Observation.valueQuantity`) or an extension's value (`extension.valueQuantity`). A
Reference, CodeableConcept or Coding gets the same dedicated checks (reference format and target type, coding
structure, bindings) in all three positions, and its inside is validated through its own schema like any other
datatype (unknown properties, JSON primitive types, cardinality, and at Full its invariants). A resource a Bundle or
Parameters carries is validated against its own type and `meta.profile`, and a contained resource against its own
type. What a tier
adds is a kind of rule, never more of the instance:

- **Spec** checks what the core definitions and the profiles' snapshots state element by element: structure,
  cardinality, primitive types, fixed values and patterns, required bindings and reference target types.
- **Full** adds the rules that need evaluation beyond one element: FHIRPath invariants, slicing, extensible
  bindings, and extensions against their loaded definitions, including their contexts and modifier placement.

A check that applies but cannot conclude is reported, never passed: a reference whose target type cannot be told,
an extension whose definition is not loaded, a binding the terminology service cannot decide, a constraint the
FHIRPath engine cannot evaluate. Each is a non-failing `Information` or `Warning` issue, so a consumer can tell
"checked and conformant" from "not checked".

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
- Type checking, including every nested datatype: backbone elements, complex datatypes, complex choice variants
  (`valueQuantity`, `valueAttachment`) and extension values
- Reference format validation
- Reference target types (`ElementDefinition.type.targetProfile`, the core definition's and each profile's); a
  `Reference.type` that disagrees with the reference is `reference-type-mismatch`
- Choice element validation
- Required terminology bindings; a CodeableConcept with text only under a required binding is an error (it carries
  no coding from the ValueSet)
- Fixed value constraints
- Pattern constraints
- Coding.system must be an absolute URI (one starting with a scheme, so `/foo` is relative), wherever the Coding appears (a choice variant such as `valueCodeableConcept`
  and an extension value included; before these were validated, a relative system there was accepted)
- Every extension, wherever it appears (including a primitive's `_field` shadow), as the Extension datatype: `url`
  present, one `value[x]` of an allowed type, its value's datatype rules; a relative `url` is an error
  (`extension-url-relative`) except on a complex extension's sub-extension
- The insides of CodeableConcept, Coding and Reference, as their datatypes
- Each resource a Bundle or Parameters carries (`Bundle.entry.resource`, `Bundle.entry.response.outcome`,
  `Parameters.parameter.resource` and its parts'), against its own type and `meta.profile`, with issues rooted at the
  entry (`Bundle.entry[3].resource.status`); an entry with no known `resourceType` is `entry-invalid-resourcetype`.
  Within an entry `%resource` and `%rootResource` are the entry, while its references to sibling entries resolve
  against the Bundle or Parameters that holds it: the innermost one, so an entry of a Bundle nested in another sees only
  its own Bundle's entries

### Compatibility

Microsoft FHIR Server compatibility mode for migration scenarios:

```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
```

Checks (similar to Spec, but more lenient):
- All Minimal checks
- Cardinality constraints (min/max)
- Type checking, including nested datatypes, complex choice variants, extension values, the insides of
  CodeableConcept, Coding and Reference, and Bundle and Parameters entries
- Reference format validation
- Choice element validation
- Required terminology bindings
- Fixed value constraints
- Pattern constraints
- **Accepts relative URIs in Coding.system** (e.g., `"internal-tags"`) - relaxed from Spec
- **Accepts a text-only CodeableConcept under a required binding** - relaxed from Spec
- **Does not check reference target types** - relaxed from Spec
- **Accepts a relative url on a resource-level extension** - relaxed from Spec

Use when:
- Migrating from Microsoft FHIR Server (Firely SDK validation)
- Running Microsoft's FHIR Server E2E test suite
- Gradually improving data quality over time

### Full

Full profile-based validation:

```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Full };
```

Checks (includes Spec, plus):
- FHIRPath invariants
- Slice matching
- Extensions against their definitions, when the schema the resolver builds from has them loaded (a package layered
  over the core schema): value types narrowed, bindings, cardinalities, a complex extension's sub-extension slices
- Where an extension is used: its definition's contexts (`extension-context`) and its modifier status
  (`extension-modifier-mismatch`: a modifier extension under `extension`, or a non-modifier under `modifierExtension`)
- Extensible terminology bindings
- Display name validation
- Advanced profile constraints

At Full an extension whose definition is not loaded is validated as the Extension datatype and reported as
`extension-definition-unchecked` (Information), as the HL7 validator reports an unknown extension. So is a
sub-extension of a loaded complex extension: the rules a definition gives its sub-extension slices (their value types
and bindings) are not modelled, only the slices' cardinalities.

An extension's use is checked when the schema provider also implements the optional `IExtensionDefinitionInfo` lookup
(`ProfileLayeredSchemaProvider` does), which answers a definition's contexts and `isModifier` by url. An `element`
context matches the parent's type name (`HumanName`, or a specialization of it such as `Age` for `Quantity`), its
element path (`Patient.name`), `Element`, or for a resource-level extension `Resource` or `DomainResource`; an
`extension` context matches the url of the extension it is nested in. A definition allowing an extension only in
`fhirpath` contexts, which are not evaluated, and a schema without the lookup, make it `extension-context-unchecked`
(Information).

### dateTime timezone leniency

Below Full, a `dateTime` may omit its timezone after a time (`2023-01-01T12:00:00`) or carry one after a date
(`2021-10-13+02:00`); Full applies the strict R4 grammar, which requires a timezone whenever a time is present and
allows none after a date. This is a deliberate compatibility policy (bug #210-4: real-world data carries these forms,
and the R5 regular expression admits them, although R5's prose still requires a timezone whenever a time is present),
and it applies to every dateTime, a plain element and a choice variant alike. Every
other dateTime rule (component ranges, calendar validity) applies at every depth.

### Reference target types

A reference's target type is told from, in order: a contained resource its `#id` names, or for `#` alone the
containing resource (from a contained resource) or the resource itself (at its root); the host's
`ValidationSettings.ReferenceTypeResolver`; an entry of the enclosing Bundle or Parameters; a relative `Type/id`; a
conditional reference `Type?query` (the type before `?`); an absolute URL ending in `Type/id` (an id of at most 64
characters, not a page such as `.html`). In R4B and R5 the `reference` of a CodeableReference is checked against the
element's target profiles in the same way. A reference whose type
cannot be told (an unresolved `urn:uuid:`, an identifier only), one with only a display, and one at an element whose
target profile is not loaded are reported as `reference-target-unchecked` (Information), never passed. A host that
validates resources extracted from a Bundle can resolve their `urn:uuid:` references by setting a resolver on that
resource's settings:

```csharp
var settings = new ValidationSettings
{
    Depth = ValidationDepth.Spec,
    ReferenceTypeResolver = new MyBundleUrnResolver(bundleEntriesByFullUrl),
};
```

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
    public string? TemplateId { get; }
    public IReadOnlyList<string> TemplateArguments { get; }
}
```

### Message templates

`Message` interpolates instance and definition content (a value, a property name, a constraint's `human` text). An
issue rendered from one of the validator's templates also carries the template's stable `TemplateId` and the values it
was filled with, in placeholder order, as `TemplateArguments`, so a consumer can render its own text, or redact, without
parsing `Message`. `IssueTemplates` lists every template (`IssueTemplates.All`); `IssueTemplate.Render(arguments)`
reproduces `Message`. An issue built from free text (a custom check) has a null `TemplateId`. Treat every argument as
untrusted content. Issues compare by value, their template arguments included, so two identical issues are equal and
hash alike (a `HashSet<ValidationIssue>` deduplicates them).

```csharp
foreach (var issue in result.Issues)
{
    var text = issue.TemplateId switch
    {
        "binding-required-text-only" => $"Text-only concept; a coding from {issue.TemplateArguments[0]} is required",
        null => issue.Code,
        _ => issue.TemplateId,
    };
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
var result = schema.Validate(element, settings);
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
        var result = schema.Validate(element, settings);
        results.Add(result);
    }
});
```

### Bundle Validation

Validating a Bundle (or Parameters) resource validates each resource it carries, at Spec and above, so there is no
need to validate the entries one by one. Each entry is checked against its own type and `meta.profile` (through the
profile-aware resolver the schema was built with), and its issues are rooted at the entry:

```csharp
var bundleSchema = schemaResolver.GetSchema(
    "http://hl7.org/fhir/StructureDefinition/Bundle");

var bundleElement = JsonNodeSourceNode.Create(bundle.MutableNode)
    .ToElement(schemaProvider);

var bundleResult = bundleSchema.Validate(bundleElement, settings);

// e.g. "Bundle.entry[1].resource.status": an error in the second entry
var entryIssues = bundleResult.Issues.Where(i => i.Path.StartsWith("Bundle.entry[", StringComparison.Ordinal));
```

A host that validates entries separately instead (for example while processing a transaction) can still resolve their
`urn:uuid:` references with a `ReferenceTypeResolver`, as above.

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

## Migration from Microsoft FHIR Server

The `Compatibility` validation depth is specifically designed for migration scenarios from Microsoft FHIR Server (which uses Firely SDK validation).

### Validation Depth Comparison

| Validation Depth | Coding.system Absolute URI | Terminology Bindings | Reference Targets | Extensions | FHIRPath Invariants | Use Case |
|-----------------|---------------------------|---------------------|-------------------|------------|---------------------|----------|
| `Minimal` | Not checked | Not checked | Not checked | Not checked | Not checked | Bulk ingestion, high throughput |
| `Compatibility` | **Not enforced** (accepts relative URIs) | Required only (text-only accepted) | Not checked | As a datatype | Not checked | **Microsoft FHIR Server migration** |
| `Spec` | **Enforced** (absolute URI required) | Required only | Checked | As a datatype | Not checked | Standard API operations |
| `Full` | **Enforced** (absolute URI required) | All bindings | Checked | Against loaded definitions | Checked | Compliance testing, IG validation |

### Key Differences: Compatibility vs Spec

The `Compatibility` depth is more lenient than `Spec` in these areas:

**Coding.system URIs:**
- `Compatibility`: Accepts relative or local references (e.g., `"internal-tags"`, `"local-system"`)
- `Spec`: Requires absolute URIs (e.g., `"http://terminology.hl7.org/CodeSystem/v3-ActCode"`)

This is common in `meta.tag` fields used for internal categorization.

### Example: Compatibility Mode

```csharp
var settings = new ValidationSettings
{
    Depth = ValidationDepth.Compatibility
};

var result = schema.Validate(element, settings);
// Accepts resources with relative URIs in Coding.system
```

**Resource that passes at Compatibility but fails at Spec:**

```json
{
  "resourceType": "Medication",
  "meta": {
    "tag": [{
      "system": "internal-tags",
      "code": "test-medication"
    }]
  },
  "code": {
    "coding": [{
      "system": "http://www.nlm.nih.gov/research/umls/rxnorm",
      "code": "1234",
      "display": "Test Med"
    }]
  }
}
```

- ✅ Accepted at `Compatibility` depth
- ❌ Rejected at `Spec` and `Full` depths (due to `"system": "internal-tags"`)

### Migration Path

Follow this path to gradually improve data quality:

1. **Start with Compatibility**: Validate existing data with `ValidationDepth.Compatibility`
   ```csharp
   var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
   ```

2. **Identify Issues**: Review resources that would fail at `Spec` depth
   ```csharp
   // Test against Spec to find issues
   var specSettings = new ValidationSettings { Depth = ValidationDepth.Spec };
   var specResult = schema.Validate(element, specSettings);
   // Log issues for data cleanup
   ```

3. **Fix Data Quality**: Gradually update relative URIs to absolute URIs
   - Replace `"internal-tags"` with `"http://your-organization.com/fhir/CodeSystem/internal-tags"`
   - Document your internal code systems with CodeSystem resources

4. **Upgrade to Spec**: Once data is compliant, switch to `ValidationDepth.Spec`
   ```csharp
   var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
   ```

5. **Full Validation**: Eventually enable `ValidationDepth.Full` for complete FHIR compliance
   ```csharp
   var settings = new ValidationSettings { Depth = ValidationDepth.Full };
   ```

## Usage Guidelines

| Depth | Use Case |
|-------|----------|
| Minimal | Bulk ingestion, high throughput scenarios |
| Compatibility | **Microsoft FHIR Server migration**, E2E test compatibility |
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
