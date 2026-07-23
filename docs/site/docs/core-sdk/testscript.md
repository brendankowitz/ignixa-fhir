---
sidebar_position: 12
title: TestScript Engine
description: Parse and execute FHIR TestScript resources
---

# Ignixa.TestScript

A FHIR TestScript execution engine that parses [TestScript](https://hl7.org/fhir/testscript.html) resources and evaluates them against any FHIR server — either via HTTP or in-process.

## Installation

```bash
dotnet add package Ignixa.TestScript
```

## Overview

The engine follows a three-phase architecture consistent with other Ignixa Core libraries:

1. **Parse** — JSON TestScript → immutable expression tree (`TestScriptDefinition`)
2. **Evaluate** — Execute operations and assertions via `ITestRequestProvider` abstraction
3. **Report** — Produce FHIR `TestReport` resource, JUnit XML, or console output

## Quick Start

```csharp
using Ignixa.Specification.Generated;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Reporting;

// 1. Parse
var result = TestScriptParser.ParseFile("patient-read-test.json");
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error.Message);
    return;
}

// 2. Configure
var httpClient = new HttpClient { BaseAddress = new Uri("https://your-fhir-server") };
var provider = new HttpTestRequestProvider(httpClient);
var schemaProvider = new R4CoreSchemaProvider();
var evaluator = new TestScriptEvaluator(provider, new InlineFixtureProvider(), schemaProvider);

// 3. Execute
var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);

// 4. Report
var testReport = TestReportResourceGenerator.Generate(report);
Console.WriteLine(testReport.ToJsonString());
```

`Generate` takes an optional `TestReportContext` carrying the facts the engine cannot infer from the
run itself — who executed the script, which server was exercised, and what to call the script. Supply
it to populate `tester`, the `server` participant, and `testScript.display`:

```csharp
var testReport = TestReportResourceGenerator.Generate(report, new TestReportContext
{
    Tester = "my-server",
    ServerUri = "https://your-fhir-server",
    TestScriptDisplay = "Search/intervals.json"
});
```

Omitting it is fine: `testScript.display` falls back to the script's name, and the `server`
participant is dropped rather than emitted with a placeholder URI.

The parser is strict: unknown assert operators, unsupported criteria fields, malformed actions, and
type-mismatched fields all produce `ParseSeverity.Error` entries rather than silently changing test
semantics. Always check `IsSuccess` and surface `Errors` — a script that fails to parse never reaches
the evaluator.

## Building TestScripts in Code

JSON is only one front-end. `TestScriptEvaluator.ExecuteAsync` takes the `TestScriptDefinition`
model directly, and the whole model graph is public immutable records — so tests can be defined in
C# without any JSON:

```csharp
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Expressions;

var definition = new TestScriptDefinition
{
    Metadata = new TestScriptMetadata { Name = "Patient read" },
    Tests =
    [
        new TestPhaseDefinition
        {
            Name = "read returns 200",
            Actions =
            [
                new OperationExpression
                {
                    Type = "read",
                    Resource = "Patient",
                    Params = "/example",
                },
                new AssertExpression { Criteria = new ResponseCodeCriteria("200") },
                new AssertExpression
                {
                    Criteria = new FhirPathCriteria("Patient.id = 'example'"),
                    WarningOnly = true,
                },
            ],
        },
    ],
};

var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);
```

Assertions are expressed through the closed `AssertCriteria` hierarchy
(`ResponseCodeCriteria`, `ResponseStatusCriteria`, `ResourceTypeCriteria`, `ContentTypeCriteria`,
`HeaderCriteria`, `FhirPathCriteria`, `FhirPathValueCriteria`, `RequestMethodCriteria`,
`RequestUrlCriteria`), so the compiler enforces which fields each assertion kind needs. Fixtures,
variables, setup asserts, parametrized tests, and teardown are all expressible the same way via
`FixtureDefinition`, `VariableDefinition`, `Setup`, `ParametrizeDefinition`, and `Teardown`.

There is currently no writer from the model back to TestScript JSON — the model is the runtime
representation, JSON is the interchange format.

## In-Process Testing

For integration tests without network overhead, use `HttpTestRequestProvider` with ASP.NET Core's `WebApplicationFactory`:

```csharp
var factory = new WebApplicationFactory<Program>();
var httpClient = factory.CreateClient();
var provider = new HttpTestRequestProvider(httpClient);
```

## FhirFakes Integration

Auto-generate test fixtures using the `Ignixa.TestScript.FhirFakes` package:

```bash
dotnet add package Ignixa.TestScript.FhirFakes
```

`CompositeFixtureProvider` tries each provider in order and returns the first non-null result. `FhirFakesFixtureProvider` must come before `InlineFixtureProvider` because `InlineFixtureProvider` returns the `fixture.Resource` value directly — and `FhirFakesFixtureProvider` reads the FhirFakes extension from inside that same `resource` object. If `InlineFixtureProvider` runs first it returns the skeleton resource immediately and `FhirFakesFixtureProvider` never runs.

```csharp
var fixtureProvider = new CompositeFixtureProvider([
    new FhirFakesFixtureProvider(),
    new InlineFixtureProvider()
]);
var evaluator = new TestScriptEvaluator(provider, fixtureProvider, schemaProvider);
```

The FhirFakes extension must be declared inside the `resource` object in the fixture definition, not at the fixture level:

```json
{
  "id": "generated-patient",
  "resource": {
    "resourceType": "Patient",
    "extension": [{
      "url": "http://ignixa.io/testscript/fhirfakes",
      "valueCode": "Patient"
    }]
  }
}
```

`FhirFakesFixtureProvider` reads `fixture.Resource.MutableNode["extension"]` to find the extension. If `resource` is absent or has no matching extension, the provider returns null and the next provider in the chain is tried.

`IFhirSchemaProvider` must be supplied to `TestScriptEvaluator` — the schema is passed through `FixtureResolutionContext` and is required by `SchemaBasedFhirResourceFaker` to generate valid fake resources.

## Polling Long-Running Operations (waitFor)

FHIR TestScript has no native way to express polling a long-running job (`$export`, `$import`, and
similar operations that return `202 Accepted` immediately and require polling a status endpoint until
the job completes). The `http://ignixa.io/testscript/waitFor` extension fills this gap: place it on an
`operation` action's `extension` array, and that operation is retried — the same request, resent —
while the response's HTTP status code matches a configurable "still working" code, up to a configurable
number of attempts, sleeping a configurable interval between attempts. Once the status stops matching,
or the attempt ceiling is reached, execution proceeds as normal — to whatever action comes next (typically
an `assert`), or, if the ceiling was reached while still polling, the operation is recorded as a failed
outcome instead.

The extension has three optional child extensions, all `valueInteger`, each with a default:

| Child extension | Meaning | Default |
|---|---|---|
| `pollingStatusCode` | HTTP status that means "still working" — keep retrying while the response matches it | `202` |
| `maxAttempts` | Maximum number of attempts (including the first) before giving up | `60` |
| `intervalMs` | Delay between attempts, in milliseconds | `1000` |

Values are validated at parse time, not clamped: `pollingStatusCode` must be in the 100-599 range,
`maxAttempts` must be at least 1, and `intervalMs` must be non-negative. Out-of-range values are parse
errors — the script never reaches the evaluator.

If the attempt ceiling is reached while the response still matches `pollingStatusCode`, the operation is
recorded as a failed outcome with a message like `Timed out waiting for job completion after 60 attempts
(last status: 202)`, rather than silently proceeding to the next action.

`waitFor` does not resolve a status URL for you — it only controls *retry* behavior for whatever request
the operation already builds. To poll a kickoff job's status endpoint, pair it with TestScript's existing
header-extraction `variable` mechanism: extract the kickoff response's `Content-Location` (or `Location`)
header into a variable, then target the polling operation's `url` at that variable.

```json
{
  "test": [{
    "name": "export completes",
    "action": [
      {
        "operation": {
          "type": { "code": "create" },
          "url": "$export",
          "responseId": "export-kickoff"
        }
      },
      {
        "operation": {
          "url": "${statusUrl}",
          "extension": [{
            "url": "http://ignixa.io/testscript/waitFor",
            "extension": [
              { "url": "pollingStatusCode", "valueInteger": 202 },
              { "url": "maxAttempts", "valueInteger": 30 },
              { "url": "intervalMs", "valueInteger": 2000 }
            ]
          }]
        }
      },
      { "assert": { "response": "okay" } }
    ]
  }],
  "variable": [
    { "name": "statusUrl", "sourceId": "export-kickoff", "headerField": "Content-Location" }
  ]
}
```

The `variable` extraction runs after every `operation` action, so `${statusUrl}` is populated by the
time the polling operation executes.

## xUnit Integration

Discover and run TestScript files as xUnit theories:

```bash
dotnet add package Ignixa.TestScript.XUnit
```

```csharp
[Theory]
[TestScriptData("testscripts/**/*.json")]
public async Task RunTestScript(string path)
{
    var result = TestScriptParser.ParseFile(path);
    if (!result.IsSuccess)
        throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Message)));
    var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);
    report.ShouldPass(); // TestScriptAssertions extension
}
```

`TestScriptAssertions` also provides `ShouldFail()`, `ShouldHaveTestCount(n)`,
`ShouldHavePassingSetup()`, and `ShouldHavePassingTeardown()`.

## Conformance Matrix CLI

The `ignixa-matrix` dotnet tool runs a folder of TestScript suites against a live FHIR server and
merges per-implementation reports into a published conformance matrix:

```bash
dotnet tool install -g Ignixa.ConformanceMatrix.Cli

# Run a conformance suite against a server, writing a Bundle of FHIR TestReport resources
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
  --impl my-server --out ./reports/my-server.json

# merge reads the native per-impl report, not TestReport, so ask for --format json
ignixa-matrix run --server https://your-fhir-server --tests ./src/Core/Ignixa.TestScript.Suites/testscripts \
  --impl my-server --out ./reports/my-server.json --format json

# Merge per-impl reports into the matrix (runs/ + index.json)
ignixa-matrix merge --results ./reports --out ./matrix \
  --commit "$(git rev-parse HEAD)" --branch main
```

`--out` is always the report file; `--format` chooses its shape — `fhir` (default) for a `Bundle` of
`TestReport` resources, or `json` for the native per-impl report that `merge` consumes.

`run` exits non-zero when any test fails *or errors* (an engine/transport error is never reported as
a pass), prints parse warnings per file, and records crashed scripts as `error` cells rather than
aborting the run. `--fhir-version` sets the `fhirVersion` parameter on the `Accept` header for
version-gated suites. `merge` replaces an existing run with the same id rather than duplicating it,
and refuses to proceed when a report file is unreadable.

## Compile TestScript for Azure Load Testing

Compile a parsed TestScript into the flat five-file Locust artifact accepted by Azure Load Testing:

```bash
ignixa-matrix compile-locust \
  --test path/to/TestScript.json \
  --out artifacts/testscript-load \
  --fhir-version 4.0 \
  --fixture-variants 100
```

The command writes five files to the output directory: `testscript.ir.json`, `diagnostics.json`,
`locustfile.py`, `ignixa_testscript_runtime.py`, and `requirements.txt`. Upload all five together
as a Locust test in Azure Load Testing.

### Execution model

Each virtual-user iteration executes one complete setup/test/teardown flow with isolated variables
and fixtures. The generated workload targets current Ignixa evaluator parity, not every behavior in
the HL7 TestScript specification. Shared cross-language contracts cover the runtime and 74 FHIRPath
expressions; the original .NET execution remains authoritative for producing a FHIR `TestReport`.

Supported operations, assertions, Ignixa extensions, and capability gates are checked at compile
time. Incompatible or malformed FHIRPath expressions produce explicit diagnostics in
`diagnostics.json`. `fhir.resources` is not a runtime dependency and the generated workload performs
no FHIR profile validation.

### Fixtures and target configuration

Fixture pools are bounded. Set `IGNIXA_FIXTURE_SEED` for deterministic fixture selection across
runs. Set the FHIR server target with `IGNIXA_BASE_URL` or Locust `--host`. Control per-iteration
pacing with `IGNIXA_WAIT_MIN_SECONDS` and `IGNIXA_WAIT_MAX_SECONDS`.

### Metrics and diagnostics

Each request contributes native source-qualified HTTP metrics. Assertion results are emitted as
synthetic `TESTSCRIPT_ASSERT` events; operation failures are emitted as `TESTSCRIPT_OPERATION`
events. `diagnostics.json` maps source-qualified names back to TestScript source paths.

### Runtime dependencies

Azure Load Testing provides Python 3.9.19 and Locust 2.33.2. Generated `requirements.txt` pins
`fhirpathpy==2.1.0`, `requests==2.32.3`, and `azure-identity==1.25.3`.

### Managed identity

| Variable | Description |
| --- | --- |
| `IGNIXA_AUTH_MODE` | `none` (default) or `managed-identity` |
| `IGNIXA_AUTH_SCOPE` | Target API application ID URI with `/.default`; required when using managed identity |
| `IGNIXA_MANAGED_IDENTITY_CLIENT_ID` | User-assigned identity client ID; omit to use system-assigned identity |

Assign the system-assigned or user-assigned identity to the Azure Load Testing resource and select
it as the engine reference identity (`referenceIdentities` with `kind: Engine` when the test is
configured as code). The target must trust Microsoft Entra tokens for the configured scope and
authorize that identity.

The runtime uses only `ManagedIdentityCredential`, starts fail-closed, caches and refreshes tokens
before expiry, and applies authentication to every FHIR HTTP request including capability checks,
operations, polling, and fixture management. HTTP 401 responses invalidate the cached token without
replaying the request. Static authorization headers and service-principal client secrets are
intentionally unsupported.

Azure Load Testing disables multi-region load distribution when managed identity authentication is
selected.

- [Authenticate with a managed identity — Azure Load Testing](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-test-secured-endpoints#authenticate-with-a-managed-identity)
- [Use a managed identity — Azure Load Testing](https://learn.microsoft.com/en-us/azure/app-testing/load-testing/how-to-use-a-managed-identity)

## Published FHIR Conformance Report

Ignixa publishes the latest R4 TestScript conformance run to the documentation site:

**[Open FHIR Conformance Report](/fhir-conformance)**

- **Raw Report**: [conformance/latest.json](https://brendankowitz.github.io/ignixa-fhir/conformance/latest.json)
- The report is generated during docs deployment by running the canonical suite corpus (`src/Core/Ignixa.TestScript.Suites/testscripts/`, also published as the `Ignixa.TestScript.Suites` package) through the same SQL Server/Azurite-backed E2E test environment used by CI.
- Failing conformance cells are published honestly; TestScript parse or evaluator errors fail docs generation.

## Related Documentation

- [ADR 2607: Custom TestScript Extensions for Automated Conformance Testing](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2607-testscript-extensions.md) — the `http://ignixa.io/testscript/*` engine extensions (`parametrize`, `fhirVersions`, `requiresCapability`, `fhirfakes`), why each exists, and the interim IG / future HL7 proposal for each.
