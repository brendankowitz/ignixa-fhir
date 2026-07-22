# TestScript-to-Locust Transpiler Design

**Date:** 2026-07-22
**Status:** Proposed
**Related:** [Azure Load Testing investigation](../../features/testscript/investigations/azure-load-testing.md), [ADR 2607: TestScript Extensions](../../adr/adr-2607-testscript-extensions.md)

## Problem

Ignixa has a typed parser and evaluator for FHIR TestScript resources, while Azure Load Testing
supports Locust as its Python load-testing engine. Reauthoring the existing TestScript suites as
Locust tests would duplicate their HTTP flows, fixtures, variables, assertions, and Ignixa
extensions.

The goal is to compile a TestScript into a Locust artifact with parity to the functionality currently
implemented by `Ignixa.TestScript`. This is not a commitment to implement every element in the HL7
TestScript specification. Features absent from the Ignixa parser or evaluator are outside the parity
target.

## Decision

Build a .NET compiler that consumes the existing `TestScriptDefinition` model and emits:

1. A versioned semantic intermediate representation (IR) as JSON.
2. A thin `locustfile.py` that loads the IR.
3. One shared, hand-written Python runtime module that interprets the IR.
4. A `requirements.txt`.

Do not generate bespoke Python control flow for every TestScript. Evaluator semantics belong in one
Python runtime so fixes are made and tested once.

Use `fhirpathpy` behind a narrow compatibility adapter for runtime FHIRPath evaluation. Do not make
`fhir.resources` a required runtime dependency in the first version. It provides typed resource
models but no TestScript execution or profile-validation behavior, and materializing Pydantic models
on every response would add work that Ignixa's current JSON-node evaluator does not perform.

## Scope

### Goals

- Preserve the behavior implemented by `TestScriptEvaluator` for a single TestScript execution.
- Repeat that isolated execution safely across Locust virtual users and iterations.
- Keep all HTTP traffic on Locust's instrumented HTTP client.
- Preserve supported fixtures, variables, action ordering, assertions, and Ignixa extensions.
- Fail explicitly when an input cannot be represented faithfully.
- Produce native Locust/Azure Load Testing performance metrics.
- Keep the existing .NET execution as the conformance preflight and authoritative TestReport.

### Non-goals

- Full HL7 TestScript coverage beyond the current Ignixa parser and evaluator.
- Multi-origin or multi-destination execution.
- Profile validation through `validateProfileId`.
- `compareToSource*`, `minimumId`, rules/rulesets, XPath, or full JSONPath support until Ignixa
  implements those semantics.
- Aggregating concurrent load executions into one FHIR TestReport.
- Calling .NET for HTTP execution from inside Locust.
- Requiring pythonnet, CoreCLR, NativeAOT, or a companion process in Azure Load Testing.
- Uploading, provisioning, or running Azure Load Testing resources in the compiler.

## Architecture

```text
TestScript JSON
      |
      v
TestScriptParser
      |
      v
TestScriptDefinition / ActionExpression / AssertCriteria
      |
      +--> LocustSupportAnalyzer --> diagnostics
      |
      +--> LocustIrCompiler
              |
              +--> testscript.ir.json
              +--> locustfile.py
              +--> ignixa_testscript_runtime.py
              +--> requirements.txt

Azure Load Testing / Locust
      |
      v
ignixa_testscript_runtime
      |
      +--> Locust HttpUser.client for every HTTP request
      +--> fhirpathpy adapter for supported FHIRPath evaluation
      +--> per-execution variables, fixtures, requests, and responses
```

### .NET compiler

Add a Locust compiler library beside the TestScript core library. It depends on
`Ignixa.TestScript`, `Ignixa.FhirPath`, serialization, and specification providers, but not on API,
Application, or DataLayer projects.

The compiler has three responsibilities:

1. Analyze the parsed model and reject unsupported behavior.
2. Resolve behavior that is more faithful and cheaper in .NET before load generation.
3. Lower the semantic model into the versioned Locust IR.

Expose the compiler through a `compile-locust` command in the conformance matrix CLI. The command
accepts the TestScript path, FHIR version, output directory, and the same server/authentication
inputs used by conformance preflight when capability evaluation is required.

### Python runtime

Maintain one pure-Python runtime module in the compiler project and copy its versioned source into
each generated artifact. This avoids requiring a private Python package feed in Azure Load Testing
while retaining one source implementation in the repository.

The generated `locustfile.py` contains configuration only:

