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
- **Compile-Time Optimization** - Constant folding, short-circuiting, and algebraic simplification
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

// Short-circuit evaluation
var expr3 = parser.Parse("false and X", options);        // Optimized to: false (X not evaluated)
var expr4 = parser.Parse("true or X", options);          // Optimized to: true (X not evaluated)

// Algebraic simplification
var expr5 = parser.Parse("X + 0", options);              // Optimized to: X
var expr6 = parser.Parse("X * 1", options);              // Optimized to: X
var expr7 = parser.Parse("X and true", options);         // Optimized to: X
```

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
```

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
- For a `Bundle` root, sibling entries by `fullUrl`, `Type/id`, and `Type/id/_history/versionId`.
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
- The configured `ElementResolver` throws → treated the same as "not found" (empty), **except** `OperationCanceledException` and `OutOfMemoryException`, which propagate rather than being swallowed. The host resolver is caller-supplied code and a trust boundary, so an ordinary failure there is "reference not found" per spec - but cancellation and out-of-memory are not ordinary failures.
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
```

:::note
FHIRPath follows propagation semantics for empty collections - operations on empty values typically return empty rather than throwing exceptions. Only constraint violations (like `single()` on multiple items) throw. An instance selector is one of these: it also throws `InvalidOperationException` ("Instance selector requires a single input item or empty collection") when the input collection it is evaluated against holds more than one item.
:::

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
