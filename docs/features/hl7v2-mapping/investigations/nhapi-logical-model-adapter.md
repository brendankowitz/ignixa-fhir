# Investigation: NHapi Logical Model Adapter

**Feature**: hl7v2-mapping
**Status**: Viable
**Created**: 2026-07-09

## Approach

Use NHapi as the HL7 v2 parse/encode dependency, but do not expose NHapi message classes directly to FHIR Mapping Language.

NHapi should sit at the format boundary:

```text
Inbound:
ER7 text -> NHapi parser -> Ignixa HL7v2 logical tree -> FML/StructureMap -> FHIR resources

Outbound:
FHIR resources -> FML/StructureMap -> Ignixa HL7v2 logical tree -> NHapi encoder -> ER7 text
```

The Ignixa adapter would translate between NHapi's HL7 v2 object model and a stable logical tree shaped for mapping. For example, NHapi can parse `ADT_A01` and expose `PID`, `XPN`, `CX`, and related datatype objects. Ignixa should project those into logical paths used by maps:

```text
msg.PID.patientIdentifierList[0].idNumber
msg.PID.patientIdentifierList[0].assigningAuthority.namespaceId
msg.PID.patientName[0].familyName.surname
msg.PID.patientName[0].givenName
msg.PID.dateTimeOfBirth
msg.PID.administrativeSex
```

FHIR Mapping Language then maps those logical paths to FHIR paths. The maps remain independent of NHapi's generated class names, method names, version-specific package layout, and object mutation quirks.

## Tradeoffs

| Pros | Cons |
|------|------|
| Reuses an established .NET HL7 v2 parser/encoder instead of building ER7 support from scratch | Adds a new third-party dependency with license and maintenance considerations |
| NHapi understands HL7 v2 versions, message structures, segments, datatypes, repetitions, and ER7 encoding rules | NHapi's object model is not the same shape we want FML authors to map against |
| Keeps raw delimiter parsing and serialization outside the FML engine | Requires adapter code to project NHapi objects into Ignixa's mapping tree |
| Allows inbound and outbound conversion to share one adapter boundary while using separate maps | Outbound map output must be converted back into NHapi objects carefully to preserve segment order and repetitions |
| Lets Ignixa define stable logical names even if NHapi internals or selected HL7 versions differ | Custom segments and site-specific Z-segments still need extension points |

## Alignment

- [x] Follows architectural layering rules
- [x] Developer Experience (works with minimal setup)
- [x] Specification compliance (if applicable)
- [x] Consistent with existing patterns

## Evidence

### NHapi capabilities

NHapi describes itself as a .NET port of the Java HAPI HL7 v2 project. Its documented purpose is to provide an HL7 2.x object model that can parse and encode HL7 2.x data to and from pipe-delimited ER7 or XML formats.

The NHapi README and NuGet page list support for HL7 versions 2.1, 2.2, 2.3, 2.3.1, 2.4, 2.5, 2.5.1, 2.6, 2.7, 2.7.1, 2.8, and 2.8.1. The package targets .NET Framework 3.5 and `netstandard2.0`, which makes it consumable from Ignixa's .NET projects.

References:

- https://github.com/nHapiNET/nHapi
- https://www.nuget.org/packages/nhapi

### Why not map NHapi directly

Directly binding FML maps to NHapi classes would leak implementation details into mapping artifacts. A map author should not need to know whether a value comes from `GetPatientName(0)`, a generated property, a version-specific namespace, or an NHapi datatype wrapper.

FML needs named child navigation. An Ignixa projection layer can provide stable logical names and hide:

- NHapi generated class differences across HL7 versions.
- Repetition access patterns.
- Optional segment handling.
- Primitive wrapper extraction.
- Custom segment and Z-field extension handling.
- Outbound object creation and segment ordering.

This also keeps the dependency reversible. If NHapi is later replaced, only the adapter changes; StructureMaps and FML tests can stay stable.

### Proposed adapter responsibilities

The adapter should own:

1. Detecting or accepting the HL7 v2 version and message type.
2. Parsing inbound ER7 with NHapi.
3. Projecting NHapi messages to an Ignixa logical tree that implements the element navigation contract needed by FML/FHIRPath.
4. Creating outbound NHapi message instances from an Ignixa logical tree.
5. Encoding outbound messages as ER7.
6. Handling custom Z-segments through configured extension nodes.
7. Reporting parse, model, and encode errors explicitly rather than silently dropping unsupported fields.

The adapter should not own:

1. Patient, Encounter, Observation, or other FHIR semantic mapping rules.
2. Terminology translation such as PID-8 to `Patient.gender`.
3. FHIR resource persistence.
4. FHIR validation.

Those responsibilities stay in FML/StructureMap, terminology services, repository layers, and validation layers respectively.

### Relationship to existing executable examples

The existing examples in `test\Ignixa.FhirMappingLanguage.Tests\Integration\Hl7v2MappingLanguageExamplesTests.cs` already assume stable logical paths such as `msg.PID.patientIdentifierList` and `msg.PID.patientName`.

This investigation makes NHapi the recommended source of those logical trees. The tests still should not reference NHapi directly until we add adapter code. Parser-level tests define the expected map shape; adapter tests should later prove NHapi `ADT_A01` messages project into that shape.

### Prototype structure

The initial runnable prototype uses a new isolated core project so NHapi does not become a dependency of `Ignixa.FhirMappingLanguage`:

```text
src\Core\Ignixa.Hl7v2\
  Ignixa.Hl7v2.csproj
  Adapters\
    NhapiAdtA01LogicalModelAdapter.cs
  LogicalModel\
    Hl7v2Element.cs
    Hl7v2LogicalPath.cs

test\Ignixa.Hl7v2.Tests\
  Ignixa.Hl7v2.Tests.csproj
  NhapiAdtA01LogicalModelAdapterTests.cs
```

Current prototype behavior:

1. `NhapiAdtA01LogicalModelAdapter` uses NHapi `PipeParser` to parse ADT^A01 ER7 text.
2. The adapter projects selected PID fields into `Hl7v2Element`, an Ignixa `IElement` implementation.
3. `Hl7v2LogicalPath` resolves FML-style paths such as `msg.PID.patientIdentifierList[0].idNumber`.
4. Tests prove PID-3, PID-5, PID-7, and PID-8 are available through the stable logical paths expected by the FML examples.

### Candidate follow-up tests

When moving from investigation to implementation, start with adapter-level tests:

1. Parse an ADT^A01 ER7 message with NHapi and project `PID-3`, `PID-5`, `PID-7`, and `PID-8` to the logical paths used by `Hl7v2AdtA01ToPatient`.
2. Populate the logical tree for an ADT^A08 outbound message and verify NHapi encodes `MSH-9`, `PID-3`, `PID-5`, `PID-7`, and `PID-8`.
3. Verify repetitions: multiple `PID-3` identifiers and multiple `PID-5` names preserve order.
4. Verify unsupported/custom segments fail with a clear diagnostic unless configured as extension nodes.

## Verdict

NHapi is the preferred foundation for the HL7 v2 format boundary. Ignixa should use it for ER7 parsing and encoding, then expose a stable Ignixa logical model adapter to FHIR Mapping Language.

Do not bind StructureMaps directly to NHapi classes. The extra adapter layer is the seam that keeps maps portable, testable, and independent from the parser dependency.