- Define the `HttpUser`.
- Load `testscript.ir.json`.
- Invoke the runtime once per Locust task iteration.
- Apply Locust wait-time configuration supplied by the generated artifact or environment.

It does not contain generated operation or assertion implementations.

All generated files occupy one flat artifact namespace. Resolved fixture bodies and fixture variants
are embedded in `testscript.ir.json`; the compiler does not emit subdirectories or rely on archive
path preservation.

## Intermediate Representation

The IR is semantic, not a re-serialization of the source TestScript. Custom extension URLs and FHIR
wire-format choices are removed by `TestScriptParser` before this stage.

The top-level document contains:

```json
{
  "schemaVersion": "1.0",
  "compilerVersion": "0.1.0",
  "metadata": {
    "name": "CRUD basic",
    "source": "CRUD/basic.json",
    "fhirVersion": "4.0"
  },
  "fixtures": [],
  "variables": [],
  "setup": [],
  "tests": [],
  "teardown": []
}
```

IR actions are a closed union:

- `operation`: method, URL template, parameter template, headers, body source, response identifier,
  request identifier, content types, and optional wait condition
- `assert`: one supported criteria variant, direction, source identifier, operator, value,
  warning-only flag, optional alternative-group identifier, and optional response-status condition

Variable extraction is a closed union:

- default value
- response header
- Ignixa-compatible dotted body path
- FHIRPath expression

The IR duplicates no raw TestScript extension structure. Adding a new parser/evaluator semantic
requires an explicit IR version change and a corresponding runtime implementation.

The runtime rejects unsupported IR major versions before any virtual users start. Minor versions may
only add optional fields with defined defaults.

## Compile-Time Processing

The compiler performs these operations before emitting IR:

- Parse the TestScript and retain all parser warnings and errors.
- Validate every expression node against the Locust support matrix.
- Filter tests by `fhirVersions`.
- Expand `parametrize` into concrete test executions.
- Resolve each `fhirfakes` fixture with Ignixa's schema provider into a compile-time variant pool and
  embed that pool in the IR.
- Validate every `requiresCapability` expression with `Ignixa.FhirPath`, then preserve the expression
  in the IR for evaluation against the run-time target.
- Normalize operation method and URL behavior using the same rules as `TestScriptEvaluator`.
- Emit stable action identifiers used for Locust metric names and diagnostics.

No unsupported behavior is silently omitted. An unsupported node produces a compiler error that
names the TestScript path, phase, test, action, and reason.

When `fhirfakes` is present, `compile-locust` requires `--fixture-variants` with a positive value.
Each generated variant is schema-valid and the runtime selects variants across executions by a
hash of an optional run seed, the engine hostname, a runtime-assigned user ordinal, and that user's
iteration number. This preserves varied load data without requiring a second schema-based faker
implementation in Python. The pool is finite and can repeat; the compiler records the pool size in
diagnostics so that bounded variation is explicit rather than mistaken for per-execution generation.

## Runtime Execution Model

One invocation of the Locust task represents one complete, isolated TestScript execution:

1. Create fresh fixture, variable, request-history, and response-history dictionaries.
2. Materialize the emitted fixtures.
3. Run setup sequentially.
4. If setup succeeds, run every emitted test sequentially.
5. Run teardown.
6. Discard the execution context.

The same virtual user may perform another invocation after its configured wait time. A new context is
created for every invocation. This preserves Ignixa's setup-test-teardown semantics within each
execution while allowing Locust to scale independent repetitions across users and engines.

There is no process-wide mutable TestScript state. Resource identifiers created during setup are
stored in the execution-local variable and response history. Scripts that hard-code colliding
resource identifiers remain the author's responsibility and receive a compiler warning.

The runtime performs one initialization step per Locust engine process before users start:

1. Fetch the current target's CapabilityStatement through an uninstrumented preflight HTTP session.
2. Evaluate suite- and test-level `requiresCapability` expressions through the compatibility-tested
   FHIRPath adapter.
3. Store the resulting immutable gate decisions for all virtual users in that engine.

If the CapabilityStatement cannot be fetched, gating fails open exactly as Ignixa does. If a gate
expression is invalid at runtime, it fails closed with an explicit diagnostic. An unmet suite-level
gate disables the entire task: fixtures, setup, tests, and teardown do not run. An unmet test-level
gate skips only that test. Capability decisions are refreshed for every Locust run, so generated
artifacts do not freeze the state of the server used during compilation. The preflight request is
excluded from load metrics because it occurs before virtual users start, matching the conformance
runner's existing one-time metadata fetch.

### Operations

