---
sidebar_position: 4
title: FHIRPath
description: High-performance FHIRPath expression engine with visitor pattern architecture
---

# Ignixa.FhirPath

A high-performance FHIRPath implementation with visitor pattern architecture, compile-time optimization, and expression caching, implementing the [FHIRPath N1 (Normative) specification](http://hl7.org/fhirpath/N1/).

Built using the [Superpower](https://github.com/datalust/superpower) parser combinator library (based on [Sprache](https://github.com/sprache/Sprache)), which provides token-driven parsing with friendly, human-readable error messages for invalid FHIRPath expressions.

## Key Features

- **Visitor Pattern Architecture** - Clean separation between AST structure and operations
- **Compile-Time Optimization** - Constant folding and short-circuiting, applied only where they provably preserve semantics
- **Expression Caching** - Parsed ASTs cached for repeated evaluations
- **Type Inference** - Static analyzer validates expressions before execution
- **High Performance** - Significant improvements over traditional switch-based evaluators
- **Extensible** - Custom functions registered via attributes and source generators
- **Instance Selectors** - Inline object construction (`Coding { code: '8480-6' }`) delegated to a host-supplied factory

## Installation

```bash
dotnet add package Ignixa.FhirPath
dotnet add package Ignixa.Serialization
dotnet add package Ignixa.Specification
```

## Quick Start

```csharp
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;

// Parse FHIR JSON
var patientJson = """{"resourceType":"Patient","active":true,"name":[{"given":["Jane"]}]}""";
var schema = FhirVersion.R4.GetSchemaProvider();
var element = JsonSourceNodeFactory.Parse(patientJson).ToElement(schema);

// Evaluate FHIRPath (with automatic caching)
var names = element.Select("name.given");
var isActive = element.IsTrue("active = true");
```

## Compile-Time Optimization

The parser can optimize expressions at compile time when using `CompilationOptions`:

```csharp
using Ignixa.FhirPath.Parser;

var parser = new FhirPathParser();
var options = new CompilationOptions { Optimize = true };

// Constant folding
var expr1 = parser.Parse("1 + 1", options);              // Optimized to: 2
var expr2 = parser.Parse("'hello' + 'world'", options);  // Optimized to: 'helloworld'

// Short-circuit evaluation, matching the three rows the evaluator itself short-circuits
var expr3 = parser.Parse("false and X", options);        // Optimized to: false (X not evaluated)
var expr4 = parser.Parse("true or X", options);          // Optimized to: true (X not evaluated)
var expr5 = parser.Parse("false implies X", options);    // Optimized to: true (X not evaluated)

// Left alone: X is always evaluated, so folding it away would discard an error it may signal
var expr6 = parser.Parse("X and false", options);        // Unchanged
var expr7 = parser.Parse("X + 0", options);              // Unchanged
var expr8 = parser.Parse("X and true", options);         // Unchanged
```

:::note
An optimization may only drop an operand that is itself a literal, or one the evaluator would not have
evaluated either. Every FHIRPath operand can signal an error, so rewriting `X and false` to `false`
would turn an expression that throws into one that quietly answers - and a rewrite that returns `X` in
place of the whole expression changes its type whenever `X` is not a boolean singleton.
:::

:::tip
The `Select()` extension methods automatically use optimized parsing. Manual optimization is only needed when using the parser API directly.
:::

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

:::warning
`Scalar()` returns the raw boxed .NET value. Don't call `.ToString()` on it to get display
text — a boolean gives `"True"` instead of FhirPath's `"true"`, and decimals format per the
current culture. Use `Select(expr).AsString()` for spec-conformant strings.
:::

### AsString

Converts a single-result expression to its FhirPath string representation (the spec's
`toString()` rules — lowercase booleans, invariant-culture decimals):

```csharp
var hasAddress = element.Select("address.exists()").AsString();  // "false", not "False"
var birthDate = element.Select("birthDate").AsString();
```

Returns `null` if the expression yields empty, multiple values, or the single result has no
primitive value (e.g. a complex/backbone element) — matching `Scalar()`'s empty/multiple contract.

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

### Instance Selectors

An instance selector constructs a new FHIR object inline, using the type name followed by a brace-delimited list of element assignments:

```text
Coding { system: 'http://loinc.org', code: '8480-6' }
Quantity { value: 120, unit: 'mm[Hg]' }
FHIR.Quantity { value: 120 }        // Namespace-qualified type name
Coding {}                           // Empty object
Coding {:}                          // Empty object, alternate form
Coding { `code`: 'c1' }             // Delimited (backtick) element name
```

Assigned values are themselves FHIRPath expressions, and selectors nest:

```text
(1 | 2 | 3).select(Coding { code: $this.toString() })
Observation { value: Quantity { value: 70 } }
Coding { code: 'TEST'.lower() }
```

Evaluation semantics:

- An empty input collection produces an empty result.
- An input collection with more than one item is an error.
- An element whose value expression evaluates to an empty collection is omitted from the created object.
- Construction itself is delegated to the host — see [Instance Creator](#instance-creator) for the required wiring.

The static analyzer resolves the declared type against the schema and infers it, so an unknown type name is reported as an analysis error. At runtime an unknown type is simply declined by the creator, producing an empty collection rather than an exception.

:::note
FHIRPath [object creation](https://build.fhir.org/ig/HL7/FHIRPath/branches/BP-FHIR-44774/index.html#instance-selector) is a Standard for Trial Use (STU) section of the specification, not normative. It has no conformance tests in `fhir-test-cases`, so behaviour the spec leaves open (choice-element naming, repeated assignments, primitive construction) is an Ignixa implementation decision rather than a compliance claim. Those decisions are recorded in `docs/features/fhirpath/investigations/instance-creation-delegate.md` in the repository.
:::

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
%resource          // Current resource (set via WithResource())
%rootResource      // Root resource (set via WithRootResource())
%context           // Evaluation context node, falling back to %resource
%this, %index      // Also spelled $this and $index
```

The specification's fixed constants resolve without any configuration:

```fhirpath
%ucum              // http://unitsofmeasure.org
%sct               // http://snomed.info/sct
%loinc             // http://loinc.org
%`vs-[name]`       // http://hl7.org/fhir/ValueSet/[name]
%`ext-[name]`      // http://hl7.org/fhir/StructureDefinition/[name]
```

`%terminologies` is recognised but not supported; referencing it throws, because it needs a terminology
service.

#### Hyphenated variable names must be quoted

The `vs-` and `ext-` families **only** expand in the backtick-delimited spelling. This is what the FHIR
profile of FHIRPath specifies - it writes both families quoted "to allow `-` in the name" - and what HAPI
does. A bare ``%vs-mine`` is an ordinary variable name, so unless you bind it yourself it fails with
`Attempting to access an undefined environment variable: vs-mine`:

```fhirpath
%`vs-administrative-gender`   // http://hl7.org/fhir/ValueSet/administrative-gender
%vs-administrative-gender     // error: undefined environment variable
```

Any name you supply through `WithEnvironmentVariable` can be read either way, since the quoting rule
applies only to the two specification prefixes.

#### `-` binds into a variable name, so subtraction needs whitespace

Ignixa's tokenizer takes `-` inside an unquoted `%name`, matching HAPI's lexer, so that names like
``%p-inactive`` - which appear in published `text/fhirpath` cqf-expression content - read as one name.
The consequence is that a hyphen touching a variable name is part of that name, not an operator:

```fhirpath
%a-b       // one variable named "a-b"
%a-1       // one variable named "a-1"
%a - b     // subtraction: %a minus b
%a -b      // subtraction: %a minus b
%a- b      // parse error - the name is "a-" and "b" follows it
```

Write whitespace **before** the hyphen when you mean subtraction. The failure mode is loud in every case
above: an unknown name, or a parse error - never a silently different number.

### Custom Variables

`EvaluationContext` is an immutable record: every `With*` method returns a new context rather than mutating the existing one.

```csharp
using Ignixa.FhirPath.Evaluation;

var context = new EvaluationContext()
    .WithResource(patientElement)               // Sets %resource
    .WithEnvironmentVariable("today", todayElement);

var result = element.Select("birthDate < %today", context);
```

`WithEnvironmentVariable` also accepts an `IEnumerable<IElement>` when a variable holds a collection. Object-initializer syntax works too, since the properties are `init`-only:

```csharp
var context = new EvaluationContext { Resource = patientElement };
```

### Reference Resolution (`resolve()`)

The `resolve()` function follows a `Reference` from one resource to another. Resolution happens in two stages, tried in order:

1. **In-instance resolution** — looks inside the resource under evaluation for a contained resource, a sibling Bundle/Parameters entry, or (when there is something to index) a bare `#`. This needs no configuration at all.
2. **`ElementResolver` fallback** — runs whenever in-instance resolution does not produce a result and an `ElementResolver` is configured on a `FhirEvaluationContext`. This is the extension point for genuinely external references (a different server, a database, a cache).

In-instance resolution requires a root to index: `RootResource` if set, otherwise `Resource` (the two can differ — for example, validation sets them to different elements while inside a contained resource's own scope). With neither set, there is nothing to index at all, so *every* reference — including a bare `#` — falls straight through to the `ElementResolver` fallback, or to empty if that isn't configured either.

#### In-instance resolution

No `ElementResolver` needed. It covers:

- Contained resources by `#id` (`"reference": "#p1"` finds the matching entry in `contained`).
- A bare `#` — **only when an index exists** (`Resource` or `RootResource` is set) — is decided entirely in-instance: it resolves to the containing resource when evaluated from inside one of that container's own contained resources, and to empty at the container's own scope (root, or a Bundle/Parameters entry). See the callout below — this is **not** the same rule as an unresolved `#id`.
- For a `Bundle` root, sibling entries by `fullUrl`, `Type/id`, `Type/id/_history/versionId`, and `fullUrl/_history/versionId` (versionId comes from the entry's `resource.meta.versionId`). When entries share a key the first entry wins, except that an entry's own `fullUrl` always takes precedence over a synthesized versioned key.
- For a `Parameters` root, resources nested under `parameter`/`part` (at any depth), keyed by `Type/id` only — Parameters entries have no `fullUrl`.

```csharp
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

IFhirSchemaProvider schemaProvider = FhirVersion.R4.GetSchemaProvider();

// subject.reference is "#p1" - Patient p1 is a contained resource, not a separate lookup.
var observationJson = """
{
  "resourceType": "Observation",
  "id": "obs1",
  "status": "final",
  "code": { "coding": [ { "system": "http://loinc.org", "code": "1234-5" } ] },
  "subject": { "reference": "#p1" },
  "contained": [
    { "resourceType": "Patient", "id": "p1" }
  ]
}
""";

var observation = ResourceJsonNode.Parse(observationJson).ToElement(schemaProvider);

// No ElementResolver configured - Resource is enough for resolve() to find the contained Patient.
var context = new EvaluationContext { Resource = observation };

var isPatient = observation
    .Select("Observation.subject.where(resolve() is Patient).exists()", context)
    .Single();

// isPatient.Value == true
```

The same applies to a `Bundle` root: `Bundle.entry.resource.ofType(Observation).subject.resolve()` finds a sibling `entry` by `fullUrl` or `Type/id` with no resolver configured, as long as `RootResource` (or `Resource`) points at the Bundle.

##### Bare `#` versus an unresolved `#id`

:::warning
An unresolved `#id` and an unresolved bare `#` behave differently, and this is the single most surprising part of the design:

- **`#id` that misses** the in-instance index falls through to the `ElementResolver`, exactly like any other unresolved reference.
- **Bare `#`** never falls through once an index exists (`Resource` or `RootResource` is set) — if it doesn't resolve to a containing resource in-instance, `resolve()` returns empty, and the `ElementResolver` is never consulted, even if one is configured and would return something.

Ignixa follows Firely for this asymmetry: `ScopedNodeExtensions.Resolve<T>` only short-circuits the exact string `"#"` — for an unresolved `#id` it still consults the external resolver. HAPI's `FHIRPathEngine.funcResolve` disagrees: it short-circuits every `#`-prefixed reference regardless of the id, so it never consults the host resolver for any fragment. See `GivenUnresolvedFragmentReference_WhenElementResolverCanResolveIt_ThenFallsBackToResolver` and the sibling bare-`#` tests in `ResolveFunctionTests.cs` for the executable contract.
:::

##### Container scoping

Contained resources are scoped to the nearest **container boundary**, not to the whole instance. A container boundary is the root itself, or any `Bundle.entry.resource` / `Parameters.parameter[.part].resource` — per R4 [`references.html` §2.3.0.8](https://hl7.org/fhir/R4/references.html#contained): "resolution stops at the elements `Bundle.entry.resource` and `Parameters.parameter.resource`, but not at `DomainResource.contained`".

Practically:

- A `#frag` reference inside a Bundle entry's resource resolves against **that entry's own** `contained` array, and cannot see a same-named `#frag` contained in a different entry.
- Bare `#` from inside a contained resource that itself lives inside a Bundle entry resolves to the **entry resource**, not the Bundle root.
- Sibling lookups (`fullUrl`, `Type/id`, and the versioned form) are **not** affected by container scoping — they remain cross-entry by design, because they are Bundle/Parameters-level lookups, not containment.

```csharp
// Two Bundle entries each contain an Organization with id "org1". Container scoping means each
// Patient's "#org1" resolves within its own entry, never the other entry's same-named contained
// resource.
IFhirSchemaProvider schemaProvider = FhirVersion.R4.GetSchemaProvider();

var bundleJson = """
{
  "resourceType": "Bundle",
  "type": "collection",
  "entry": [
    {
      "resource": {
        "resourceType": "Patient", "id": "patA",
        "managingOrganization": { "reference": "#org1" },
        "contained": [ { "resourceType": "Organization", "id": "org1", "name": "OrgA" } ]
      }
    },
    {
      "resource": {
        "resourceType": "Patient", "id": "patB",
        "managingOrganization": { "reference": "#org1" },
        "contained": [ { "resourceType": "Organization", "id": "org1", "name": "OrgB" } ]
      }
    }
  ]
}
""";

var bundle = ResourceJsonNode.Parse(bundleJson).ToElement(schemaProvider);
var context = new EvaluationContext { Resource = bundle };

var orgAName = bundle
    .Select("Bundle.entry.resource.ofType(Patient).where(id = 'patA').managingOrganization.resolve().name", context)
    .Single();
var orgBName = bundle
    .Select("Bundle.entry.resource.ofType(Patient).where(id = 'patB').managingOrganization.resolve().name", context)
    .Single();

// orgAName.Value == "OrgA", orgBName.Value == "OrgB" - each entry sees only its own contained pool.
```

#### External resolver (fallback)

Configure an `ElementResolver` on `FhirEvaluationContext` to resolve references that are not part of the instance being evaluated - a reference to a resource that lives on another server, in a database, or behind a cache.

```csharp
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Specification;

// Obtain a schema provider for your FHIR version
// Example: var schemaProvider = new R4CoreSchemaProvider();
IFhirSchemaProvider schemaProvider = GetSchemaProvider();

// Create a FHIR evaluation context with an ElementResolver that resolves references
var context = new FhirEvaluationContext().WithElementResolver(reference =>
{
    // reference will be a string like "Patient/123" or "Practitioner/456"

    // Fetch from your data store (database, API, cache, etc.)
    // This method should return the resource JSON or null if not found
    string? resourceJson = GetResourceByReference(reference);
    if (resourceJson == null)
        return null; // Return null if resource not found

    // Parse and return as IElement
    var sourceNode = JsonSourceNodeFactory.Parse(resourceJson);
    return sourceNode.ToElement(schemaProvider);
});

// Example implementation of GetResourceByReference:
// string? GetResourceByReference(string reference)
// {
//     // Parse reference (e.g., "Patient/123" -> type="Patient", id="123")
//     var parts = reference.Split('/', 2);
//     if (parts.Length != 2) return null;
//     
//     // Fetch from database, cache, or other data source
//     return FetchFromDatabase(parts[0], parts[1]);
// }

// Now resolve() works in FHIRPath expressions
var encounterJson = """
{
  "resourceType": "Encounter",
  "id": "enc1",
  "participant": [
    {
      "individual": {
        "reference": "Practitioner/dr-smith"
      }
    }
  ]
}
""";

var encounter = JsonSourceNodeFactory.Parse(encounterJson).ToElement(schemaProvider);

// Use resolve() to follow the reference and check the practitioner type
var practitioners = encounter.Select(
    "participant.individual.where(resolve() is Practitioner)", 
    context);

// Access properties of resolved resources
var practitionerNames = encounter.Select(
    "participant.individual.resolve().name.family",
    context);
```

**Common use cases:**

```csharp
// Check if a reference resolves to a specific resource type
"subject.resolve() is Patient"

// Access properties through references
"performer.resolve().name.family"

// Filter by resolved resource properties  
"participant.individual.where(resolve().active = true)"

// Chain multiple references
"encounter.resolve().serviceProvider.resolve().name"
```

:::note
The `resolve()` function's error-handling contract:

- The reference is not found in-instance, and either no `ElementResolver` is configured or it also misses → returns empty. This follows FHIRPath's propagation semantics - operations on empty collections return empty rather than throwing exceptions, so expressions can keep evaluating even when a reference can't be resolved.
- The configured `ElementResolver` throws → treated the same as "not found" (empty), **except** `OperationCanceledException`, `OutOfMemoryException` and `InsufficientExecutionStackException` (including when wrapped in an `AggregateException`), which propagate rather than being swallowed. The host resolver is caller-supplied code and a trust boundary, so an ordinary failure there is "reference not found" per spec - but cancellation, memory exhaustion and stack exhaustion are not ordinary failures.
- A defect while building or querying the in-instance index itself (for example a broken `IElement` implementation) is **not** treated as "not found" - it propagates. Only the external-resolver boundary is trusted enough to swallow exceptions; a bug in Ignixa's own resolution logic is not.

A contained or intra-Bundle/Parameters reference resolves with **no `ElementResolver` configured at all** - see [In-instance resolution](#in-instance-resolution). `ResolveFunctionTests.cs` (`test/Ignixa.FhirPath.Tests/Evaluation/`) is the executable spec behind this section - in-instance resolution, container scoping, the bare-`#` versus `#id` asymmetry, and this error-handling contract are all asserted there. If this page and that file ever disagree, trust the tests.
:::

### Instance Creator

[Instance selectors](#instance-selectors) require an `InstanceCreator` on the evaluation context. `Ignixa.FhirPath` references only `Ignixa.Abstractions` and has no object model of its own, so it delegates construction to the host — the same extension-point shape as `ElementResolver` for `resolve()`.

`Ignixa.Serialization` ships the reference implementation, `SourceNodeInstanceFactory`. Wire its `Create` method as a method group:

```csharp
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

IFhirSchemaProvider schemaProvider = FhirVersion.R4.GetSchemaProvider();

var context = new EvaluationContext()
    .WithInstanceCreator(new SourceNodeInstanceFactory(schemaProvider).Create);

var coding = element
    .Select("Coding { system: 'http://loinc.org', code: '8480-6' }", context)
    .Single();
```

The delegate signature is `Func<InstanceCreationRequest, IElement?>`. The request types live in `Ignixa.Abstractions`:

```csharp
public sealed record InstanceCreationRequest(
    string TypeName,
    string? NamespacePrefix,
    IReadOnlyList<InstanceElement> Elements);

public sealed record InstanceElement(string Name, IReadOnlyList<IElement> Values);
```

Implement it directly to construct into your own model. Return `null` to decline a type — the engine then yields an empty collection.

`InstanceCreator` is declared on the base `EvaluationContext`, so it can be combined with the FHIR-specific hooks in a single object initializer:

```csharp
var context = new FhirEvaluationContext
{
    ElementResolver = ResolveReference,
    InstanceCreator = new SourceNodeInstanceFactory(schemaProvider).Create
};
```

`SourceNodeInstanceFactory` builds `ISourceNode`-backed elements and makes these choices, which the STU spec section leaves open:

- Resources get a `resourceType` property, written last so an element assignment cannot forge it.
- Assigning a choice element by its base name (`value`) emits the type-suffixed property (`valueQuantity`) when the assigned value's type matches a declared choice type. Already-suffixed names pass through unchanged.
- Repeated assignments to the same element name aggregate into an array rather than overwriting. Exceeding the element's cardinality throws.
- If the target type is a FHIR primitive and the only assignment is `value`, the result is a primitive node (`HasPrimitiveValue == true`), not an object with a `value` child.
- Types the schema does not know, and the `System` namespace, are declined.

:::warning
Unlike `resolve()`, an unconfigured `InstanceCreator` does not degrade to an empty collection - evaluating an instance selector throws `InvalidOperationException` naming `WithInstanceCreator`. A stand-in node would carry no schema metadata and could not be serialized, so the failure is surfaced instead of hidden.
:::

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

### Expression Size Limits

Every stage after tokenizing (the parser, the AST builders, the analyzer, the delegate compiler and the evaluator) recurses over the expression. A .NET stack overflow cannot be caught, so an expression nested or chained deeply enough would terminate the whole process. Expression text often comes from outside the server (FHIRPath Patch paths, ViewDefinitions, `$transform` maps, package-supplied SearchParameters and invariants), so the parser rejects oversized expressions up front with a `FormatException`, and `TryParse` returns `false`:

| Limit | Default | What it counts |
|---|---|---|
| `MaxExpressionLength` | 16,384 | Characters in the expression text. |
| `MaxNestingDepth` | 16 | Depth of nested `(`, `[` and `{`: parentheses, function-call arguments, indexers and instance selectors. |
| `MaxExpressionDepth` | 256 | Depth of the syntax tree. Each step in a chain such as `a.b.c`, `1 + 2 + 3`, `a[0][1]` or `x.not().not()` adds a level, as does each level of nesting. |

The defaults apply to every `FhirPathParser`, including the one behind `Select`, `Scalar` and the other extension methods. The deepest expressions in the shipped FHIR search parameters and invariants reach a nesting depth of 6, a tree depth of 70 and a length of about 3,100 characters.

Bracket nesting has a much lower limit than tree depth because each bracket level costs the parser far more stack. Measured on a bare 1 MB thread (Windows x64, .NET 10, Release build, tier-1 JIT with dynamic PGO), the parser ran out of stack at about 25 levels when function calls and instance selectors alternated, while chains of operators ran to several hundred terms. These depths vary between runs and with JIT state, and a request thread has already used part of its stack, so treat them as an order of magnitude.

The limits are configurable through `CompilationOptions`:

```csharp
var parser = new FhirPathParser(new CompilationOptions
{
    MaxNestingDepth = 8,
    MaxExpressionDepth = 128,
});
```

Raise them only for trusted expressions, and only after measuring on the smallest thread stack the host uses. As a further safeguard, every recursive stage calls `RuntimeHelpers.EnsureSufficientExecutionStack()` as it recurses, so a thread whose stack is too small for an expression gets an exception rather than terminating the process:

- the parser's grammar checks at every bracket level and reports a `FormatException` (with the `InsufficientExecutionStackException` as its inner exception), so `TryParse` returns `false`;
- the AST builders, the delegate compiler, the evaluator and `FhirPathAnalyzer.Analyze` throw `InsufficientExecutionStackException`. `TryParse` lets it propagate rather than returning `false`, as it does cancellation and out-of-memory: none of them says the expression is invalid.

The limits keep these fallbacks off the normal path; they do not replace them.

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

try
{
    // Instance selectors throw when no InstanceCreator is configured.
    // Select() is lazy, so the throw surfaces on enumeration.
    var result = element.Select("Coding { code: 'c1' }").ToList();
}
catch (InvalidOperationException ex)
{
    // "Cannot construct 'Coding': no instance creator is configured on the evaluation context..."
    Console.WriteLine($"Not configured: {ex.Message}");
}

try
{
    // An invalid regular expression in matches(), matchesFull() or replaceMatches()
    // throws FhirPathEvaluationException (namespace Ignixa.FhirPath.Evaluation).
    // Select() may be lazy, so the throw can surface on enumeration.
    var result = element.Select("name.family.matches('abc[')").ToList();
}
catch (FhirPathEvaluationException ex)
{
    // "Invalid regular expression pattern 'abc[' in matches(): ..."
    // ex.InnerException is the ArgumentException raised by the regex parser.
    Console.WriteLine($"Invalid pattern: {ex.Message}");
}
```

:::note
FHIRPath follows propagation semantics for empty collections - operations on empty values typically return empty rather than throwing exceptions. Only constraint violations (like `single()` on multiple items) and invalid arguments throw. An instance selector is one of these: it also throws `InvalidOperationException` ("Instance selector requires a single input item or empty collection") when the input collection it is evaluated against holds more than one item. An invalid regular expression pattern in `matches()`, `matchesFull()` or `replaceMatches()` throws `FhirPathEvaluationException` (a subclass of `InvalidOperationException`) rather than returning empty. Empty propagation still applies first: if the input is empty, the pattern is never compiled and no exception is thrown.
:::

### Regular Expressions

Patterns in `matches()`, `matchesFull()` and `replaceMatches()` come from expression text, so the engine limits what one can cost:

- A pattern longer than 4,096 characters is refused with `FhirPathResourceLimitExceededException` before it is built. A pattern can be computed at runtime, and building one costs time and memory in proportion to its length: a 1,048,576-character pattern took 2.4 s and 391 MB to build, a 4,194,304-character one 36 s and 1.5 GB.
- A pattern runs on .NET's linear-time engine (`RegexOptions.NonBacktracking`), so a pattern such as `^(a+)+$` cannot backtrack catastrophically.
- A pattern that engine refuses runs on the backtracking engine instead. It refuses a lookahead or lookbehind, a backreference, an atomic group, a conditional or `\G`, and any pattern whose automaton would be too large (for example an alternation of a few thousand branches).
- Either engine runs under a match timeout of at most 1 second. Linear time is not small time: a large bounded repetition over a long input, such as `(a|aa){1000}b` over a million characters, can take tens of seconds, so the timeout is what bounds both engines. Exceeding it throws `FhirPathResourceLimitExceededException` (a `FhirPathEvaluationException`), with the `RegexMatchTimeoutException` as its inner exception; it never reads as "no match".
- Regex time is charged to the evaluation's step budget (`MaxSteps`) by the clock, at 250 ns a step: building a pattern, matching and replacing. Each call's timeout is the lesser of 1 second and the time the evaluation's remaining steps stand for (rounded down to 1 s, 250 ms, 62.5 ms or 15.6 ms), so the regex time of one evaluation is bounded by its budget, not by how many calls it makes. Measured: forty backtracking matches of about 0.3-0.6 s each ran 13-25 s before this and stop after 1.8-2.3 s now; ten `replaceMatches()` calls over 16,384-character inputs ran 6.2 s and stop after 2.0 s.
- The linear engine does not check the timeout while it computes captures, which `replaceMatches()` needs whenever its substitution contains a reference (`$0`, `$&`, `$1`, `${name}`, `$+`, `` $` ``, `$'`, `$_`). On a 1,000,000-character input, `replaceMatches('(.*a){80}', '$1')` ran for about 21 seconds under the 1-second timeout. Such a call therefore accepts at most `MaxRegexCaptureInputLength` characters of input (16,384 by default); measured, the capture phase then adds at most about 0.8 s to a match the timeout already bounds. A literal substitution, or `$$`, computes no captures and is not limited.
- `replaceMatches()` bounds the length of its result before building it, so a pattern that matches the empty string at every position cannot multiply its input.
- Case-insensitive matching (the `i` flag of `matchesFull()`, or an inline `(?i)`) uses invariant-culture case rules, so `'I'.matches('(?i)i')` is `true` on every host, including tr-TR.
- Constructed patterns are cached (bounded), so `where(x.matches(p))` over many items compiles `p` once.

### Evaluation Limits and Cancellation

A short expression can ask for exponential work regardless of the resource it runs on - `aggregate($total & $total, 'a')` doubles a string per item, nested `select()` calls multiply CPU with flat memory - so every evaluation runs under a budget carried by `EvaluationContext`:

| Limit (`FhirPathEvaluationLimits`) | Default | What it bounds |
|---|---|---|
| `MaxSteps` | 10,000,000 | Units of work: one per expression node evaluated, per item navigated or yielded (paths, `children()`, `descendants()`, projections), four per per-item scope (`where`, `select`, `all`, `aggregate` and the like), two per element a dedup classifies and per child pair a comparison descends into, one per equality comparison a collection operation makes, ten per child a dedup key hashes, per 16 characters of text a function scans or builds and of String operands a comparison, sort or dedup key reads (including Strings read from the resource), one per item that `sort()` (times log2 of the count), the aggregates, `join()`, `tail()`, `skip()`, `take()`, `ofType()` and `type()` walk, four per item of `lowBoundary()` and `highBoundary()`, and one per item each time a memoised collection is reused. Work whose cost its input does not predict - UCUM unit reading and conversion, regular expressions - is charged its elapsed time at 250 ns a step. Measured at the limit, an evaluation stops after about 1-3 s of CPU depending on the work; the slowest shapes are pairwise comparison of deep unkeyed elements and a union grown one item at a time. Set `Timeout` for a wall-clock bound. |
| `MaxCollectionSize` | 1,000,000 | Items in a collection the evaluator builds larger than its inputs: `descendants()`, `combine()`, `\|`, `aggregate()`, `select()`, `repeat()`, `repeatAll()`, `split()`, `toChars()`, and the pairing matrix behind `~`. Navigating the resource is not limited, but `descendants()` is: its input can be a multiplied collection. |
| `MaxStringLength` | 16,777,216 | Characters in a string built by `&`, `+`, `replace()`, `replaceMatches()`, `join()`, `encode()` or `escape()`, checked before it is allocated; any other engine-built string a function returns is checked as it is returned. That backstop runs at every function an engine-built string passes through (`first()`, `select($this)`, `where(...)`), so a long one is re-checked and re-charged (a step per 16 characters) each time it passes through one. Strings read from the resource are not limited. |
| `MaxRegexCaptureInputLength` | 16,384 | Input to a `replaceMatches()` whose substitution contains a reference (see Regular Expressions). |
| `Timeout` | none | Wall-clock time. Off by default so results do not depend on machine load; checked every 1,024 steps. |

Exceeding any limit throws `FhirPathResourceLimitExceededException`, a `FhirPathEvaluationException`. The limits apply to each `FhirPathEvaluator.Evaluate` (and `Select`) call separately and cover all enumeration of the sequence it returns, so a context reused across evaluations never carries one evaluation's count into the next. The defaults leave every shipped search parameter, invariant and conformance test unchanged: the whole suite still passes with the step budget lowered tenfold.

```csharp
var context = new EvaluationContext
{
    Limits = FhirPathEvaluationLimits.Default with { MaxSteps = 1_000_000, Timeout = TimeSpan.FromSeconds(2) },
    CancellationToken = cancellationToken,
};

try
{
    var results = patient.Select(untrustedExpression, context).ToList();
}
catch (FhirPathResourceLimitExceededException)
{
    // The expression asked for more than the evaluation allows: report it as too costly.
}
```

`CancellationToken` is observed when evaluation starts and every 1,024 steps, on both the interpreter and the compiled-delegate path (which checks it on entry and per item of a compiled `where()`); cancellation throws `OperationCanceledException`, not the limit exception, because "the caller gave up" and "the expression asked for too much" need different handling. Neither the token nor `Timeout` interrupts a single regular-expression call, which is bounded by the match timeout and the capture-input limit instead.

### Quantity Units

Relating two quantity units (comparison, equality, `~`, `sort()`, the aggregates, `toQuantity(unit)`, `comparable()`, arithmetic) asks UCUM to read them, which costs time in proportion to the symbols a unit contains - up to about two milliseconds for a single derived symbol such as `H`. So:

- A unit longer than 128 characters (`QuantityUnitConverter.MaxUnitLength`) is never handed to UCUM: it is treated like a unit UCUM cannot read, compatible only with the identical string. `1 'm.m.….m' = 1 'm65'` with 65 symbols is therefore empty rather than `true`.
- Each unit's reading (its dimension, and one of it in canonical units) is cached per process (4,096 units) and each answer per evaluation, so repeating a comparison costs a step.
- The UCUM work an evaluation does is charged to its step budget by the clock (250 ns a step).

Measured: `1 'cm' < 1 'm'` repeated 65,536 times took 28.8 s and now 0.3 s; 10,000 data-supplied quantities compared with `1 'm'` took 4.6 s and now 0.8 s; one 100,000-character unit took 5.1 s and is now refused in 0.15 s; a union of 1,000 distinct 1,000-character units took 53 s and now 0.24 s.

Duplicate elimination in `|`, `distinct()`, `isDistinct()`, `intersect()`, `exclude()`, `in`, `contains` and `repeat()` hashes every element whose equality allows it (strings, numbers, Booleans, codings and other complex elements) and groups quantities by unit compatibility (UCUM dimension) and by magnitude, converting each distinct unit once, so for such data its cost grows linearly with the collection. Three cases still compare pairwise, within the step budget: temporals, unreadable quantities, and quantities in units that do not convert linearly into the first unit of their dimension (each probe needs a UCUM conversion, charged 12,000 steps). Keying temporals is deferred, because it needs a multi-probe index: values with a timezone at full precision could be keyed by UTC instant, but a temporal also equals a String with its text and a timezone-less or partial value stays indeterminate against them, so it needs an index that probes more than one key per lookup. `subsetOf()` and `supersetOf()` use a stricter, type-aware equality and remain pairwise. `repeat()` additionally keeps its own limits of 10,000 iterations and 15,000,000 comparisons, and `repeatAll()` its 100,000 iterations (`FhirPathEvaluationLimits.MaxRepeatAllIterations`); all three throw `FhirPathResourceLimitExceededException`.

A subexpression whose value cannot change during an evaluation - rooted at `%resource`, `%rootResource` or `%context` and continuing only through navigation, `|`, and built-in functions that are pure functions of their focus (such as `descendants()`, `count()`, `as(Type)`; not `trace()`, `now()` or `resolve()`) - is computed once per evaluation and reused, except while a node or trace handler is observing the evaluation. That keeps `dom-3`, which walks the whole resource for every contained resource, linear in the resource's size: a Questionnaire with 150 contained ValueSet expansions validates well within the default budget.

How hosts react to the exception:

| Host | Outcome |
|---|---|
| Search indexing | The parameter is skipped and logged at Warning; the write succeeds. |
| Validation (`FhirPathInvariantCheck`, slicing) | The instance fails with an Error, because data crafted to exhaust a limit must not pass the invariant. |
| FHIRPath Patch | `RequestTooCostlyException` (HTTP 400, OperationOutcome `too-costly`). A path the parser refuses is a bad patch (HTTP 400, `invalid`). |
| `$transform` | `RequestTooCostlyException`. Each expression and the transformation as a whole are bounded by `Experimental:Transform:TimeoutSeconds`. A map expression the parser refuses is a bad request (HTTP 400). |
| GraphQL `fhirpath:` argument | The whole request fails as too costly (HTTP 400, `too-costly`); no partial data is returned. |
| De-identification | The rule fails closed, and the request fails as too costly (HTTP 400, `too-costly`). |
| Any other host | An unhandled `FhirPathResourceLimitExceededException` reaching the API is reported as `too-costly` rather than as a processing error. |
| SQL-on-FHIR | Rethrown as `FhirPathResourceLimitExceededException` with the resource type in the message. |

Every host that evaluates an expression taken from a request - Patch, `$transform`, GraphQL, MCP, de-identification and SQL-on-FHIR export - passes the request's cancellation token into evaluation. Search indexing and validation evaluate the server's own expressions, have no token to pass, and run under the default limits.

### Expression Caches

Parsed expressions and compiled delegates are cached by expression text. Every cache is capacity-bounded and drops its older generation when full rather than growing, so text a caller chooses cannot exhaust memory.

The shared cache is split into two tiers. Expressions the server owns, or that a tenant registered and the server persisted (search parameters, including custom ones, and profile discriminators), use a large tier. Expressions an API caller supplies (de-identification rule paths and `generalize` cases, GraphQL filters, FHIRPath Patch paths) are evaluated with `EvaluationContext.ExpressionIsRequestSupplied = true` and use a small separate tier, so a tenant submitting many distinct expressions evicts only other request-supplied expressions and never the server's own. Persisted tenant-authored text deliberately stays in the large tier, since it is bounded by what a tenant may register and is evaluated on every write. A host that evaluates text arriving with a request through the library should set the flag. Use `TypedElementExtensions.PrecompileRequestSupplied` instead of `Precompile` to validate such text.

`$transform` and the FHIR Mapping Language evaluator keep their own bounded caches.

### Logging and Error Messages

Evaluation error messages name types and functions and never include element values, so a message is safe to log with its exception. Log sites in the server sanitize expression text (line breaks replaced) and truncate it to 200 characters; hosts that log expression text should do the same.

`EvaluationContext.TraceHandler` receives the `trace()` name, which the expression author chooses, and a projection of resource data that may be PHI. A host that sets a handler must strip control characters from the name before logging and must not write the projection to logs, telemetry or responses unredacted. The server does not set a handler.

### Host Culture and Clock

Results do not depend on the host's culture or time zone. `now()`, `today()` and `timeOfDay()` all read the UTC clock and format with the invariant culture, so `today()` is the Gregorian UTC date even on a host whose default calendar is not Gregorian (th-TH, ar-SA), and `timeOfDay()` agrees with the time component of `now()`.

## Architecture

The FHIRPath engine uses a visitor pattern architecture with compile-time optimization:

```
Expression String → Parser (with optimization) → AST → Visitor-based Evaluator → Results
```

### Visitor Pattern Design

The AST uses the visitor pattern to cleanly separate structure from operations:

```csharp
// Expression base class
public abstract class Expression {
    public abstract TOutput AcceptVisitor<TContext, TOutput>(
        IFhirPathExpressionVisitor<TContext, TOutput> visitor,
        TContext context);
}

// Evaluator implements visitor interface
public class FhirPathEvaluator : IFhirPathExpressionVisitor<EvaluationContext, IEnumerable<IElement>> {
    public IEnumerable<IElement> VisitBinary(BinaryExpression expr, EvaluationContext context) { ... }
    public IEnumerable<IElement> VisitFunctionCall(FunctionCallExpression expr, EvaluationContext context) { ... }
    // ... 11 more visitor methods
}
```

**Benefits:**
- **Extensibility**: New visitors (optimizer, debugger, SQL translator) can be added without modifying AST
- **Type Safety**: Compiler enforces handling of all expression types via double dispatch
- **Separation of Concerns**: AST structure decoupled from evaluation/analysis logic
- **Consistency**: Matches the visitor pattern used throughout the Ignixa codebase

### Components

**FhirPathParser**: Tokenizes and parses expression strings into an Abstract Syntax Tree (AST) using the [Superpower](https://github.com/datalust/superpower) parser combinator library. Includes optional compile-time optimization pass for constant folding, short-circuiting, and algebraic simplification.

**FhirPathEvaluator**: Visitor-based evaluator that traverses the AST using the visitor pattern. Implements optimizations like ReferenceEquals context checking and constant indexer fast paths for improved performance.

**FhirPathAnalyzer**: Static analyzer visitor that performs type inference and validation on expressions before execution. Uses the same visitor infrastructure for consistency.

**FhirPathDelegateCompiler**: Compiles common AST patterns to executable delegates for improved performance. Supports approximately 80% of typical search parameter patterns:
- Simple paths: `name`, `identifier`
- Two-level paths: `name.family`, `identifier.value`
- Where clauses: `telecom.where(system='phone')`
- Collection functions: `name.first()`, `identifier.exists()`

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
Most applications should use the `Select()`, `Scalar()`, `AsString()`, `IsTrue()` extension methods which handle caching automatically. Direct API access is only needed for custom caching strategies or AST inspection.
:::

## Performance Tips

1. **Automatic caching works best with literal expressions** - use the same string repeatedly to benefit from cached ASTs and compiled delegates
2. **Use specific paths** instead of wildcards - simpler expressions compile better and benefit from optimizations like constant indexer fast paths
3. **Cache evaluation results** when evaluating same expression on same data multiple times
4. **Prefer simple patterns** - path navigation and basic predicates compile to fast delegates; complex expressions fall back to visitor-based interpreter
5. **Compile-time optimization is automatic** - the `Select()` extension methods automatically use optimized parsing with constant folding and short-circuiting
6. **Constant indexes are optimized** - expressions like `name[0]` use fast paths that avoid creating unnecessary intermediate objects

## Related Documentation

- [Abstractions](/docs/core-sdk/abstractions)
- [Validation](/docs/core-sdk/validation)
