# ADR 2607: Custom TestScript Extensions for Automated Conformance Testing

## Status

Accepted

> `parametrize`, `fhirVersions`, and `fhirfakes` shipped with the TestScript engine and conformance matrix. `requiresCapability` lands with PR #292. Upstream normalization of the recognized `requiresCapability` shorthand (see "Shorthand normalization" below) is part of the follow-up work tracked in #324.

## Context

The FHIR `TestScript` resource drives the `Ignixa.TestScript` engine and the `ignixa-matrix` CLI, which runs `conformance-tests/` unattended in CI against multiple FHIR versions and publishes a conformance matrix to the docs site. TestScript's structure is fixed by the spec — the only sanctioned way to add engine behavior is extensions. Four behaviors the base R4/R5 resource cannot express turned out to be load-bearing for a fully automated pipeline:

1. **Data-driven tests** — run one test body once per input value. Native TestScript has `variable.defaultValue` but no repetition mechanism; the alternative is copy-pasting a test N times per value (the date-interval suite would be ~64 hand-maintained tests instead of 16).
2. **FHIR-version applicability per test** — one suite folder runs against R4, R4B, and R5 servers. TestScript has no per-test (or even per-script) FHIR-version applicability element.
3. **Capability gating** — skip tests a target server cannot support. `TestScript.metadata.capability` exists natively (fields verified from the R4 schema in `Ignixa.Specification`: `required`, `validated`, `description`, `origin`, `destination`, `link`, `capabilities`), but it is *declarative by reference*: `capabilities` is a canonical to a whole separate `CapabilityStatement` resource, and the spec never defines the algorithm by which an engine decides "does the target conform to that CapabilityStatement". It also only exists at script level — there is no per-test placement. What automation needs is a *machine-evaluable inline predicate* over the live `/metadata` response, at both suite and test granularity. This is a genuine gap, not a reimplementation: the native element declares intent for humans; the extension encodes a decision procedure for engines.
4. **Synthesized fixtures** — generate a schema-valid fake resource instead of embedding a literal fixture body, keeping suites short and schema-version-agnostic.

All extension URLs live under `http://ignixa.io/testscript/*`.

## Decision

### 1. `http://ignixa.io/testscript/parametrize` — data-driven test expansion

Complex extension on `TestScript.test[]` (0..1; the parser warns and uses the first if repeated — `TestScriptParser.ParseParametrize`). Sub-extensions: `variable` (valueString, must name a `TestScript.variable`) and `values` (valueString, comma-separated). The evaluator executes the test once per value with the variable bound, reporting each execution as `"{test.name} [{value}]"`.

From `conformance-tests/Search/intervals.json`:

```json
{
  "name": "Observation: date eq — only inside year matches",
  "extension": [{
    "url": "http://ignixa.io/testscript/parametrize",
    "extension": [
      { "url": "variable", "valueString": "searchDate" },
      { "url": "values", "valueString": "2028,2028-06,2028-06-15,2028-06-15T12:00:00Z" }
    ]
  }],
  "action": [
    { "operation": { "type": { "code": "search" }, "resource": "Observation",
      "params": "?code=fhir262-interval-test&date=eq${searchDate}" } }
  ]
}
```

Designed to degrade gracefully: the referenced variable carries a `defaultValue`, so an engine that ignores the extension executes the test once with the default (this contract is documented in the variable description in `intervals.json` itself).

### 2. `http://ignixa.io/testscript/fhirVersions` — per-test FHIR-version gate

`valueString` extension on `TestScript.test[]` with a comma-separated version list. When `ignixa-matrix run --fhir-version 4.3` is given, the evaluator (`IsVersionCompatible`) skips tests whose list does not contain the requested version; no extension, or no `--fhir-version`, means run everywhere. From `conformance-tests/Search/string-modifiers.json`:

```json
{
  "name": "of-type: identifier:of-type matches full system|code|value",
  "extension": [{ "url": "http://ignixa.io/testscript/fhirVersions", "valueString": "4.0,4.3,5.0" }],
  "action": [ { "operation": { "type": { "code": "search" }, "resource": "Patient",
    "params": "?identifier:of-type=http://terminology.hl7.org/CodeSystem/v2-0203|MR|12345" } } ]
}
```

Closes the gap of maintaining one suite tree across R4/R4B/R5: version-specific search modifiers (e.g. `:of-type`) stay in the shared file instead of forking the suite per version.

### 3. `http://ignixa.io/testscript/requiresCapability` — CapabilityStatement-evaluated skip

`valueString` FHIRPath expression, valid on the `TestScript` root (suite-level, `TestScriptMetadata.RequiresCapability`) and on `test[]` (`TestPhaseDefinition.RequiresCapability`). Only `valueString` is honored; other value types parse as absent. The CLI fetches `/metadata` once per run (`RunCommand.FetchCapabilityStatementAsync`) and the evaluator evaluates the expression against it (`EvaluateCapabilityRequirement`).

Precedence and failure policy (see `TestScriptEvaluator.ExecuteAsync`):
- The suite-level gate runs **before fixtures and setup**; if unmet, every test (including each parametrized iteration) is recorded as skipped and the run short-circuits.
- The per-test gate runs after the FHIR-version check and before parametrized expansion.
- **Fail-open** when no CapabilityStatement is available (unfetchable `/metadata` must not silently skip a whole run); **fail-closed** (skip, with the evaluation error as the reason) when the expression itself is malformed — that is an authoring bug, not a server characteristic.

No shipped conformance suite uses it yet; worked examples from `test/Ignixa.TestScript.Tests/Parsing/TestScriptParserTests.cs`:

```json
// test-level
"extension":[{"url":"http://ignixa.io/testscript/requiresCapability",
  "valueString":"rest.resource.where(type='Patient').operation.where(name='everything').exists()"}]

// suite-level (on the TestScript root)
"extension":[{"url":"http://ignixa.io/testscript/requiresCapability",
  "valueString":"rest.operation.where(name='reindex').exists()"}]
```