The runtime ports the currently implemented request behavior:

- HTTP method derivation
- URL and parameter construction
- Header substitution
- Body lookup through `sourceId`
- Request and response history
- Variable extraction after a response
- `waitFor` polling with cooperative `gevent.sleep`

Every actual HTTP attempt uses the Locust user client's request method. Polling attempts share a
stable request name so Azure reports their combined latency and throughput.

### Assertions

Port the criteria and operators currently represented by `AssertCriteria` and `AssertOperator`,
including:

- response category and response code
- content type
- resource type
- response headers
- request method and URL
- FHIRPath boolean and scalar-value checks
- warning-only behavior
- assertion alternative groups
- response-status-conditional assertions

Normal assertion outcomes are emitted as Locust request events with request type
`TESTSCRIPT_ASSERT`, zero response time, and stable source-qualified names separate from HTTP sampler
names. A failed assertion supplies an exception to the event; a passing assertion does not. An
inapplicable assertion produces no request event and is recorded as skipped in structured
diagnostics.

A failed warning-only assertion produces a structured warning but no request event, so it cannot
affect Locust's error percentage. Operation-level semantic failures that are not represented by a
failed HTTP request, including `waitFor` exhaustion, emit failed `TESTSCRIPT_OPERATION` events.

Synthetic assertion and operation events contribute to Locust's aggregate request and error counts.
The design does not assume that Azure failure criteria can filter by Locust request type. Stable
source-qualified request names are always emitted; whether Azure can additionally scope criteria by
request type must be established by the Azure smoke test.

## FHIRPath Compatibility

The Python runtime exposes one internal function:

```python
evaluate_fhirpath(expression, resource, expected_shape)
```

`expected_shape` is `boolean` or `scalar`. The adapter is responsible for matching Ignixa's:

- empty-result behavior
- boolean truth evaluation
- single-value coercion
- multi-value coercion
- string conversion
- exception-to-assertion-error behavior

The initial implementation uses `fhirpathpy`. An expression is supported only after differential
tests show equivalent behavior to `Ignixa.FhirPath` for the resources used by the relevant
TestScript. Known-incompatible expressions fail compilation through a maintained compatibility
manifest.

NativeAOT may later replace the adapter implementation with a `ctypes` call to Ignixa without
changing the IR or runtime control flow. It is not required for the first release.

## `fhir.resources`

`fhir.resources` is not required for current evaluator parity:

- TestScript parsing already happens in .NET.
- Runtime operations only require JSON dictionaries and byte payloads.
- `resourceType` assertions do not require typed model construction.
- The library does not implement TestScript execution, profile validation, or TestReport aggregation.

It may be added later behind an optional resource-model adapter for version-specific authoring checks.
That adapter must not run on every response by default and must not become a substitute for FHIR
profile validation.

## Error Handling

### Compiler errors

- Invalid TestScript JSON or parser errors stop compilation.
- Unsupported semantic nodes stop compilation.
- IR serialization failures stop compilation.
- Incompatible FHIRPath expressions stop compilation.
- Missing required fixture materialization stops compilation.

Parser warnings and compatibility warnings are included in a generated diagnostics file.

### Runtime errors

- IR version mismatch fails during Locust startup.
- Undefined variables, missing fixture/history entries, malformed response bodies, and assertion
  evaluation errors become named `TESTSCRIPT_ASSERT` failures.
- Missing header values and unresolved dotted body paths remain non-failing extraction misses, matching
  the current variable extractor. Malformed FHIRPath extraction expressions become explicit operation
  failures.
- Transport errors remain normal failed Locust HTTP requests.
- Operation-level semantic errors without a failed HTTP request emit `TESTSCRIPT_OPERATION` failures.
- Setup failure prevents test execution for that invocation.
- Teardown follows the same execution policy as `TestScriptEvaluator`.
- Cancellation and Locust shutdown are not converted into successful outcomes.

No error path silently returns a passing result.

## Reporting

Azure Load Testing and Locust remain authoritative for:

- request count and throughput
- response-time percentiles
- latency
- transport errors
- TestScript assertion error rate

The existing .NET preflight remains authoritative for conformance outcomes and FHIR TestReport
generation. The transpiler does not generate one TestReport per user or aggregate concurrent
execution timelines into a synthetic TestReport.

Generated diagnostics identify the source TestScript location for every stable metric name so a
Locust or Azure failure can be traced back to its source action.

## Testing

### Compiler tests

