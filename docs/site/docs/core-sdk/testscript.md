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
2. **Evaluate** — Execute operations and assertions via `IFhirClient` abstraction
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
var definition = result.Value!;

// 2. Configure
var httpClient = new HttpClient { BaseAddress = new Uri("https://your-fhir-server") };
var fhirClient = new HttpFhirClient(httpClient);
var registry = new SingleClientRegistry(fhirClient);
var evaluator = new TestScriptEvaluator(registry, new InlineFixtureProvider(), schemaProvider);

// 3. Execute
var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

// 4. Report
var testReport = TestReportResourceGenerator.Generate(report);
Console.WriteLine(testReport.ToJsonString());
```

## In-Process Testing

For integration tests without network overhead, use `InProcessFhirClient` with ASP.NET Core's `WebApplicationFactory`:

```csharp
var factory = new WebApplicationFactory<Program>();
var httpClient = factory.CreateClient();
var fhirClient = new HttpFhirClient(httpClient);
```

## FhirFakes Integration

Auto-generate test fixtures using the `Ignixa.TestScript.FhirFakes` package:

```bash
dotnet add package Ignixa.TestScript.FhirFakes
```

```csharp
var provider = new CompositeFixtureProvider([
    new InlineFixtureProvider(),
    new FhirFakesFixtureProvider()
]);
```

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
    var definition = TestScriptParser.ParseFile(path);
    var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);
    report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
}
```

## Supported Features

| Feature | Status |
|---------|--------|
| CRUD operations | ✅ |
| Search operations | ✅ |
| Response assertions | ✅ |
| FHIRPath assertions | ✅ |
| Variable substitution | ✅ |
| Fixture management | ✅ |
| autocreate/autodelete | ✅ |
| TestReport generation | ✅ |
| Multi-server (origin/destination) | 🔜 |
| Batch/transaction | 🔜 |
| Profile validation | 🔜 |
