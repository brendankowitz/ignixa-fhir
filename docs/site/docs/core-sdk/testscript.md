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
var evaluator = new TestScriptEvaluator(provider, new InlineFixtureProvider(), schemaProvider);

// 3. Execute
var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);

// 4. Report
var testReport = TestReportResourceGenerator.Generate(report);
Console.WriteLine(testReport.ToJsonString());
```

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
    report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
}
```

## Supported Features

| Feature | Status |
|---------|--------|
| CRUD operations | ✅ |
| Search operations | ✅ |
| Response assertions | ✅ |
| FHIRPath assertions | planned |
| Variable substitution | ✅ |
| Fixture management | ✅ |
| autocreate/autodelete | ✅ |
| TestReport generation | ✅ |
| Multi-server (origin/destination) | planned |
| Batch/transaction | planned |
| Profile validation | planned |
