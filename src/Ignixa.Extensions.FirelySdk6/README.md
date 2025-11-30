# Ignixa.Extensions.FirelySdk

Firely SDK interoperability extensions for Ignixa. Provides bidirectional conversion between Ignixa's modern interfaces (`IElement`, `IType`) and Firely SDK's interfaces (`ITypedElement`, `IElementDefinitionSummary`).

## Purpose

This library enables:

1. **Community adoption**: Use Ignixa core libraries with existing Firely SDK-based tools
2. **Gradual migration**: Adopt Ignixa interfaces incrementally without rewriting everything
3. **Interoperability**: Work with both ecosystems seamlessly

## Usage

### Firely → Ignixa Conversion

Convert Firely SDK types to Ignixa types to use with Ignixa libraries:

```csharp
using Ignixa.Extensions.FirelySdk;
using Hl7.Fhir.ElementModel;

// Get a Firely SDK element
ITypedElement firelyElement = ...;

// Convert to Ignixa
IElement ignixaElement = firelyElement.ToIgnixaElement();

// Use with Ignixa libraries
var validator = new IgnixaValidator();
var result = validator.Validate(ignixaElement);
```

### Ignixa → Firely Conversion

Convert Ignixa types to Firely SDK types to use with Firely tools:

```csharp
using Ignixa.Extensions.FirelySdk;
using Ignixa.Abstractions;

// Get an Ignixa element
IElement ignixaElement = ...;

// Convert to Firely SDK
ITypedElement firelyElement = ignixaElement.ToTypedElement();

// Use with Firely SDK tools
var navigator = firelyElement.ToFhirPathNavigator();
var result = navigator.Scalar("Patient.name.family");
```

### ISourceNode → IElement Conversion

Convert Firely's schema-less `ISourceNode` to Ignixa's schema-aware `IElement`:

```csharp
using Ignixa.Extensions.FirelySdk;
using Hl7.Fhir.Serialization;

// Parse JSON with Firely SDK
ISourceNode sourceNode = FhirJsonNode.Parse(json);

// Convert to schema-aware IElement using Ignixa schema
IElement element = sourceNode.ToElement(schema);

// Access type-aware properties
var instanceType = element.InstanceType;  // e.g., "Patient"
var name = element.Children("name").First();
```

### Batch Conversions

Convert collections efficiently:

```csharp
// Firely → Ignixa
IEnumerable<ITypedElement> firelyElements = ...;
IEnumerable<IElement> ignixaElements = firelyElements.ToIgnixaElements();

// Ignixa → Firely
IReadOnlyList<IElement> ignixaElements = ...;
IEnumerable<ITypedElement> firelyElements = ignixaElements.ToTypedElements();
```

## Architecture

### Adapter Classes

1. **IgnixaElementAdapter**: Wraps Firely `ITypedElement` → Implements Ignixa `IElement`
   - Lazy child materialization
   - Caches all children to avoid repeated enumeration
   - Filters by name efficiently

2. **TypedElementAdapter**: Wraps Ignixa `IElement` → Implements Firely `ITypedElement`
   - Streaming conversion (no upfront materialization)
   - Preserves Ignixa annotation system

3. **SourceNavigatorAdapter**: Wraps Firely `ISourceNode` → Implements Ignixa `ISourceNavigator`
   - Bridges the schema-less interfaces between Firely and Ignixa
   - Derives `ResourceType` from child elements
   - Forwards annotations via `IAnnotatable`

### Extension Methods

- **IgnixaExtensions**: `.ToIgnixaElement()`, `.ToIgnixaElements()` - Firely `ITypedElement` → Ignixa `IElement`
- **FirelySdkExtensions**:
  - `.ToTypedElement()`, `.ToTypedElements()` - Ignixa `IElement` → Firely `ITypedElement`
  - `.ToSourceNavigator()` - Firely `ISourceNode` → Ignixa `ISourceNavigator`
  - `.ToElement(schema)` - Firely `ISourceNode` → Ignixa `IElement` (with schema metadata)

### Smart Unwrapping

The adapters detect when an element has already been wrapped and unwrap it instead of double-wrapping:

```csharp
// No double-wrapping
IElement ignixa = ...;
ITypedElement firely = ignixa.ToTypedElement();
IElement back = firely.ToCoreElement();  // Returns original ignixa, not a wrapper!
```

## Performance Considerations

### Lazy Materialization

CoreElementAdapter (Firely → Ignixa):
- Children are materialized only on first access
- All children cached to avoid repeated Firely enumeration
- Name filtering uses cached children

### Streaming Conversion

TypedElementAdapter (Ignixa → Firely):
- No upfront materialization
- Children converted on-demand as enumerated
- Memory-efficient for large trees

## Limitations

1. **IType.Children**: Empty collection (Firely `IElementDefinitionSummary` doesn't provide child definitions)
2. **IsAbstract**: Not available in Firely's `IElementDefinitionSummary`, always returns `false`
3. **Annotations**: Firely `ITypedElement` doesn't support general annotations (only stores original element for unwrapping)

## Dependencies

- **Ignixa.Abstractions**: Modern Ignixa interfaces (IElement, IType, etc.)
- **Ignixa.Serialization**: SchemaAwareElement for ISourceNode → IElement conversion
- **Hl7.Fhir.Base**: Firely SDK base package (provides ITypedElement, ISourceNode, IElementDefinitionSummary, etc.)

> **Note**: This package uses `Hl7.Fhir.Base` instead of version-specific packages like `Hl7.Fhir.R4`, making it compatible with any FHIR version (R4, R4B, R5, STU3).

## Migration Strategy

For library authors:

1. **Accept both interfaces**: Support both `IElement` and `ITypedElement` in your APIs
   ```csharp
   public class MyValidator
   {
       public ValidationResult Validate(IElement element) { ... }
       public ValidationResult Validate(ITypedElement element) => Validate(element.ToCoreElement());
   }
   ```

2. **Prefer Ignixa internally**: Use Ignixa interfaces internally, provide Firely shims externally
   ```csharp
   // Internal: Use IElement
   private void ValidateInternal(IElement element) { ... }

   // Public: Accept ITypedElement, convert to IElement
   public void Validate(ITypedElement element) => ValidateInternal(element.ToCoreElement());
   ```

3. **Deprecate Firely methods**: Mark Firely-based methods as obsolete after a transition period
   ```csharp
   [Obsolete("Use Validate(IElement) instead")]
   public ValidationResult Validate(ITypedElement element) => Validate(element.ToCoreElement());
   ```

## Examples

### Example 1: Validate Firely Element with Ignixa Validator

```csharp
// Firely SDK: Parse JSON to ITypedElement
var firelyElement = FhirJsonNode.Parse(jsonString).ToTypedElement(modelInspector);

// Convert to Ignixa
var ignixaElement = firelyElement.ToCoreElement();

// Validate with Ignixa (uses IElement)
var validator = new IgnixaFhirValidator();
var result = validator.Validate(ignixaElement);
```

### Example 2: Use Ignixa Element with FHIRPath

```csharp
// Ignixa: Get element from custom source
var ignixaElement = myCustomSource.GetPatient("123");

// Convert to Firely SDK
var firelyElement = ignixaElement.ToTypedElement();

// Evaluate FHIRPath (Firely SDK)
var compiler = new FhirPathCompiler();
var result = compiler.Compile("Patient.name.where(use='official').family")
    .Invoke(firelyElement, EvaluationContext.CreateDefault());
```

### Example 3: Interop Between Ecosystems

```csharp
// Start with Firely
ITypedElement firelyPatient = ...;

// Convert to Ignixa for validation
var ignixaPatient = firelyPatient.ToCoreElement();
var validationResult = new IgnixaValidator().Validate(ignixaPatient);

// Convert back to Firely for FHIRPath
var firelyAgain = ignixaPatient.ToTypedElement();
var name = new FhirPathCompiler().Compile("Patient.name[0].family")
    .Scalar(firelyAgain, EvaluationContext.CreateDefault());
```

## Testing

The shims library includes comprehensive tests to ensure correct bidirectional conversion:

- Round-trip conversion (Firely → Ignixa → Firely)
- Property preservation (Name, Value, InstanceType, Location)
- Child navigation equivalence
- Annotation handling
- Performance benchmarks

## License

MIT License - See LICENSE file in repository root
