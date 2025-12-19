# Investigation: FHIR Validation Reference Implementations

**Feature**: validation
**Status**: Complete
**Created**: 2025-10-20
**Original ADR**: N/A

---

## Executive Summary

This analysis examined two industry-leading FHIR validators:

1. **Firely Validator API** - Modern .NET validation engine with sophisticated schema-based architecture
2. **HAPI FHIR Validation Resources** - XML-based profiles, Schematron rules, and StructureDefinitions

### Key Takeaways

**Firely's architecture** represents a mature, production-ready approach built around three core patterns:

1. **Schema Compilation Pattern**: StructureDefinitions → compiled schemas → cached for reuse
2. **Composable Assertion Model**: Small, focused validators (IValidatable) combined into ElementSchemas
3. **Three-Stage State Management**: Global (run) → Instance (resource) → Location (element)

**HAPI's resources** provide canonical examples of:

1. **Schematron for invariants**: XML-based declarative constraints (e.g., `bundle.sch`)
2. **StructureDefinition organization**: Bundles grouped by type (types/resources/others)
3. **Constraint encoding**: Both FHIRPath and XPath expressions in XML

### Most Important Patterns to Adopt

1. **Schema Resolver + Cache** (Firely): `IElementSchemaResolver` with `ConcurrentDictionary` caching
2. **Validation State Threading** (Firely): Immutable state passed through validation pipeline
3. **Assertion Composition** (Firely): Single-element validators aggregated into schemas
4. **Result Reporting** (Firely): `ResultReport` with evidence chain for OperationOutcome generation
5. **Early-Exit Optimization** (Firely): Shortcut members for type validation

### Key Differences

| Aspect | Firely | HAPI | Recommendation |
|--------|--------|------|----------------|
| **Validation Model** | In-memory compiled schemas | XML resources + runtime parsing | **Firely** - Compiled schemas for performance |
| **Extensibility** | Pluggable validators via IValidatable | Schematron + custom rules | **Firely** - Cleaner composition model |
| **Performance** | ConcurrentDictionary caching, early-exit | N/A (resources only) | **Firely** - Built-in optimization |
| **FHIRPath Support** | Native with compiled expressions | Encoded in XML | **Firely** - Reuse our FhirPath engine |
| **Terminology** | ICodeValidationTerminologyService interface | N/A | **Firely** - Async terminology API |

---

## Firely Validator Analysis

### Architecture Overview

Firely uses a **layered validation pipeline** with clear separation of concerns:

```
┌─────────────────────────────────────────────────────────────┐
│ ValidationSettings (Configuration + Services)                │
│  - IElementSchemaResolver (schema cache)                     │
│  - ICodeValidationTerminologyService (terminology)           │
│  - FhirPathCompiler (invariant evaluation)                   │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ Schema Hierarchy (Compiled from StructureDefinitions)        │
│                                                               │
│  FhirSchema (Abstract Base)                                  │
│    ├─ ResourceSchema (Resource-level, Meta.profile magic)    │
│    ├─ DatatypeSchema (Datatype-level, abstract resolution)   │
│    └─ ExtensionSchema (Extension URL resolution)             │
│                                                               │
│  ElementSchema (Generic container)                           │
│    └─ Members: IReadOnlyCollection<IAssertion>               │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ Assertion Layer (Composable Validators)                      │
│                                                               │
│  IAssertion (Base Interface)                                 │
│    ├─ IValidatable (Single element)                          │
│    │    ├─ CardinalityValidator (0..1, 1..*, etc.)           │
│    │    ├─ FhirPathValidator (Invariants: ele-1, etc.)       │
│    │    ├─ BindingValidator (ValueSet validation)            │
│    │    ├─ FixedValidator (Fixed values)                     │
│    │    ├─ PatternValidator (Pattern matching)               │
│    │    └─ InvariantValidator (Abstract base)                │
│    │                                                          │
│    └─ IGroupValidatable (Multiple elements)                  │
│         ├─ SliceValidator (Slicing + discriminators)         │
│         └─ ElementSchema (Group of assertions)               │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│ Result Reporting                                             │
│                                                               │
│  ResultReport                                                │
│    ├─ Result: ValidationResult (Success/Failure/Undecided)   │
│    └─ Evidence: IReadOnlyList<IAssertion>                    │
│         └─ IssueAssertion (OperationOutcome issues)          │
│              ├─ IssueNumber (code)                           │
│              ├─ Severity (Error/Warning/Info)                │
│              ├─ Location (FHIRPath)                          │
│              └─ DefinitionPath (StructureDefinition ref)     │
└─────────────────────────────────────────────────────────────┘
```

### Key Interfaces and Abstractions

#### 1. Core Validation Interfaces

```csharp
// Base interface - all assertions are serializable
public interface IAssertion : IJsonSerializable
{
}

// Single-element validator
public interface IValidatable : IAssertion
{
    ResultReport Validate(IScopedNode input, ValidationSettings vc, ValidationState state);
}

// Group validator (e.g., cardinality on repeating elements)
public interface IGroupValidatable : IAssertion, IValidatable
{
    ResultReport Validate(IEnumerable<IScopedNode> input, ValidationSettings vc, ValidationState state);
}

// Fixed result (for optimization - no need to evaluate)
public interface IFixedResult
{
    ValidationResult FixedResult { get; }
}
```

**Pattern**: Interface segregation principle - validators implement only what they need.

#### 2. ElementSchema - The Composition Container

```csharp
public class ElementSchema : IGroupValidatable
{
    internal Canonical Id { get; private set; }
    internal IReadOnlyCollection<IAssertion> Members { get; private set; }

    // Performance optimization: type validators checked first
    internal IReadOnlyCollection<IAssertion> ShortcutMembers { get; private set; }

    // Quick access to cardinality validators (run even when input is empty)
    internal IReadOnlyCollection<CardinalityValidator> CardinalityValidators { get; private set; }

    internal virtual ResultReport ValidateInternal(
        IEnumerable<IScopedNode> input,
        ValidationSettings vc,
        ValidationState state)
    {
        // Empty input optimization: only run cardinality + slice validators
        if (!input.Any())
        {
            var results = new List<ResultReport>();

            if (CardinalityValidators.Any())
                results.AddRange(CardinalityValidators.Select(cv => cv.Validate(nothing, vc, state)));

            var sliceValidators = Members.OfType<SliceValidator>().Where(vc.Filter);
            if (sliceValidators.Any())
                results.AddRange(sliceValidators.Select(sv => sv.Validate(nothing, vc, state)));

            return results.Any() ? ResultReport.Combine(results) : ResultReport.SUCCESS;
        }

        // Normal flow: run all members, apply filter
        var members = Members.Where(vc.Filter);
        var subresult = members.Select(ma => ma.ValidateMany(input, vc, state));
        return ResultReport.Combine(subresult.ToList());
    }
}
```

**Key Insights**:
- **Lazy evaluation**: Empty input shortcut (performance)
- **Filtering**: `ValidationSettings.Filter()` allows selective validation
- **Result aggregation**: `ResultReport.Combine()` merges multiple results

#### 3. ValidationState - Three-Tier State Threading

