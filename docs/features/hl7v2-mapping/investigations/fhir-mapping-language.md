# Investigation: FHIR Mapping Language

**Feature**: hl7v2-mapping
**Status**: Viable
**Created**: 2026-07-09

## Approach

Use FHIR Mapping Language (FML) as the structural transformation layer between FHIR resources and typed HL7 v2 logical models.

The conversion pipeline would have three explicit stages:

1. Parse inbound ER7 into a typed logical tree, preferably through an NHapi-backed Ignixa adapter, for example `Hl7v2AdtA01` with `MSH`, `PID`, `PV1`, and datatype children such as `Cx` and `Xpn`.
2. Execute a unidirectional StructureMap/FML map from that typed HL7 v2 logical model to FHIR resources such as `Patient`.
3. For outbound messages, execute a separate FHIR-to-HL7v2 logical model map, then serialize the resulting logical tree back to ER7.

This keeps FML responsible for what it is good at: navigating named children, creating target structures, invoking transforms, and translating codes. It does not ask FML to parse or emit delimiter-based HL7 v2 text. The preferred parser/serializer candidate is documented in [NHapi Logical Model Adapter](nhapi-logical-model-adapter.md).

## Tradeoffs

| Pros | Cons |
|------|------|
| Uses the existing `Ignixa.FhirMappingLanguage` parser, ConceptMap support, transform functions, and StructureMap model rather than inventing a custom mapping DSL | Requires an HL7 v2 adapter layer before production conversion is possible |
| Keeps mapping rules declarative and packageable as StructureMap resources | Requires logical model definitions for the HL7 v2 message families and datatypes we support |
| Supports separate inbound and outbound maps, matching the StructureMap rule that reverse maps are not implied | Round-trip fidelity depends on the logical model retaining v2 fields that do not have direct FHIR equivalents |
| Code translation can use inline ConceptMaps or external ConceptMap resources | Complex v2 repetition, escape, null, and optionality semantics still need adapter-level tests |
| Preserves the existing layered architecture by keeping parsing/serialization outside Application handlers | Map execution validates transformations, not raw v2 conformance |

## Alignment

- [x] Follows architectural layering rules
- [x] Developer Experience (works with minimal setup)
- [x] Specification compliance (if applicable)
- [x] Consistent with existing patterns

## Evidence

### FHIR specification fit

The FHIR Mapping Language specification describes maps as transformations between directed acyclic graphs with named children. It explicitly does not require formal declarations or strong typing, although type-aware features become available when StructureDefinitions are present. It also lists embedded ConceptMaps, structure references, imports, constants, groups, and transformation rules as first-class map parts.

The StructureMap resource specification describes StructureMap as detailed rules that can automate conversion between structures. It states that source and target models are normally StructureDefinitions or logical models, and that the mapping language could be used to define a map from an HL7 v2 message to another model. It also states that maps are unidirectional and no reverse map is implied.

Relevant official references:

- https://build.fhir.org/mapping-language.html
- https://build.fhir.org/mapping-tutorial.html
- https://build.fhir.org/structuremap.html

### Ignixa implementation fit

Local code already has the core pieces needed for this approach:

- `src\Core\Ignixa.FhirMappingLanguage\Parser\MappingParser.cs` parses FML text into `MapExpression`.
- `src\Core\Ignixa.FhirMappingLanguage\Parser\MappingGrammar.cs` supports `conceptmap`, `uses`, `group`, source/target rules, transform calls, literals, and qualified identifiers.
- `src\Core\Ignixa.FhirMappingLanguage\Evaluation\MappingEvaluator.cs` executes parsed maps.
- `src\Core\Ignixa.FhirMappingLanguage\Mutator\JsonNodeMutator.cs` supports target resource mutation for transformations.
- `test\Ignixa.Application.Tests\Features\Transform\TransformResourceHandlerTests.cs` verifies `$transform` can copy and enrich FHIR resources.
- Existing StructureMap investigations document operation integration and mutation behavior under `docs\features\structuremap\investigations\`.

### Executable examples

Added executable parser examples in:

`test\Ignixa.FhirMappingLanguage.Tests\Integration\Hl7v2MappingLanguageExamplesTests.cs`

The tests cover two representative maps:

1. `Hl7v2AdtA01ToPatient`: maps a typed `Hl7v2AdtA01` logical model into FHIR `Patient`, including PID identifier, name, birth date, and administrative sex translation.
2. `PatientToHl7v2AdtA08`: maps FHIR `Patient` into a typed `Hl7v2AdtA08` logical model, including MSH message type fields and PID patient fields.

The examples intentionally test parser and AST shape, not production conversion execution. That is the right boundary until an HL7 v2 adapter exists to parse ER7 into typed trees and serialize typed trees back to ER7.

Focused verification command:

```powershell
dotnet test test\Ignixa.FhirMappingLanguage.Tests\Ignixa.FhirMappingLanguage.Tests.csproj --framework net9.0 --filter Hl7v2MappingLanguageExamplesTests --verbosity minimal
```

Result:

```text
Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

### Other approaches worth investigating

1. **Raw ER7 directly in FML**: likely not viable. FML expects named child trees; raw `MSH|^~\&|...` text needs delimiter parsing, escape handling, repetitions, components, subcomponents, and segment ordering outside the map language.
2. **External HL7 v2 parser adapter**: required for production. The recommended version of this approach is the NHapi-backed adapter described in [NHapi Logical Model Adapter](nhapi-logical-model-adapter.md).
3. **Template-only converter**: possible for narrow outbound messages, but it duplicates mapping semantics and would compete with StructureMap rather than reuse it.

## Verdict

FHIR Mapping Language is viable for converting to and from HL7 v2 only when HL7 v2 is represented as a typed logical model. It should not be used as the raw ER7 parser or serializer.

Recommended next step: evaluate an implementation plan for the NHapi-backed adapter boundary, including logical model shape, supported message families, ER7 parsing strategy, and outbound serialization fidelity.
