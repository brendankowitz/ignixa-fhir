# Ignixa.TestScript

A FHIR TestScript execution engine that parses and evaluates [TestScript](https://hl7.org/fhir/testscript.html) resources against any FHIR server.

## Installation

```bash
dotnet add package Ignixa.TestScript
```

## Quick Start

```csharp
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Fixtures;

// Parse a TestScript
var result = TestScriptParser.ParseFile("tests/patient-crud.json");
if (!result.IsSuccess)
{
    foreach (var error in result.Errors)
        Console.Error.WriteLine(error.Message);
    return;
}

// Configure execution
var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var provider = new HttpTestRequestProvider(httpClient);

// Execute
var evaluator = new TestScriptEvaluator(provider, new InlineFixtureProvider(), schemaProvider);
var report = await evaluator.ExecuteAsync(result.Value!, CancellationToken.None);

Console.WriteLine($"Result: {report.OverallOutcome}"); // Pass, Fail, or Error
```

## Architecture

The engine follows a three-phase pattern:

1. **Parse** — JSON → `TestScriptDefinition` expression tree
2. **Evaluate** — Execute operations and assertions via `ITestRequestProvider`
3. **Report** — Produce FHIR `TestReport` resource or JUnit XML

## Related Packages

- `Ignixa.TestScript.FhirFakes` — Auto-generate test fixtures
- `Ignixa.TestScript.XUnit` — Discover and run TestScripts as xUnit theories