```csharp
public record ValidationState
{
    // Tier 1: Global state (shared across entire validation run)
    internal class GlobalState
    {
        public ValidationLogger RunValidations { get; private set; } = new();
        public int ResourcesValidated { get; set; } = 0;
        public FhirPathCompilerCache? FPCompilerCache { get; internal set; }
    }
    internal GlobalState Global { get; private set; } = new();

    // Tier 2: Instance state (per resource being validated)
    internal class InstanceState
    {
        public string? ResourceUrl { get; set; }
    }
    internal InstanceState Instance { get; private set; } = new();

    // Tier 3: Location state (current element + definition)
    internal class LocationState
    {
        public DefinitionPath DefinitionPath { get; set; } = DefinitionPath.Start();
        public InstancePath InstanceLocation { get; set; } = InstancePath.Start();
    }
    internal LocationState Location { get; private set; } = new();

    // Immutable state updates
    internal ValidationState UpdateLocation(Func<DefinitionPath, DefinitionPath> update) =>
        new() { Global = Global, Instance = Instance, Location = new LocationState { ... } };
}
```

**Pattern**: Immutable state with nested scope - pass-by-value semantics prevent state pollution.

**Benefits**:
- **Thread-safe**: No shared mutable state
- **Clean composition**: Each validator receives complete context
- **Traceable**: DefinitionPath + InstancePath for precise error reporting

#### 4. Schema Resolution and Caching

```csharp
public interface IElementSchemaResolver
{
    ElementSchema? GetSchema(Canonical schemaUri);
}

public class CachedElementSchemaResolver : IElementSchemaResolver
{
    private readonly ConcurrentDictionary<Canonical, ElementSchema?> _cache = new();
    public IElementSchemaResolver Source { get; private set; }

    public ElementSchema? GetSchema(Canonical schemaUri)
    {
        // Direct cache hit
        if (_cache.TryGetValue(schemaUri, out ElementSchema? schema))
            return schema;

        // Fetch from source and cache
        var newValue = Source.GetSchema(schemaUri);
        return _cache.GetOrAdd(schemaUri, newValue);
    }
}
```

**Pattern**: Decorator pattern with ConcurrentDictionary for thread-safe caching.

**Benefits**:
- **No locks needed**: ConcurrentDictionary handles race conditions
- **External cache support**: Constructor overload accepts shared cache
- **Schema reuse**: Same schema instance for all validations

### Terminology Integration

```csharp
public interface ICodeValidationTerminologyService
{
    Task<Parameters> ValueSetValidateCode(Parameters parameters, string? id = null, bool useGet = false);
    Task<Parameters> Subsumes(Parameters parameters, string? id = null, bool useGet = false);
}

// In ValidationSettings
public ICodeValidationTerminologyService ValidateCodeService;
public ValidateCodeServiceFailureHandler? HandleValidateCodeServiceFailure = null;

// BindingValidator uses it
private static (Issue?, string?) callService(ValidateCodeParameters parameters, ValidationSettings ctx, string display)
{
    try
    {
        var callParams = parameters.Build();
        return interpretResults(TaskHelper.Await(() => ctx.ValidateCodeService.ValueSetValidateCode(callParams)), display);
    }
    catch (FhirOperationException tse)
    {
        var desiredResult = ctx.HandleValidateCodeServiceFailure?.Invoke(parameters, tse)
            ?? TerminologyServiceExceptionResult.Warning;
        // Return warning or error based on handler result
    }
}
```

**Pattern**: Async terminology service with pluggable error handling.

**Benefits**:
- **Async support**: Non-blocking terminology lookups
- **Graceful degradation**: Handler decides warning vs error on failure
- **Extensible**: Custom terminology services via interface

### Performance Optimizations

#### 1. Shortcut Members (Early-Exit Pattern)

```csharp
internal IReadOnlyCollection<IAssertion> ShortcutMembers { get; private set; }

private static IReadOnlyCollection<IAssertion> extractShortcutMembers(IEnumerable<IAssertion> members)
    => members.OfType<FhirTypeLabelValidator>().ToList();

internal virtual ResultReport ValidateInternal(IScopedNode input, ValidationSettings vc, ValidationState state)
{
    // Run type validators first - if they fail, skip other checks
    if (ShortcutMembers.Count != 0)
    {
        var subResult = ShortcutMembers.Where(vc.Filter).Select(ma => ma.ValidateOne(input, vc, state));
        var report = ResultReport.Combine(subResult.ToList());
        if (!report.IsSuccessful) return report;
    }

    // Continue with full validation
    var members = Members.Where(vc.Filter);
    var subresult = members.Select(ma => ma.ValidateOne(input, vc, state));
    return ResultReport.Combine(subresult.ToList());
}
```

**Benefit**: Fail fast on type mismatches before running expensive invariants.

#### 2. FHIRPath Compilation Cache

```csharp
public class FhirPathValidator : InvariantValidator
{
    private CompiledExpression? _compiledExpression;
    private FhirPathCompiler? _lastUsedCompiler;

    private CompiledExpression getDefaultCompiledExpression(FhirPathCompiler compiler)
    {
        if (compiler == _lastUsedCompiler && _compiledExpression is not null)
            return _compiledExpression;

        _lastUsedCompiler = compiler;
        return _compiledExpression = compiler.Compile(Expression);
    }
}
```

**Pattern**: Lazy compilation + memoization per-validator instance.

#### 3. Empty Input Optimization

```csharp
if (!input.Any())
{
    // Only run validators that matter for empty input
    // - CardinalityValidator: Check minimum cardinality (required elements)
    // - SliceValidator: Check mandatory slices
    // Skip all other assertions (huge performance gain)
}
```

**Benefit**: Avoid traversing entire schema when element is absent.

### Configuration and Extensibility

```csharp
public class ValidationSettings
{
    // Required services
    internal IElementSchemaResolver ElementSchemaResolver;
    public ICodeValidationTerminologyService ValidateCodeService;

    // Optional customizations
    public TypeNameMapper? TypeNameMapper = null;
    public FhirPathCompiler? FhirPathCompiler = null;
    public ValidationProfileSelector? SelectValidationProfiles = null;
    public ExtensionUrlFollower? FollowExtensionUrl = null;

    // Filtering (selective validation)
    public ICollection<Predicate<IAssertion>> IncludeFilters = new List<Predicate<IAssertion>>();
    public ICollection<Predicate<IAssertion>> ExcludeFilters = new List<Predicate<IAssertion>>();

    // Best practices handling
    public ValidateBestPracticesSeverity ConstraintBestPractices = ValidateBestPracticesSeverity.Warning;

    // Failure handlers
    public ValidateCodeServiceFailureHandler? HandleValidateCodeServiceFailure = null;
}
```

**Extensibility Points**:
1. **Custom type mapping**: `TypeNameMapper` (e.g., map "code" → canonical URL)
2. **Profile selection**: `ValidationProfileSelector` (filter Meta.profile)
3. **Extension handling**: `ExtensionUrlFollower` (resolve extension URLs)
4. **Selective validation**: Include/exclude filters for assertions
5. **Error handling**: Terminology service failure handler