**Shorthand normalization (issue #324 follow-up):** `ignixa-lab` authoring source used a direct
`requiresCapability` string property instead of the extension form; because parsing is permissive,
that shorthand was silently inert, and `ignixa-lab` compensated with a host-side
`TestScriptContentNormalizer` preprocessor. That normalization is now upstream:
`TestScriptContentNormalizer` (`src/Core/Ignixa.TestScript/Parsing/TestScriptContentNormalizer.cs`) is
applied automatically by `TestScriptParser.Parse`/`ParseFile` and rewrites `requiresCapability` (root
and per-`test[]`) into the canonical extension before typed parsing runs. Both forms are accepted only
when they carry the identical expression; a non-string shorthand value or a shorthand/extension
disagreement is a parse error (`TestScriptNormalizationException`, surfaced as a
`ParseSeverity.Error`), and unrelated unrecognized properties still pass through untouched. The
normalizer is public so hosts with their own JSON pipeline can apply the same rewrite without
reimplementing it.

Compared point-by-point with native `TestScript.metadata.capability`: `capabilities` (canonical, 1..1) requires authoring a standalone CapabilityStatement per gate and an unspecified conformance-subsumption algorithm; `required`/`validated` are descriptive booleans with no evaluable predicate; `origin`/`destination`/`link` address multi-server topology and documentation, not gating logic; and the backbone exists only at script level. The extension replaces all of that with one inline expression over the live `/metadata` payload, at both granularities.

### 4. `http://ignixa.io/testscript/fhirfakes` — synthesized fixtures

`valueCode` (resource type) extension placed **inside the inline resource object carried by `fixture[].resource`** — not as a sibling of `fixture`. (Note: the engine already treats `fixture.resource` as an inline resource body; spec-pure R4 `fixture.resource` is a `Reference`.) Consumed by `FhirFakesFixtureProvider` (`src/Core/Ignixa.TestScript.FhirFakes/FhirFakesFixtureProvider.cs`), which generates a schema-valid fake via `SchemaBasedFhirResourceFaker`. From `conformance-tests/CRUD/basic.json`:

```json
{
  "id": "patient-fixture",
  "resource": {
    "extension": [
      { "url": "http://ignixa.io/testscript/fhirfakes", "valueCode": "Patient" }
    ]
  }
}
```

Co-location with the fixture body imposes a provider-ordering constraint: `CompositeFixtureProvider` returns the first non-null result, so `FhirFakesFixtureProvider` must precede `InlineFixtureProvider` — otherwise the inline provider returns the skeleton resource (which is just the extension carrier) verbatim and generation never happens. This ordering is enforced by convention at every call site (e.g. `RunCommand.cs`) and documented in `docs/site/docs/core-sdk/testscript.md`.

**Naming discrepancy resolved:** `http://ignixa.io/testscript-fhirfakes-generation` (with a `valueString` C# faker-expression DSL) appears only in the pre-implementation investigation `docs/features/testscript/investigations/execution-engine.md`. Neither that URL nor the expression DSL shipped; the implemented contract is `http://ignixa.io/testscript/fhirfakes` with `valueCode` = resource type only (no profile-driven generation yet). The investigation doc is historical; the implemented URL is authoritative.

No other custom extensions were found (`grep -rn "ignixa.io/testscript"` across source, tests, `conformance-tests/`, and `docs/`).

## Consequences

**Positive:**
- The conformance matrix runs unattended across servers and FHIR versions with skip reasons recorded per cell, instead of hand-forked suites or hard failures on unsupported capabilities.
- `parametrize` collapses N near-identical tests into one definition; adding a boundary value is a one-string change.
- Three of the four extensions are ignore-safe: unknown non-modifier extensions must be ignored per the FHIR spec, so a plain engine still runs the suite — `parametrize` falls back to `variable.defaultValue`, and version/capability gates simply don't skip.

**Negative:**
- Portability is degraded, not preserved: on a plain engine, capability/version-gated tests *fail* (rather than skip) against servers lacking the capability, and `fhirfakes` fixtures break outright — the skeleton `resource` has no real content to POST. A suite using `fhirfakes` is Ignixa-only today.
- The inline-`fixture.resource` convention (which `fhirfakes` depends on) is itself a deviation from R4's `Reference` type, so stock validators flag these scripts before even reaching the extensions.
- No published StructureDefinitions exist yet, so external authors get no validation or IDE support for the extension URLs (addressed below).
- The alternative — waiting on an HL7 ballot cycle for first-class elements — was rejected: these behaviors were needed to ship the conformance matrix, and extensions are the spec-sanctioned escape hatch.

## Future TestScript change proposals (HL7)

Scoped as they would be filed in HL7 Jira (project FHIR, resource TestScript, FHIR Infrastructure WG), targeting the next TestScript release:

1. **Parametrized tests** — add `TestScript.test.parameter` (BackboneElement, 0..1): `variable` (1..1, id of a `TestScript.variable`) and `value` (1..*, string). Semantics: the test executes once per value with the variable bound; `TestReport.test` gains a matching element identifying the value for each execution. Fallback for non-supporting engines: execute once using `variable.defaultValue` (exactly the degradation contract Ignixa uses today).
2. **FHIR-version applicability** — add `TestScript.fhirVersion` (0..*, code, FHIRVersion value set) and `TestScript.test.fhirVersion` (0..*, overriding the script-level list), mirroring `CapabilityStatement.fhirVersion`. Engines skip non-matching tests and report them as skipped, not failed.
3. **Evaluable capability gates** — extend `TestScript.metadata.capability` with `condition` (0..1, string, FHIRPath evaluated against the CapabilityStatement retrieved from the destination) as a machine-checkable alternative to the `capabilities` canonical, and allow the capability backbone at `TestScript.test` level. The proposal should also pin skip semantics: an unmet condition yields a skipped (not failed) result in TestReport. Ignixa's fail-open/fail-closed policy is the suggested default behavior.
4. **Generated fixtures** — widen `TestScript.fixture` with a choice: today's `resource` Reference, or `generateType` (code) / `generateProfile` (canonical to StructureDefinition), meaning "engine synthesizes a conformant instance". Generation strategy stays engine-defined; the element only standardizes the request. This is the most speculative of the four and could reasonably start as a standard extension in the FHIR extensions pack instead.

## Interim Implementation Guide

Publish a small FHIR package, **`ignixa.fhir.testscript-extensions`** (canonical base `http://ignixa.io/fhir/testscript-extensions`), containing:

- **Four extension StructureDefinitions.** Their `url` values must remain exactly the URLs already in the wild (`http://ignixa.io/testscript/parametrize`, `.../fhirVersions`, `.../requiresCapability`, `.../fhirfakes`) — instances bind to `Extension.url`, so re-homing under the package canonical would break every existing script. Contexts and types as implemented:
  - `parametrize`: context `TestScript.test`, 0..1, complex (sub-extensions `variable` 1..1 string, `values` 1..1 string).
  - `fhirVersions`: context `TestScript.test`, 0..1, `valueString`.
  - `requiresCapability`: contexts `TestScript` and `TestScript.test`, 0..1 each, `valueString` (FHIRPath).
  - `fhirfakes`: intended placement is inside the inline `fixture.resource` object, which is not a spec-valid location a stock validator can reach (see Consequences). Declare context `Resource` with the placement constraint documented in the SD description; a cleaner context becomes possible only via the fixture-generation spec change above.
- **A profile** `http://ignixa.io/fhir/testscript-extensions/StructureDefinition/ignixa-testscript` on TestScript slicing `extension` and `test.extension` on the above URLs, giving external authors validator and IDE (`.fhir` package cache) support.
- **No StructureMap.** Assessed and rejected for this purpose despite Ignixa shipping a full FML engine (`Ignixa.FhirMappingLanguage`): a StructureMap is a static structural transform, but the value of these extensions is *runtime* branching on the target server (`/metadata` contents, `--fhir-version`), which a pre-run transform cannot know. The one static transform with real value — "downlevel" a script for plain engines by stripping gate extensions and expanding `parametrize` into N literal tests with the variable textually substituted into `params`/`expression` strings — is string templating, which FML handles poorly; it is better built as a small C# utility on the existing `TestScriptDefinition` model (blocked today only by the model having no JSON writer, noted in `docs/site/docs/core-sdk/testscript.md`). No such transform utility currently exists in the repo (verified: the ConformanceMatrix CLI has only `run` and `merge`).
- **Versioning/publishing:** the package versions independently of server releases (semver, bumped on content change), starting at `0.1.0` to match the repo's experimental-versioning posture (ADR 2606). Publish by committing the package source under `docs/` and serving the built `package.tgz` plus rendered pages from `docs/site/static/fhir/testscript-extensions/` — the existing `docs.yml` GitHub Pages deployment picks static assets up with no new infrastructure. Open question for the maintainer: the canonical host `ignixa.io` does not currently resolve these paths; registering the package on packages.fhir.org (or Simplifier) would make canonicals resolvable to tooling and is recommended before advertising the IG externally.

## References

- Engine: `src/Core/Ignixa.TestScript/Parsing/TestScriptParser.cs`, `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`
- Shorthand normalization: `src/Core/Ignixa.TestScript/Parsing/TestScriptContentNormalizer.cs`, `src/Core/Ignixa.TestScript/Parsing/TestScriptNormalizationException.cs`
- FhirFakes: `src/Core/Ignixa.TestScript.FhirFakes/FhirFakesFixtureProvider.cs`
- CLI wiring: `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs`
- Docs: [TestScript Engine](https://brendankowitz.github.io/ignixa-fhir/docs/core-sdk/testscript)
- FHIR spec: [TestScript](https://hl7.org/fhir/testscript.html), [Extensibility](https://hl7.org/fhir/extensibility.html), [FHIR NPM packages](https://hl7.org/fhir/packages.html)
