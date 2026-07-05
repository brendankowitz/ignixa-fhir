# Feature: Codegen

**Status**: Exploring
**Created**: 2026-07-05

## Problem Statement

This repo's build-time code generation (`codegen/Ignixa.Specification.Generators/`) — which produces the per-FHIR-version core schema, reference metadata, value set provider, search parameter, compartment, and code system artifacts under `src/Core/Ignixa.Specification/Generated/` and `src/Core/Ignixa.Search/Generated/` — depends on a vendored third-party tool, [fhir-codegen](https://github.com/FHIR/fhir-codegen) (git submodule `third-party/fhir-codegen`), to parse official FHIR packages into a `DefinitionCollection` our own `ILanguage` implementations then read from.

The FHIR R6 ballot2→ballot4 upgrade (`docs/adr/adr-2607-fhir-r6-ballot4-upgrade.md`) found this dependency is a real, recurring source of friction:

- The submodule's parsing pipeline is **R5-canonical throughout** — every FHIR version's package content gets parsed into R5 concrete POCO types (Firely SDK `Hl7.Fhir.Model.*`), with converters bridging STU3/R4/R4B up to R5. **There is no R6→R5 converter and no native R6 model support**, so R6 content parses only as far as R5's shape tolerates it.
- The vendored Firely SDK version (`Hl7.Fhir.R5` 5.13.1) bakes several FHIR code enums (`VersionIndependentResourceTypesAll`, `SearchParamType`, etc.) as fixed C# enum members at the SDK's release time. Any FHIR ballot that adds a new code value to one of these enums (confirmed 4 separate times during the ballot4 upgrade: `DeviceAlert`, `SearchParamType`'s `resource` value, and by extension any future new code) causes an `InvalidCastException` the first time our generator code touches that property, in a codebase we don't control.
- Fixing this today means either (a) patching a personal fork of the vendored submodule (temporary, not upstreamed, a CI-availability risk — see the ADR), or (b) reactively patching our own generator code's property access one crash at a time as each new ballot exercises a previously-untouched enum-backed property.
- This is not a one-time cost: R6 is pre-normative and will keep changing shape across ballots until it stabilizes, so this exact class of friction is expected to recur on every future ballot bump absent a structural change.

**What this feature investigates:** whether to absorb the FHIR-package-parsing responsibility currently delegated to `fhir-codegen` into code we own — reading each package's raw JSON directly (e.g. via `System.Text.Json`) for exactly the fields our 9 generator languages actually need (element definitions, search parameter fields, compartment definitions, code system concepts), with no dependency on a versioned Firely SDK's typed/enum model — with the eventual goal of dropping the `fhir-codegen` submodule dependency entirely.

## Constraints

- Must not regress any of the 5 FHIR versions currently generated (STU3, R4, R4B, R5, R6).
- Must not change the public shape/API of the generated artifacts consumed elsewhere in the repo (`R{Version}CoreSchemaProvider`, `R{Version}ReferenceMetadata`, `R{Version}ValueSetProvider`, `R{Version}SearchParameterDefinitions`, `R{Version}CompartmentDefinitions`, `R{Version}CodeSystemMappings`) unless a deliberate, separately-decided migration accompanies the change.
- Any replacement must be verifiable against the same official FHIR packages already used (`hl7.fhir.{version}.core`), ideally with a way to diff old-generator-output vs new-generator-output for a known-good version (e.g. R5) before cutting over.
- Should not block future ballot bumps in the interim — this is exploratory; the current fork-based approach remains in place until a decision is made and implemented.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [absorb-parsing-drop-fhir-codegen-dependency](investigations/absorb-parsing-drop-fhir-codegen-dependency.md) | In Progress | Replace `fhir-codegen`'s FHIR-package parsing with our own JSON-based loader (mirroring the existing `ISourceNavigator`/`JsonNodeSourceNode` runtime pattern), eliminating the recurring Firely-SDK-enum-vs-current-ballot crash class and the vendored submodule dependency entirely |

## Decision

*No ADR yet - investigations in progress*
