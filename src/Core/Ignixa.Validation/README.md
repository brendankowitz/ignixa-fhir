# Ignixa.Validation

FHIR validation system with four validation depth levels.

## Architecture

**Four Validation Depth Levels** (ADR-2527):

| Depth | Checks | Use Case |
|------|--------|----------|
| **Minimal** | JSON structure, required fields | CREATE/UPDATE (blocking), bulk ingestion |
| **Compatibility** | + Cardinality, types (lenient URIs) | Microsoft FHIR Server migration |
| **Spec** | + Required terminology, absolute URIs | CREATE/UPDATE (blocking) |
| **Full** | + FHIRPath invariants, all bindings | $validate (async), compliance testing |

## Quick Start

### Profile-aware validation (IG support)

To validate a resource against an Implementation Guide profile declared in
`Resource.meta.profile`, layer the IG package's profiles + ValueSets on top
of the base spec and use `ProfileAwareValidationSchemaResolver`:

```csharp
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;

// 1. Load an IG package via Ignixa.PackageManagement.
//    (See Ignixa.PackageManagement for NpmPackageLoader + PackageExtractor.)
//    extractedResources: IReadOnlyList<ExtractedResource>

// 2. Layer the IG profiles on top of the base spec.
var baseSchema = new R4CoreSchemaProvider();
var schemaProvider = new ProfileLayeredSchemaProvider(baseSchema, extractedResources);

// 3. Layer the IG ValueSets on top of the base spec terminology.
var packageVs = new PackageValueSetSource(extractedResources);
var terminology = new InMemoryTerminologyService(
    primary: baseSchema.ValueSetProvider,
    additional: new[] { (IValueSetProvider)packageVs });

// 4. Build the resolver chain.
var inner = new StructureDefinitionSchemaResolver(schemaProvider, terminologyService: terminology);
var cached = new CachedValidationSchemaResolver(inner);
var profileAware = new ProfileAwareValidationSchemaResolver(cached);

// 5. Resolve a schema that composes base + every profile declared in meta.profile.
var element = sourceNode.ToElement(schemaProvider);
var schema = profileAware.ResolveForElement(element);

// 6. Validate.
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
var result = schema!.Validate(element, settings, new ValidationState());
```

`ProfileAwareValidationSchemaResolver` also implements `IValidationSchemaResolver`,
so it's a drop-in for any DI registration that previously bound the cached resolver.
The production `ValidationServicesRegistration` wires it automatically; consumers
that have access to the resource element should call `ResolveForElement(element)`
to pick up `meta.profile` composition.

### CLI

The `ignixa-validator` CLI exposes profile-aware validation directly:

```
ignixa-validator r4 \
  --input patient.json \
  --package hl7.fhir.us.core@6.1.0 \
  --depth spec \
  --console
```

`--package id@version` is repeatable; multiple packages are layered in declared
order (later wins on profile id collisions). Packages are cached under
`%TEMP%/ignixa-validator-package-cache`.

### Basic Validation (Recommended)

```csharp
using Ignixa.Validation.Schema;
using Ignixa.Specification.Generated;
using Ignixa.Serialization;

// 1. Get FHIR schema for your version (R4, R4B, R5, or STU3)
var fhirSchema = new R4CoreSchemaProvider();

// 2. Get the type definition for the resource you want to validate
var patientType = fhirSchema.GetTypeDefinition("Patient");

// 3. Build validation schema (automatically creates all checks from StructureDefinition)
var builder = new StructureDefinitionSchemaBuilder();
var validationSchema = builder.BuildSchema(patientType!, fhirSchema);

// 4. Parse and validate your FHIR resource
var json = "{\"resourceType\":\"Patient\",\"id\":\"123\"}";
var sourceNode = JsonSourceNodeFactory.Parse(json).ToSourceNode();
var element = sourceNode.ToElement(fhirSchema);

// 5. Choose validation depth
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
var state = new ValidationState();
var result = validationSchema.Validate(element, settings, state);

if (!result.IsValid)
{
    var operationOutcome = result.ToOperationOutcome();
    // Return operationOutcome to client
}
```

### Validating Different Resource Types

```csharp
using Ignixa.Validation.Schema;
using Ignixa.Specification.Generated;
using Ignixa.Serialization;

var fhirSchema = new R4CoreSchemaProvider();
var builder = new StructureDefinitionSchemaBuilder();

// Validate Observation
var observationType = fhirSchema.GetTypeDefinition("Observation");
var observationSchema = builder.BuildSchema(observationType!, fhirSchema);

var observationJson = "{\"resourceType\":\"Observation\",\"status\":\"final\",\"code\":{}}";
var sourceNode = JsonSourceNodeFactory.Parse(observationJson).ToSourceNode();
var result = observationSchema.Validate(sourceNode.ToElement(fhirSchema));
```