- Golden tests for representative `TestScriptDefinition` to IR output.
- Analyzer tests proving every supported expression is accepted.
- Analyzer tests proving unsupported behavior fails with a source-qualified diagnostic.
- Snapshot tests for `parametrize`, `fhirVersions`, `requiresCapability`, and `fhirfakes`
  compile-time processing.
- Fixture-pool tests proving variant count, selection, and explicit repetition behavior.
- IR schema-version compatibility tests.

### Runtime tests

- Port current evaluator behavior tests to Python using a fake Locust client.
- Cover operations, substitutions, extraction, histories, assertion operators, warning-only
  assertions, alternative groups, conditional assertions, polling, and phase control.
- Verify skipped assertions emit no request event, warning-only failures remain non-failing, and
  operation-level semantic failures emit `TESTSCRIPT_OPERATION`.
- Verify suite-level capability rejection performs no fixture, setup, test, or teardown work and
  test-level rejection skips only the gated test.
- Prove a fresh context is created per user iteration.
- Prove no mutable state crosses virtual users.

### Differential tests

Execute the same TestScript with deterministic responses through:

1. `TestScriptEvaluator` and a fake `ITestRequestProvider`.
2. The Python runtime and a fake Locust client.

Compare:

- emitted HTTP requests
- extracted variables
- request and response history
- phase ordering
- pass, fail, warning, error, and skip outcomes
- assertion messages where they are contractual

For `waitFor`, compare the number and sequence of provider/client sends. The .NET report's one
aggregate operation result is not expected to map one-to-one to Locust's event for every polling
attempt.

Run every FHIRPath expression in the shipped TestScript suites through both `Ignixa.FhirPath` and
`fhirpathpy`. Add the official FHIRPath corpus where the two adapters expose equivalent input types.

### End-to-end tests

- Run local Locust against a deterministic fake FHIR server.
- Run an Azure Load Testing smoke test that verifies dependency installation, artifact loading,
  multi-engine isolation, fixture uniqueness, polling, assertion events, and stable metric names.

## Acceptance Criteria

The first release is acceptable when:

1. Every TestScript scenario supported by the current Ignixa evaluator either compiles or produces an
   explicit, documented compatibility error.
2. Every compiled scenario has equivalent single-execution requests, state transitions, and outcomes
   in the .NET and Python differential harness.
3. All FHIRPath expressions used by compiled suites pass the compatibility gate.
4. Concurrent Locust users repeat isolated TestScript executions without shared mutable state.
5. Azure Load Testing reports HTTP metrics and semantic assertion failures under stable names.
6. The original .NET conformance run continues to produce the authoritative TestReport.
7. Capability gates are evaluated once per engine against the current run-time target.
8. `fhirfakes` inputs use an explicit fixture-variant pool and never silently collapse to one shared
   resource body.

## Risks

- `fhirpathpy` may diverge from `Ignixa.FhirPath` on expressions used by real suites. The
  compatibility gate contains the risk; NativeAOT remains a possible later replacement.
- `fhirfakes` variation is bounded by the compile-time pool rather than generated without limit for
  every execution. Authors must size the pool for the intended concurrency and iteration count.
- The Python runtime intentionally duplicates evaluator semantics. Differential tests, a versioned
  IR, and one shared runtime reduce but do not eliminate drift.
- Synthetic assertion and operation events affect aggregate Locust request and error counts. Azure's
  ability to filter criteria by Locust request type is unverified; stable request names and the smoke
  test provide the fallback and decision point.
- Per-execution fixture creation can generate significant write load. This is intentional but may
  require test-specific data and cleanup policy.
- Azure Load Testing pins Python and Locust versions independently of this repository. The generated
  artifact must declare and test its supported engine versions.

## Rejected Alternatives

### Generate complete Python source per TestScript

Rejected because every generated artifact would contain another copy of evaluator behavior.
Correctness fixes would require regeneration, generated code would be noisy, and differential testing
would have to reason about many generated implementations instead of one runtime.

### Make `fhir.resources` the runtime model

Rejected because it adds object-materialization cost without supplying the missing execution,
validation, or reporting semantics. Raw JSON is closer to the current Ignixa evaluator.

### Use pythonnet and bundled CoreCLR

Rejected as the primary design because it depends on undocumented Azure engine behavior, carries a
large runtime payload, and introduces JIT and native probing into every load engine.

### Require a NativeAOT Ignixa bridge

Rejected for the first release because Ignixa has not been validated for NativeAOT and no C ABI
exists. The architecture leaves a narrow FHIRPath seam where this can be added later without changing
HTTP execution or the IR.