### Assertion Examples

#### CardinalityValidator

```csharp
public class CardinalityValidator : IGroupValidatable
{
    public int? Min { get; private set; }
    public int? Max { get; private set; }

    ResultReport IGroupValidatable.Validate(IEnumerable<IScopedNode> input, ValidationSettings _, ValidationState s)
    {
        var count = input.Count();
        return !inRange(count)
            ? new IssueAssertion(Issue.CONTENT_INCORRECT_OCCURRENCE,
                $"Instance count is {count}, which is not within the specified cardinality of {Min}..{Max}")
                .AsResult(s, input.FirstOrDefault(), nameof(CardinalityValidator))
            : ResultReport.SUCCESS;
    }

    private bool inRange(int x) => (!Min.HasValue || x >= Min.Value) && (!Max.HasValue || x <= Max.Value);
}
```

**Pattern**: Group validator checks collection size.

#### BindingValidator

```csharp
public class BindingValidator : IValidatable
{
    public Canonical ValueSetUri { get; private set; }
    public BindingStrength? Strength { get; private set; }
    public bool AbstractAllowed { get; private set; }

    ResultReport IValidatable.Validate(IScopedNode input, ValidationSettings vc, ValidationState s)
    {
        if (!ModelInspector.Base.IsBindable(input.InstanceType))
            return ResultReport.SUCCESS; // Not applicable

        if (input.ParseBindable() is { } bindable)
        {
            // Check content requirements (required bindings need code)
            var result = verifyContentRequirements(input, bindable, s);
            if (!result.IsSuccessful) return result;

            // Only validate required bindings against terminology service
            if (Strength != BindingStrength.Required) return ResultReport.SUCCESS;

            // Call terminology service
            return validateCode(bindable, vc, s, input);
        }

        return ResultReport.SUCCESS;
    }
}
```

**Pattern**: Async terminology validation only for required bindings.

#### SliceValidator

```csharp
public class SliceValidator : IGroupValidatable
{
    public bool Ordered { get; private set; }
    public bool DefaultAtEnd { get; private set; }
    public IAssertion Default { get; private set; }
    public IReadOnlyList<SliceCase> Slices { get; private set; }

    public class SliceCase
    {
        public string Name { get; private set; }
        public IAssertion Condition { get; private set; }  // Discriminator
        public IAssertion Assertion { get; private set; }  // Slice schema
    }

    ResultReport IGroupValidatable.Validate(IEnumerable<IScopedNode> input, ValidationSettings vc, ValidationState state)
    {
        var lastMatchingSlice = -1;
        var buckets = new Buckets(Slices, Default);

        foreach (var candidate in input)
        {
            bool hasSucceeded = false;

            // Try to match slice conditions (discriminators)
            for (var sliceNumber = 0; sliceNumber < Slices.Count; sliceNumber++)
            {
                var conditionResult = Slices[sliceNumber].Condition.ValidateOne(candidate, vc, state);

                if (conditionResult.IsSuccessful)
                {
                    // Check ordering if required
                    if (sliceNumber < lastMatchingSlice && Ordered)
                        evidence.Add(new IssueAssertion(...).AsResult(...));

                    buckets.AddToSlice(Slices[sliceNumber], candidate, index);
                    hasSucceeded = true;
                    break; // Single match (unless MultiCase=true)
                }
            }

            // No slice matched - add to default
            if (!hasSucceeded)
                buckets.AddToDefault(candidate, index);
        }

        // Validate each bucket against its slice schema
        return ResultReport.Combine(buckets.Validate(vc, state));
    }
}
```

**Pattern**: Bucket-based slicing with condition matching (discriminators).

**Key Features**:
- **Ordered slicing**: Track last matched slice
- **Default bucket**: Elements not matching any slice
- **MultiCase support**: Allow multiple slice matches per element

---

## HAPI Validator Resources Analysis

### Resource Structure and Organization

HAPI provides canonical FHIR validation resources organized into:

```
hapi-fhir-validation-resources-r4/
├── src/main/resources/org/hl7/fhir/r4/model/
│   ├── profile/
│   │   ├── profiles-types.xml       (6.2 MB) - Base datatypes (Element, Identifier, etc.)
│   │   ├── profiles-resources.xml   (20 MB)  - All resource profiles
│   │   └── profiles-others.xml      (1.5 MB) - Other definitions
│   ├── schema/
│   │   ├── *.sch                    (145 files) - Schematron rules per resource
│   │   └── fhir-invariants.sch      - Combined invariants
│   └── extension/
│       └── extension-definitions.xml - Standard extensions
```

### Profile Definitions (StructureDefinitions)

**Format**: XML Bundle containing StructureDefinition resources

```xml
<Bundle xmlns="http://hl7.org/fhir">
  <type value="collection"/>
  <entry>
    <fullUrl value="http://hl7.org/fhir/StructureDefinition/Element"/>
    <resource>
      <StructureDefinition>
        <id value="Element"/>
        <url value="http://hl7.org/fhir/StructureDefinition/Element"/>
        <name value="Element"/>
        <kind value="complex-type"/>
        <abstract value="true"/>
        <type value="Element"/>

        <snapshot>
          <element id="Element">
            <path value="Element"/>
            <min value="0"/>
            <max value="*"/>
            <constraint>
              <key value="ele-1"/>
              <severity value="error"/>
              <human value="All FHIR elements must have a @value or children"/>
              <expression value="hasValue() or (children().count() &gt; id.count())"/>
              <xpath value="@value|f:*|h:div"/>
            </constraint>
          </element>

          <element id="Element.id">
            <path value="Element.id"/>
            <min value="0"/>
            <max value="1"/>
            <type>
              <code value="http://hl7.org/fhirpath/System.String"/>
            </type>
          </element>

          <element id="Element.extension">
            <path value="Element.extension"/>
            <slicing>
              <discriminator>
                <type value="value"/>
                <path value="url"/>
              </discriminator>
              <rules value="open"/>
            </slicing>
            <min value="0"/>
            <max value="*"/>
            <type>
              <code value="Extension"/>
            </type>
          </element>
        </snapshot>
      </StructureDefinition>
    </resource>
  </entry>
</Bundle>
```

**Key Insights**:

1. **Snapshot vs Differential**: Profiles include full snapshot (computed view)
2. **Constraint encoding**: Both FHIRPath (`expression`) and XPath (`xpath`) provided
3. **Slicing definition**: Discriminator paths + rules encoded in XML
4. **Type references**: Canonical URLs or FHIR types
5. **Cardinality**: `min`/`max` on every element

### Validation Rule Encoding (Schematron)

**Format**: Schematron XML with XPath assertions