## Validation Depth Guide

### Minimal
Fastest validation for high-throughput scenarios:
```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Minimal };
```

### Compatibility
For migrating from Microsoft FHIR Server (Firely SDK validation):
```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Compatibility };
```
- Accepts relative URIs in `Coding.system` (e.g., `"internal-tags"`)
- Same checks as Spec, but more lenient for migration scenarios
- Use when running Microsoft FHIR Server E2E test suite

### Spec
Standard FHIR specification compliance:
```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
```
- Enforces absolute URIs in `Coding.system`
- Required terminology bindings
- Recommended for production API operations

### Full
Complete FHIR profile validation:
```csharp
var settings = new ValidationSettings { Depth = ValidationDepth.Full };
```
- All Spec checks plus FHIRPath invariants
- Extensible terminology bindings
- Use for compliance testing and profile conformance

## What Gets Validated

When you use `StructureDefinitionSchemaBuilder`, it automatically extracts and creates validation checks from FHIR StructureDefinition metadata. Here's what gets checked:

### Tier 1 (Fast) - Universal Checks

These checks run for every CREATE/UPDATE operation:

| Check | Purpose | When Applied |
|-------|---------|--------------|
| **JsonStructureCheck** | Validates JSON is well-formed object with resourceType | Resources only |
| **NarrativeCheck** | Validates Narrative.status and Narrative.div | Resources only |
| **CardinalityCheck** | Validates min/max element count (0..1, 1..1, 0..*, 1..*) | All elements except xhtml |
| **TypeCheck** | Validates primitive types (id, string, integer, boolean, etc.) | All primitive elements |

### Tier 2 (Spec) - Schema-Driven Checks

Additional checks extracted from StructureDefinition metadata:

| Check | Purpose                                                          | When Applied |
|-------|------------------------------------------------------------------|--------------|
| **ReferenceFormatCheck** | Validates Reference.reference format (relative/literal/url)      | Elements with type=Reference |
| **CodingStructureCheck** | Validates Coding.system + Coding.code structure                  | Elements with type=Coding or CodeableConcept |
| **ChoiceElementCheck** | Validates choice elements (only one variant present)             | Elements with name ending in [x] |
| **ExtensionStructureCheck** | Validates Extension.url is present and value/extension rules     | Elements named "extension" |
| **FixedValueCheck** | Validates element has exact fixed value                          | Elements with fixedValue constraint |
| **PatternCheck** | Validates element matches pattern constraint                     | Elements with patternValue constraint |
| **BindingCheck** | Validates coded values against built-in, required ValueSets      | Elements with binding |
| **NestedComplexTypeCheck** | Validates nested BackboneElement and complex types               | BackboneElement children |
| **UnknownPropertyCheck** | Rejects unknown/undefined properties                             | All resources |
| **FhirPathInvariantCheck** | Validates FHIRPath constraints (ele-1, dom-1, custom invariants) | Elements with constraint definitions |

### Tier 3 (Profile) - Slicing, advanced terminology

Profile validation against Implementation Guide profiles declared in
`Resource.meta.profile`. See the [Profile-aware validation](#profile-aware-validation-ig-support)
section above for the wiring.

| Check | Purpose | When Applied |
|-------|---------|--------------|
| **FhirPathInvariantCheck** | Validates FHIRPath constraints from IG profiles (e.g. CARIN-BB `EOB-inst-careTeam-practitioner`) | Profile elements with `constraint` definitions |
| **BindingCheck** | Validates coded values against IG ValueSets (e.g. US Core `us-core-birthsex`) | Profile elements with binding (requires ITerminologyService layered with `PackageValueSetSource`) |
| **CardinalityCheck** | Enforces tightened cardinality from profile elements (e.g. US Core Patient.identifier min=1) | Profile elements with stricter min/max |
| **FixedValueCheck / PatternCheck** | Enforces fixed/pattern values from profile elements | Profile elements with fixed/pattern constraints |

**Currently unsupported (out of scope)**: discriminator-based slicing
(e.g. CARIN-BB `adjudication-has-amount-type-slice`). Slice members in
the snapshot are deduped to the slice header so the validator emits the
correct base cardinality; per-slice constraints are silently ignored.
Tracked for a future iteration.

## License

MIT License - see LICENSE file in repository root
