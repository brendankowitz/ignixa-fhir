# Conformance CLI Auth Header and TestReport Output Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the conformance CLI with opt-in auth-header support and FHIR TestReport output while preserving the existing matrix report behavior.

**Architecture:** Add two new options to the `run` command, apply the auth header to the shared `HttpClient` used by the TestScript engine, and serialize each executed `TestScriptReport` into a FHIR `TestReport` resource or a `Bundle` collection when multiple scripts run.

**Tech Stack:** C#, .NET 10, System.CommandLine, Ignixa.TestScript, System.Text.Json.Nodes

---

### Task 1: Add auth-header parsing helpers and tests

**Files:**
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs`
- Modify: `test/Ignixa.ConformanceMatrix.Cli.Tests/RunCommandTests.cs`

- [ ] **Step 1: Write failing tests for auth header normalization**

```csharp
[Fact]
public void GivenBareTokenValue_WhenNormalizingAuthHeader_ThenUsesAuthorizationHeader()
{
    var (name, value) = RunCommand.ParseAuthHeader("Bearer abc123");

    name.ShouldBe("Authorization");
    value.ShouldBe("Bearer abc123");
}

[Fact]
public void GivenExplicitHeaderValue_WhenNormalizingAuthHeader_ThenPreservesHeaderName()
{
    var (name, value) = RunCommand.ParseAuthHeader("X-Test: value");

    name.ShouldBe("X-Test");
    value.ShouldBe("value");
}
```

- [ ] **Step 2: Run the focused CLI tests to verify they fail**

Run: `dotnet test test/Ignixa.ConformanceMatrix.Cli.Tests/Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~RunCommandTests -v minimal`
Expected: FAIL because `ParseAuthHeader` does not exist yet.

- [ ] **Step 3: Implement auth-header parsing and application**

```csharp
internal static (string Name, string Value) ParseAuthHeader(string input)
{
    var trimmed = input.Trim();
    if (trimmed.Contains(':') && !trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
    {
        var parts = trimmed.Split(new[] { ':' }, 2);
        return (parts[0].Trim(), parts[1].Trim());
    }

    return ("Authorization", trimmed);
}
```

- [ ] **Step 4: Run the focused CLI tests to verify they pass**

Run: `dotnet test test/Ignixa.ConformanceMatrix.Cli.Tests/Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~RunCommandTests -v minimal`
Expected: PASS

### Task 2: Add the new CLI options and wire them into the run flow

**Files:**
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs`
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/README.md`

- [ ] **Step 1: Add CLI options for auth and TestReport output**

```csharp
var authHeaderOption = new Option<string?>("--auth-header")
{
    Description = "Authentication header value to apply to every request (for example 'Bearer <token>' or 'Authorization: Bearer <token>')"
};

var testReportOption = new Option<string?>("--test-report")
{
    Description = "Optional path to write a FHIR TestReport JSON resource or Bundle"
};
```

- [ ] **Step 2: Pass the new values into the run method and apply the header to the `HttpClient`**

```csharp
var authHeader = parseResult.GetValue(authHeaderOption);
var testReportPath = parseResult.GetValue(testReportOption);
return RunAsync(server, tests, impl, outPath, fhirVersion, authHeader, testReportPath, cancellationToken);
```

- [ ] **Step 3: Update the command help text and README examples**

Add examples showing `--auth-header` and `--test-report` to `tools/Ignixa.ConformanceMatrix.Cli/README.md`.

### Task 3: Generate FHIR TestReport output files

**Files:**
- Modify: `tools/Ignixa.ConformanceMatrix.Cli/Commands/RunCommand.cs`
- Modify: `test/Ignixa.ConformanceMatrix.Cli.Tests/RunCommandTests.cs`

- [ ] **Step 1: Write failing tests for TestReport payload assembly**

```csharp
[Fact]
public void GivenSingleReport_WhenBuildingPayload_ThenReturnsTestReportResource()
{
    var payload = RunCommand.BuildTestReportPayload([new JsonObject { ["resourceType"] = "TestReport" }]);
    payload.ShouldNotBeNull();
    payload! ["resourceType"]!.GetValue<string>().ShouldBe("TestReport");
}

[Fact]
public void GivenMultipleReports_WhenBuildingPayload_ThenReturnsBundleCollection()
{
    var payload = RunCommand.BuildTestReportPayload([
        new JsonObject { ["resourceType"] = "TestReport" },
        new JsonObject { ["resourceType"] = "TestReport" }
    ]);

    payload.ShouldNotBeNull();
    payload!["resourceType"]!.GetValue<string>().ShouldBe("Bundle");
    payload["type"]!.GetValue<string>().ShouldBe("collection");
}
```

- [ ] **Step 2: Run the focused CLI tests to verify they fail**

Run: `dotnet test test/Ignixa.ConformanceMatrix.Cli.Tests/Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~RunCommandTests -v minimal`
Expected: FAIL because `BuildTestReportPayload` does not exist yet.

- [ ] **Step 3: Implement payload assembly and file writing**

```csharp
internal static JsonObject BuildTestReportPayload(IReadOnlyList<JsonObject> reports)
{
    if (reports.Count == 1)
        return reports[0];

    return new JsonObject
    {
        ["resourceType"] = "Bundle",
        ["type"] = "collection",
        ["entry"] = new JsonArray(reports.Select(report => new JsonObject { ["resource"] = report }).ToArray())
    };
}
```

- [ ] **Step 4: Run the focused CLI tests to verify they pass**

Run: `dotnet test test/Ignixa.ConformanceMatrix.Cli.Tests/Ignixa.ConformanceMatrix.Cli.Tests.csproj --filter FullyQualifiedName~RunCommandTests -v minimal`
Expected: PASS

### Task 4: Validate the end-to-end CLI flow

**Files:**
- No new files; run the existing CLI against a server

- [ ] **Step 1: Run the CLI against the deployed server with both new options**

```bash
dotnet run --project tools/Ignixa.ConformanceMatrix.Cli/Ignixa.ConformanceMatrix.Cli.csproj -- run \
  --server https://bkowitz-testdeploy.azurewebsites.net \
  --tests <testscripts-folder> \
  --impl bkowitz-testdeploy \
  --out ./reports/bkowitz-testdeploy.json \
  --auth-header "Bearer <token>" \
  --test-report ./reports/bkowitz-testdeploy.testreport.json
```

- [ ] **Step 2: Inspect the generated TestReport file and confirm the payload is valid JSON**

- [ ] **Step 3: Run the full CLI tests for the conformance tool**

Run: `dotnet test test/Ignixa.ConformanceMatrix.Cli.Tests/Ignixa.ConformanceMatrix.Cli.Tests.csproj -v minimal`
Expected: PASS