```xml
<?xml version="1.0" encoding="UTF-8"?>
<sch:schema xmlns:sch="http://purl.oclc.org/dsdl/schematron" queryBinding="xslt2">
  <sch:ns prefix="f" uri="http://hl7.org/fhir"/>
  <sch:ns prefix="h" uri="http://www.w3.org/1999/xhtml"/>

  <!-- Global invariants -->
  <sch:pattern>
    <sch:title>Global</sch:title>
    <sch:rule context="f:extension">
      <sch:assert test="exists(f:extension)!=exists(f:*[starts-with(local-name(.), 'value')])">
        ext-1: Must have either extensions or value[x], not both
      </sch:assert>
    </sch:rule>
  </sch:pattern>

  <!-- Resource-specific rules -->
  <sch:pattern>
    <sch:title>Bundle</sch:title>
    <sch:rule context="f:Bundle">
      <sch:assert test="not(f:total) or (f:type/@value = 'searchset') or (f:type/@value = 'history')">
        bdl-1: total only when a search or history
      </sch:assert>

      <sch:assert test="not(f:type/@value='document') or f:entry[1]/f:resource/f:Composition">
        bdl-11: A document must have a Composition as the first resource
      </sch:assert>

      <sch:assert test="(f:type/@value = 'history') or (count(for $entry in f:entry[f:resource]
        return $entry[count(parent::f:Bundle/f:entry[f:fullUrl/@value=$entry/f:fullUrl/@value
        and ((not(f:resource/*/f:meta/f:versionId/@value)
        and not($entry/f:resource/*/f:meta/f:versionId/@value))
        or f:resource/*/f:meta/f:versionId/@value=$entry/f:resource/*/f:meta/f:versionId/@value)])!=1])=0)">
        bdl-7: FullUrl must be unique in a bundle, or else entries with the same fullUrl
        must have different meta.versionId (except in history bundles)
      </sch:assert>
    </sch:rule>

    <sch:rule context="f:Bundle/f:entry">
      <sch:assert test="not(exists(f:fullUrl[contains(string(@value), '/_history/')]))">
        bdl-8: fullUrl cannot be a version specific reference
      </sch:assert>

      <sch:assert test="exists(f:resource) or exists(f:request) or exists(f:response)">
        bdl-5: must be a resource unless there's a request or response
      </sch:assert>
    </sch:rule>
  </sch:pattern>
</sch:schema>
```

**Key Insights**:

1. **XPath-based**: Declarative rules with XPath 2.0 expressions
2. **Context-specific**: Rules scoped to specific elements
3. **Named constraints**: Constraint keys (e.g., `bdl-1`) match StructureDefinition
4. **Complex logic**: Multi-level XPath for sophisticated checks (uniqueness, conditional requirements)
5. **Human-readable messages**: Embedded in assertions

### Extension Handling

Extension definitions follow the same StructureDefinition pattern:

```xml
<StructureDefinition>
  <id value="patient-birthPlace"/>
  <url value="http://hl7.org/fhir/StructureDefinition/patient-birthPlace"/>
  <name value="birthPlace"/>
  <status value="draft"/>
  <kind value="complex-type"/>
  <abstract value="false"/>
  <context>
    <type value="element"/>
    <expression value="Patient"/>
  </context>
  <type value="Extension"/>
  <snapshot>
    <element id="Extension">
      <path value="Extension"/>
      <max value="1"/>
    </element>
    <element id="Extension.url">
      <path value="Extension.url"/>
      <fixedUri value="http://hl7.org/fhir/StructureDefinition/patient-birthPlace"/>
    </element>
    <element id="Extension.value[x]">
      <path value="Extension.value[x]"/>
      <type>
        <code value="Address"/>
      </type>
    </element>
  </snapshot>
</StructureDefinition>
```

**Key Insights**:

1. **Context declaration**: Where extension is valid (`Patient` here)
2. **Fixed URL**: Extension identity constraint
3. **Type constraints**: Only `Address` allowed for value[x]
4. **Cardinality**: Extensions can restrict max to 1

---

## Comparison Table

| Aspect | Firely Approach | HAPI Approach | Recommendation for Ignixa |
|--------|----------------|---------------|---------------------------|
| **Architecture** | Compiled schema tree (ElementSchema → IAssertion[]) | XML resources loaded at runtime | **Firely** - Pre-compile schemas for performance |
| **Validation Model** | Visitor pattern with IValidatable/IGroupValidatable | Schematron + XML validation | **Firely** - More flexible, testable |
| **State Management** | Immutable ValidationState (Global/Instance/Location) | Not applicable (resources only) | **Firely** - Thread-safe, traceable |
| **Terminology** | Async ICodeValidationTerminologyService with error handlers | N/A | **Firely** - Matches our async patterns |
| **Performance** | ConcurrentDictionary caching, early-exit, lazy evaluation | N/A | **Firely** - Built-in optimizations |
| **FHIRPath Support** | Compiled expressions with caching (`FhirPathValidator`) | XPath encoded in XML | **Firely** - Reuse `Ignixa.FhirPath` |
| **Slicing** | SliceValidator with bucket-based matching | Discriminator definitions in XML | **Firely** - Algorithmic implementation |
| **Extensibility** | Pluggable validators, filters, custom handlers | Static XML resources | **Firely** - Open for extension |
| **Result Reporting** | ResultReport with evidence chain → OperationOutcome | N/A | **Firely** - Structured, hierarchical |
| **Schema Organization** | Hierarchical (FhirSchema → ResourceSchema/DatatypeSchema) | Flat (Bundle of StructureDefinitions) | **Firely** - Type-specific logic |
| **Constraint Keys** | Issue numbers (constants) + human messages | Constraint keys (e.g., `ele-1`) in XML | **Hybrid** - Use keys + issue codes |

---

## Recommended Patterns for Ignixa

### Pattern 1: Schema Resolver with Caching

**Description**: Centralized schema resolution with thread-safe caching

**Benefits**:
- **Performance**: Schemas compiled once, reused many times
- **Thread-safe**: ConcurrentDictionary handles races
- **Pluggable**: Interface allows multiple implementations (file, embedded, remote)

**Applicability with ResourceJsonNode**:

```csharp
// Interface (Ignixa.Domain or Ignixa.Validation.Abstractions)
public interface IValidationSchemaResolver
{
    ValidationSchema? GetSchema(Canonical canonicalUrl);
}

// Implementation with caching
public class CachedValidationSchemaResolver : IValidationSchemaResolver
{
    private readonly ConcurrentDictionary<Canonical, ValidationSchema?> _cache = new();
    private readonly IValidationSchemaResolver _source;

    public CachedValidationSchemaResolver(IValidationSchemaResolver source)
    {
        _source = source;
    }

    public ValidationSchema? GetSchema(Canonical canonicalUrl)
    {
        if (_cache.TryGetValue(canonicalUrl, out var schema))
            return schema;

        var newSchema = _source.GetSchema(canonicalUrl);
        return _cache.GetOrAdd(canonicalUrl, newSchema);
    }
}

// Source implementation (from Ignixa.Specification)
public class StructureDefinitionSchemaResolver : IValidationSchemaResolver
{
    private readonly IStructureDefinitionSummaryProvider _provider;

    public ValidationSchema? GetSchema(Canonical canonicalUrl)
    {
        var sd = _provider.Provide(canonicalUrl.ToString());
        if (sd == null) return null;

        // Build schema from StructureDefinition
        return SchemaBuilder.FromStructureDefinition(sd);
    }
}
```

**Usage in Application Layer**:

```csharp
// Startup registration (Ignixa.Api/Program.cs)
builder.Services.AddSingleton<IStructureDefinitionSummaryProvider, R4StructureDefinitionProvider>();
builder.Services.AddSingleton<IValidationSchemaResolver>(sp =>
{
    var provider = sp.GetRequiredService<IStructureDefinitionSummaryProvider>();
    var source = new StructureDefinitionSchemaResolver(provider);
    return new CachedValidationSchemaResolver(source); // Wrap with cache
});
```

---

### Pattern 2: Immutable Validation State Threading

**Description**: Pass immutable state through validation pipeline (Global → Instance → Location)

**Benefits**:
- **Thread-safe**: No shared mutable state
- **Traceable**: Complete context at every validation point
- **Composable**: Easy to add new state tiers

**Applicability with ResourceJsonNode**:

```csharp
// Ignixa.Validation/ValidationState.cs
public record ValidationState
{
    // Tier 1: Run-level state (shared across entire validation)
    internal class GlobalState
    {
        public int ResourcesValidated { get; set; } = 0;
        public Dictionary<string, object> Cache { get; } = new();
    }
    internal GlobalState Global { get; private init; } = new();

    // Tier 2: Instance-level state (per resource)
    internal class InstanceState
    {
        public string? ResourceType { get; set; }
        public string? ResourceId { get; set; }
    }
    internal InstanceState Instance { get; private init; } = new();

    // Tier 3: Location state (current element path)
    internal class LocationState
    {
        public string InstancePath { get; set; } = string.Empty;
        public string? DefinitionPath { get; set; }
    }
    internal LocationState Location { get; private init; } = new();

    // Immutable update methods
    public ValidationState WithLocation(string instancePath, string? definitionPath) =>
        this with
        {
            Location = new LocationState
            {
                InstancePath = instancePath,
                DefinitionPath = definitionPath
            }
        };

    public ValidationState WithInstance(string resourceType, string? resourceId) =>
        this with
        {
            Instance = new InstanceState
            {
                ResourceType = resourceType,
                ResourceId = resourceId
            }
        };
}

// Usage in validator
public class ResourceValidator : IValidator
{
    public ValidationResult Validate(ResourceJsonNode resource, ValidationSettings settings, ValidationState state)
    {
        // Update state for this instance
        state = state.WithInstance(resource.ResourceType, resource.Id);

        // Validate child elements (state flows down)
        foreach (var element in resource.GetElements())
        {
            var elementState = state.WithLocation($"{resource.ResourceType}.{element.Name}", definitionPath: null);
            var result = ValidateElement(element, settings, elementState);
            // ...
        }
    }
}
```

---

### Pattern 3: Composable Assertion Model

**Description**: Small, single-purpose validators composed into schemas

**Benefits**:
- **Testability**: Each validator is isolated and testable
- **Reusability**: Validators reused across different schemas
- **Maintainability**: Clear single responsibility

**Applicability with ResourceJsonNode**:

```csharp
// Base interfaces (Ignixa.Validation.Abstractions)
public interface IValidationAssertion
{
    ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state);
}

// Example: Cardinality validator
public class CardinalityAssertion : IValidationAssertion
{
    public int? Min { get; }
    public int? Max { get; }

    public CardinalityAssertion(int? min, int? max)
    {
        Min = min;
        Max = max;
    }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        if (node is JsonArray array)
        {
            var count = array.Count;
            if (Min.HasValue && count < Min.Value)
                return ValidationResult.Failure($"Expected at least {Min} elements, found {count}");
            if (Max.HasValue && count > Max.Value)
                return ValidationResult.Failure($"Expected at most {Max} elements, found {count}");
        }

        return ValidationResult.Success;
    }
}

// Example: FHIRPath invariant validator
public class FhirPathInvariantAssertion : IValidationAssertion
{
    private readonly string _key;
    private readonly string _expression;
    private readonly string _humanDescription;
    private readonly FhirPathExpression _compiled;

    public FhirPathInvariantAssertion(string key, string expression, string description)
    {
        _key = key;
        _expression = expression;
        _humanDescription = description;
        _compiled = FhirPathEvaluator.Compile(expression); // Use Ignixa.FhirPath
    }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        // Convert JsonNode to ISourceNode for FHIRPath evaluation
        var sourceNode = JsonNodeSourceNode.FromJsonNode(node);
        var result = _compiled.Evaluate(sourceNode);

        if (!result.IsTrue())
        {
            return ValidationResult.Failure(
                $"Constraint '{_key}' failed: {_humanDescription}",
                issueCode: _key
            );
        }

        return ValidationResult.Success;
    }
}

// Composite schema (container)
public class ElementValidationSchema
{
    public string ElementPath { get; }
    public IReadOnlyList<IValidationAssertion> Assertions { get; }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        var results = new List<ValidationResult>();

        foreach (var assertion in Assertions)
        {
            var result = assertion.Validate(node, settings, state);
            results.Add(result);
        }

        return ValidationResult.Combine(results);
    }
}
```

**Schema Building**:

```csharp
// Build schema from StructureDefinition
public static ElementValidationSchema FromElementDefinition(IElementDefinitionSummary element)
{
    var assertions = new List<IValidationAssertion>();

    // Cardinality
    if (element.Min.HasValue || element.Max.HasValue)
        assertions.Add(new CardinalityAssertion(element.Min, element.Max));

    // Type checking
    if (element.Type.Any())
        assertions.Add(new TypeAssertion(element.Type));

    // Fixed value
    if (element.Fixed != null)
        assertions.Add(new FixedValueAssertion(element.Fixed));

    // Invariants
    foreach (var constraint in element.Constraint)
    {
        if (!string.IsNullOrEmpty(constraint.Expression))
            assertions.Add(new FhirPathInvariantAssertion(
                constraint.Key,
                constraint.Expression,
                constraint.Human
            ));
    }

    // Binding
    if (element.Binding != null)
        assertions.Add(new BindingAssertion(
            element.Binding.ValueSet,
            element.Binding.Strength
        ));

    return new ElementValidationSchema(element.Path, assertions);
}
```

---

### Pattern 4: Result Reporting with Evidence Chain

**Description**: Structured validation results with hierarchical evidence

**Benefits**:
- **Structured errors**: Easily converted to OperationOutcome
- **Context preservation**: Location + definition path included
- **Aggregation**: Combine multiple results

**Applicability with ResourceJsonNode**:

