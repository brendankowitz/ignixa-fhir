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

# Run a conformance suite against a server, producing a per-impl report
ignixa-matrix run --server https://your-fhir-server --tests ./conformance-tests \
  --impl my-server --out ./reports/my-server.json

# Merge per-impl reports into the matrix (runs/ + index.json)
ignixa-matrix merge --results ./reports --out ./matrix \
  --commit "$(git rev-parse HEAD)" --branch main
```

`run` exits non-zero when any test fails *or errors* (an engine/transport error is never reported as
a pass), prints parse warnings per file, and records crashed scripts as `error` cells rather than
aborting the run. `--fhir-version` sets the `fhirVersion` parameter on the `Accept` header for
version-gated suites. `merge` replaces an existing run with the same id rather than duplicating it,
and refuses to proceed when a report file is unreadable.