```csharp
// Result model (Ignixa.Validation.Models)
public record ValidationResult
{
    public ValidationOutcome Outcome { get; init; } // Success/Warning/Error
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();

    public bool IsSuccess => Outcome == ValidationOutcome.Success;

    public static ValidationResult Success { get; } = new() { Outcome = ValidationOutcome.Success };

    public static ValidationResult Failure(string message, string? issueCode = null, string? location = null)
    {
        return new ValidationResult
        {
            Outcome = ValidationOutcome.Error,
            Issues = new[]
            {
                new ValidationIssue
                {
                    Severity = IssueSeverity.Error,
                    Code = issueCode ?? "validation-failed",
                    Message = message,
                    Location = location
                }
            }
        };
    }

    public static ValidationResult Combine(IEnumerable<ValidationResult> results)
    {
        var resultsList = results.ToList();
        if (!resultsList.Any()) return Success;

        var worstOutcome = resultsList.Max(r => r.Outcome);
        var allIssues = resultsList.SelectMany(r => r.Issues).ToList();

        return new ValidationResult
        {
            Outcome = worstOutcome,
            Issues = allIssues
        };
    }

    // Convert to OperationOutcome
    public OperationOutcome ToOperationOutcome()
    {
        var outcome = new OperationOutcome();

        foreach (var issue in Issues)
        {
            outcome.Issue.Add(new OperationOutcome.IssueComponent
            {
                Severity = issue.Severity,
                Code = OperationOutcome.IssueType.Invariant,
                Diagnostics = issue.Message,
                Location = issue.Location != null ? new[] { issue.Location } : null,
                Details = new CodeableConcept
                {
                    Text = issue.Code
                }
            });
        }

        return outcome;
    }
}

public record ValidationIssue
{
    public IssueSeverity Severity { get; init; } = IssueSeverity.Error;
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Location { get; init; }
    public string? DefinitionPath { get; init; }
}

public enum ValidationOutcome
{
    Success = 0,
    Warning = 1,
    Error = 2
}
```

**Usage with State**:

```csharp
public class FhirPathInvariantAssertion : IValidationAssertion
{
    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        var result = _compiled.Evaluate(sourceNode);

        if (!result.IsTrue())
        {
            return new ValidationResult
            {
                Outcome = ValidationOutcome.Error,
                Issues = new[]
                {
                    new ValidationIssue
                    {
                        Severity = IssueSeverity.Error,
                        Code = _key,
                        Message = $"Constraint '{_key}' failed: {_humanDescription}",
                        Location = state.Location.InstancePath, // From state
                        DefinitionPath = state.Location.DefinitionPath // From state
                    }
                }
            };
        }

        return ValidationResult.Success;
    }
}
```

---

### Pattern 5: Early-Exit Optimization with Shortcut Validators

**Description**: Run cheap validators first, skip expensive ones on failure

**Benefits**:
- **Performance**: Fail fast on type mismatches
- **Reduced CPU**: Skip FHIRPath/terminology checks when type is wrong
- **Configurable**: Decide which validators are shortcuts

**Applicability with ResourceJsonNode**:

```csharp
public class ElementValidationSchema
{
    public IReadOnlyList<IValidationAssertion> ShortcutAssertions { get; }
    public IReadOnlyList<IValidationAssertion> RegularAssertions { get; }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        // Phase 1: Run shortcut validators (type checks, basic structure)
        if (ShortcutAssertions.Any())
        {
            var shortcutResults = ShortcutAssertions
                .Select(a => a.Validate(node, settings, state))
                .ToList();

            var combinedShortcut = ValidationResult.Combine(shortcutResults);

            // Early exit: if shortcut validators fail, skip rest
            if (!combinedShortcut.IsSuccess)
                return combinedShortcut;
        }

        // Phase 2: Run regular validators (invariants, bindings, etc.)
        var regularResults = RegularAssertions
            .Select(a => a.Validate(node, settings, state))
            .ToList();

        return ValidationResult.Combine(regularResults);
    }
}

// Identify shortcut validators during schema build
public static ElementValidationSchema FromElementDefinition(IElementDefinitionSummary element)
{
    var shortcuts = new List<IValidationAssertion>();
    var regular = new List<IValidationAssertion>();

    // Type checking is always a shortcut
    if (element.Type.Any())
        shortcuts.Add(new TypeAssertion(element.Type));

    // Cardinality for required elements (cheap check)
    if (element.Min.HasValue && element.Min.Value > 0)
        shortcuts.Add(new CardinalityAssertion(element.Min, null));

    // Everything else is regular
    foreach (var constraint in element.Constraint)
        regular.Add(new FhirPathInvariantAssertion(...));

    if (element.Binding != null)
        regular.Add(new BindingAssertion(...));

    return new ElementValidationSchema(element.Path, shortcuts, regular);
}
```

---

### Pattern 6: Terminology Service Integration

**Description**: Async terminology validation with graceful degradation

**Benefits**:
- **Non-blocking**: Async terminology lookups don't block validation
- **Fault tolerance**: Configurable handling of terminology service failures
- **Extensible**: Pluggable terminology service implementations

**Applicability with ResourceJsonNode**:

```csharp
// Interface (Ignixa.Validation.Abstractions or Ignixa.Domain)
public interface ITerminologyService
{
    Task<TerminologyValidationResult> ValidateCodeAsync(
        string system,
        string code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken);
}

// Result model
public record TerminologyValidationResult
{
    public bool IsValid { get; init; }
    public string? Message { get; init; }
    public string? Display { get; init; }
}

// Binding assertion with terminology validation
public class BindingAssertion : IValidationAssertion
{
    private readonly string _valueSetUrl;
    private readonly BindingStrength _strength;

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        // Only validate required bindings (optimization)
        if (_strength != BindingStrength.Required)
            return ValidationResult.Success;

        // Extract code from node
        var code = ExtractCode(node);
        if (code == null)
            return ValidationResult.Success; // No code to validate

        try
        {
            // Call terminology service (async)
            var result = TaskHelper.Await(() =>
                settings.TerminologyService.ValidateCodeAsync(
                    code.System,
                    code.Code,
                    code.Display,
                    _valueSetUrl,
                    CancellationToken.None
                ));

            if (!result.IsValid)
            {
                return ValidationResult.Failure(
                    result.Message ?? $"Code '{code.Code}' is not valid for ValueSet '{_valueSetUrl}'",
                    issueCode: "terminology-validation-failed",
                    location: state.Location.InstancePath
                );
            }
        }
        catch (Exception ex)
        {
            // Graceful degradation: terminology service failure
            var severity = settings.TerminologyFailureMode == TerminologyFailureMode.Error
                ? ValidationOutcome.Error
                : ValidationOutcome.Warning;

            return new ValidationResult
            {
                Outcome = severity,
                Issues = new[]
                {
                    new ValidationIssue
                    {
                        Severity = severity == ValidationOutcome.Error ? IssueSeverity.Error : IssueSeverity.Warning,
                        Code = "terminology-service-unavailable",
                        Message = $"Terminology service failed: {ex.Message}",
                        Location = state.Location.InstancePath
                    }
                }
            };
        }

        return ValidationResult.Success;
    }

    private (string System, string Code, string? Display)? ExtractCode(JsonNode node)
    {
        // Handle Coding, CodeableConcept, Code, etc.
        // ...
    }
}

// Settings
public class ValidationSettings
{
    public ITerminologyService TerminologyService { get; set; } = new NoOpTerminologyService();
    public TerminologyFailureMode TerminologyFailureMode { get; set; } = TerminologyFailureMode.Warning;
}

public enum TerminologyFailureMode
{
    Warning,
    Error
}
```

---

### Pattern 7: Three-Tier Validation Pipeline

**Description**: Fast → Spec → Profile validation stages with progressive depth

**Benefits**:
- **Configurable depth**: Choose validation level based on use case
- **Performance**: Skip expensive checks when not needed
- **Clear separation**: Each tier has specific responsibility

**Applicability with ResourceJsonNode**:

```csharp
// Validation service interface
public interface IFhirValidationService
{
    Task<ValidationResult> ValidateAsync(
        ResourceJsonNode resource,
        ValidationTier tier = ValidationTier.Spec,
        string[]? profiles = null,
        CancellationToken cancellationToken = default);
}

public enum ValidationTier
{
    Fast,    // JSON syntax + required fields only
    Spec,    // + FHIR spec constraints (invariants, cardinality, types)
    Profile  // + Profile constraints (custom invariants, slicing, bindings)
}

// Implementation
public class FhirValidationService : IFhirValidationService
{
    private readonly IValidationSchemaResolver _schemaResolver;
    private readonly ITerminologyService _terminologyService;

    public async Task<ValidationResult> ValidateAsync(
        ResourceJsonNode resource,
        ValidationTier tier,
        string[]? profiles,
        CancellationToken cancellationToken)
    {
        var results = new List<ValidationResult>();
        var state = new ValidationState().WithInstance(resource.ResourceType, resource.Id);

        // Tier 1: Fast validation (always run)
        var fastResult = ValidateFast(resource, state);
        results.Add(fastResult);
        if (!fastResult.IsSuccess) return ValidationResult.Combine(results);

        // Tier 2: Spec validation
        if (tier >= ValidationTier.Spec)
        {
            var baseSchemaUrl = $"http://hl7.org/fhir/StructureDefinition/{resource.ResourceType}";
            var baseSchema = _schemaResolver.GetSchema(new Canonical(baseSchemaUrl));
            if (baseSchema != null)
            {
                var specResult = baseSchema.Validate(resource.MutableNode, settings, state);
                results.Add(specResult);
            }
        }

        // Tier 3: Profile validation
        if (tier >= ValidationTier.Profile && profiles != null)
        {
            foreach (var profileUrl in profiles)
            {
                var profileSchema = _schemaResolver.GetSchema(new Canonical(profileUrl));
                if (profileSchema != null)
                {
                    var profileResult = profileSchema.Validate(resource.MutableNode, settings, state);
                    results.Add(profileResult);
                }
            }
        }

        return ValidationResult.Combine(results);
    }

    private ValidationResult ValidateFast(ResourceJsonNode resource, ValidationState state)
    {
        var assertions = new List<IValidationAssertion>
        {
            new RequiredFieldAssertion("resourceType"),
            new JsonStructureAssertion() // Check for valid JSON structure
        };

        var schema = new ElementValidationSchema(resource.ResourceType, assertions);
        return schema.Validate(resource.MutableNode, new ValidationSettings(), state);
    }
}
```

---

### Pattern 8: Slice Validation with Discriminators

**Description**: Bucket-based slicing with discriminator path matching

**Benefits**:
- **Correct FHIR slicing**: Matches spec behavior
- **Ordered slicing support**: Track slice order
- **Default bucket**: Handle unmatched elements

**Applicability with ResourceJsonNode**:

```csharp
public class SliceAssertion : IValidationAssertion
{
    public record SliceDefinition(
        string Name,
        IValidationAssertion Discriminator,
        ElementValidationSchema Schema
    );

    public IReadOnlyList<SliceDefinition> Slices { get; }
    public ElementValidationSchema DefaultSchema { get; }
    public bool Ordered { get; }
    public bool DefaultAtEnd { get; }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        if (node is not JsonArray array)
            return ValidationResult.Success;

        var buckets = Slices.ToDictionary(s => s.Name, s => new List<JsonNode>());
        var defaultBucket = new List<JsonNode>();
        var lastMatchedSliceIndex = -1;
        var results = new List<ValidationResult>();

        for (int i = 0; i < array.Count; i++)
        {
            var element = array[i];
            bool matched = false;

            for (int sliceIndex = 0; sliceIndex < Slices.Count; sliceIndex++)
            {
                var slice = Slices[sliceIndex];
                var discriminatorResult = slice.Discriminator.Validate(element, settings, state);

                if (discriminatorResult.IsSuccess)
                {
                    // Check ordering
                    if (Ordered && sliceIndex < lastMatchedSliceIndex)
                    {
                        results.Add(ValidationResult.Failure(
                            $"Element at index {i} matches slice '{slice.Name}', but appears out of order",
                            location: $"{state.Location.InstancePath}[{i}]"
                        ));
                    }

                    buckets[slice.Name].Add(element);
                    lastMatchedSliceIndex = sliceIndex;
                    matched = true;
                    break; // Single match (unless MultiCase)
                }
            }

            if (!matched)
            {
                defaultBucket.Add(element);
            }
        }

        // Validate each bucket against its slice schema
        foreach (var slice in Slices)
        {
            var bucketArray = new JsonArray(buckets[slice.Name].ToArray());
            var sliceState = state.WithLocation($"{state.Location.InstancePath}:{slice.Name}", null);
            var sliceResult = slice.Schema.Validate(bucketArray, settings, sliceState);
            results.Add(sliceResult);
        }

        // Validate default bucket
        if (defaultBucket.Any())
        {
            var defaultArray = new JsonArray(defaultBucket.ToArray());
            var defaultState = state.WithLocation($"{state.Location.InstancePath}:@default", null);
            var defaultResult = DefaultSchema.Validate(defaultArray, settings, defaultState);
            results.Add(defaultResult);
        }

        return ValidationResult.Combine(results);
    }
}

// Discriminator implementation
public class DiscriminatorAssertion : IValidationAssertion
{
    private readonly string _path; // FHIRPath to discriminator element
    private readonly object _expectedValue;

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        // Evaluate FHIRPath to extract discriminator value
        var sourceNode = JsonNodeSourceNode.FromJsonNode(node);
        var result = FhirPathEvaluator.Evaluate(sourceNode, _path);

        if (result.Any() && result.First().Value?.Equals(_expectedValue) == true)
            return ValidationResult.Success;

        return ValidationResult.Failure("Discriminator does not match");
    }
}
```

---

## Integration Considerations

### 1. Mapping to ResourceJsonNode Architecture

**Challenge**: Firely uses `ITypedElement` (SDK), we use `ResourceJsonNode` (JsonNode-based).

**Solution**: Adapter pattern between JsonNode and validation abstractions.

```csharp
// Adapter: JsonNode → ISourceNode (for FHIRPath)
public class JsonNodeSourceNode : ISourceNode
{
    private readonly JsonNode _node;
    private readonly string _name;

    public static JsonNodeSourceNode FromJsonNode(JsonNode node, string name = "root")
    {
        return new JsonNodeSourceNode(node, name);
    }

    public string Name => _name;
    public string? Value => _node is JsonValue value ? value.ToString() : null;

    public IEnumerable<ISourceNode> Children(string? name = null)
    {
        if (_node is JsonObject obj)
        {
            foreach (var prop in obj)
            {
                if (name == null || prop.Key == name)
                    yield return new JsonNodeSourceNode(prop.Value, prop.Key);
            }
        }
        else if (_node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                yield return new JsonNodeSourceNode(array[i], $"[{i}]");
            }
        }
    }
}

// Direct validation on ResourceJsonNode
public class ResourceValidator : IValidator
{
    public ValidationResult Validate(ResourceJsonNode resource, ValidationSettings settings)
    {
        // Use MutableNode property for validation
        var schema = _schemaResolver.GetSchema(GetCanonicalUrl(resource.ResourceType));
        return schema.Validate(resource.MutableNode, settings, new ValidationState());
    }
}
```

### 2. Leveraging Ignixa.FhirPath

**Current**: `Ignixa.FhirPath.Evaluation.FhirPathEvaluator` with custom ITypedElement.

**Integration**:

```csharp
public class FhirPathInvariantAssertion : IValidationAssertion
{
    private readonly FhirPathExpression _compiled;

    public FhirPathInvariantAssertion(string expression)
    {
        // Use Ignixa.FhirPath instead of SDK's compiler
        _compiled = FhirPathEvaluator.Compile(expression);
    }

    public ValidationResult Validate(JsonNode node, ValidationSettings settings, ValidationState state)
    {
        // Convert JsonNode → ISourceNode
        var sourceNode = JsonNodeSourceNode.FromJsonNode(node);

        // Evaluate using Ignixa.FhirPath
        var result = _compiled.Evaluate(sourceNode);

        return result.IsTrue()
            ? ValidationResult.Success
            : ValidationResult.Failure($"Invariant failed: {_humanDescription}");
    }
}
```

### 3. Using Ignixa.Specification (Generated Structure Providers)

**Current**: Generated `R4StructureDefinitionProvider` provides schema metadata.

**Integration**:

```csharp
public class StructureDefinitionSchemaBuilder
{
    private readonly IStructureDefinitionSummaryProvider _provider;

    public ElementValidationSchema BuildSchema(string canonicalUrl)
    {
        var sd = _provider.Provide(canonicalUrl);
        if (sd == null) return null;

        var assertions = new List<IValidationAssertion>();

        // Build from root element
        var root = sd.GetElements().First();

        // Cardinality
        if (root.Min.HasValue || root.Max.HasValue)
            assertions.Add(new CardinalityAssertion(root.Min, root.Max));

        // Constraints
        foreach (var constraint in root.Constraint)
        {
            assertions.Add(new FhirPathInvariantAssertion(
                constraint.Key,
                constraint.Expression,
                constraint.Human
            ));
        }

        // Child elements (recursive)
        foreach (var child in sd.GetElements().Skip(1))
        {
            var childSchema = BuildElementSchema(child);
            assertions.Add(new ChildElementAssertion(child.ElementName, childSchema));
        }

        return new ElementValidationSchema(root.ElementName, assertions);
    }
}
```

### 4. Multi-Tenant Validation

**Challenge**: Validation settings may differ per tenant (e.g., different terminology servers).

**Solution**: Tenant-aware ValidationSettings factory.

```csharp
public interface IValidationSettingsFactory
{
    ValidationSettings CreateSettings(int tenantId);
}

public class TenantValidationSettingsFactory : IValidationSettingsFactory
{
    private readonly ITenantConfigurationStore _tenantStore;
    private readonly ConcurrentDictionary<int, ValidationSettings> _cache = new();

    public ValidationSettings CreateSettings(int tenantId)
    {
        return _cache.GetOrAdd(tenantId, tid =>
        {
            var config = _tenantStore.GetTenantConfiguration(tid);

            return new ValidationSettings
            {
                SchemaResolver = new CachedValidationSchemaResolver(...),
                TerminologyService = CreateTerminologyService(config.TerminologyEndpoint),
                TerminologyFailureMode = config.StrictTerminology
                    ? TerminologyFailureMode.Error
                    : TerminologyFailureMode.Warning
            };
        });
    }
}

// Usage in handler
public class CreateOrUpdateHandler : IRequestHandler<CreateOrUpdateCommand, ResourceWrapper>
{
    private readonly IFhirValidationService _validationService;
    private readonly IValidationSettingsFactory _settingsFactory;

    public async Task<ResourceWrapper> HandleAsync(CreateOrUpdateCommand request, CancellationToken ct)
    {
        // Get tenant-specific validation settings
        var settings = _settingsFactory.CreateSettings(request.TenantId);

        // Validate
        var result = await _validationService.ValidateAsync(
            request.Resource,
            ValidationTier.Spec,
            profiles: request.Resource.Meta.Profile,
            ct
        );

        if (!result.IsSuccess)
            throw new FhirValidationException(result.ToOperationOutcome());

        // Continue with persistence
    }
}
```

---

## Next Steps for Implementation

1. **Phase 1: Core Abstractions** (Week 1-2)
   - Define `IValidationAssertion`, `ValidationResult`, `ValidationState`
   - Implement `IValidationSchemaResolver` interface
   - Create adapter: `JsonNodeSourceNode` for FHIRPath integration

2. **Phase 2: Basic Validators** (Week 3-4)
   - `CardinalityAssertion`
   - `TypeAssertion`
   - `FhirPathInvariantAssertion` (using Ignixa.FhirPath)
   - `RequiredFieldAssertion`

3. **Phase 3: Schema Building** (Week 5-6)
   - `StructureDefinitionSchemaBuilder` (from Ignixa.Specification)
   - `CachedValidationSchemaResolver`
   - `ElementValidationSchema` composition

4. **Phase 4: Advanced Validators** (Week 7-8)
   - `BindingAssertion` (with ITerminologyService)
   - `SliceAssertion` (discriminators, buckets)
   - `FixedValueAssertion`, `PatternAssertion`

5. **Phase 5: Integration** (Week 9-10)
   - Three-tier validation service (`Fast`/`Spec`/`Profile`)
   - Multi-tenant settings factory
   - OperationOutcome generation
   - Performance benchmarking

6. **Phase 6: Testing & Documentation** (Week 11-12)
   - Unit tests for each validator
   - Integration tests with real StructureDefinitions
   - Performance tests (compare to Firely)
   - ADR updates

---

## Conclusion

The Firely validator demonstrates a mature, production-ready architecture with clear patterns that map well to Ignixa's JsonNode-based approach. The key architectural pillars to adopt are:

1. **Schema compilation with caching** - Significant performance gain
2. **Immutable state threading** - Thread-safe, traceable validation
3. **Composable assertion model** - Testable, maintainable validators
4. **Structured result reporting** - Clean OperationOutcome generation
5. **Async terminology integration** - Non-blocking, fault-tolerant

HAPI's resources provide canonical examples of FHIR constraint encoding, useful for:
- **Test fixtures**: Validate our schemas against canonical profiles
- **Schematron patterns**: Understand complex constraints (e.g., Bundle uniqueness)
- **Extension examples**: Guide Extension validation implementation

By combining Firely's architecture with our existing infrastructure (ResourceJsonNode, Ignixa.FhirPath, Ignixa.Specification), we can build a high-performance, maintainable validation system that supports all three tiers (Fast/Spec/Profile) while remaining flexible for future enhancements.

---

**End of Report**
