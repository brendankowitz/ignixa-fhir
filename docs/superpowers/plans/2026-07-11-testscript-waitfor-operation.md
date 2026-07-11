# TestScript `waitFor` Operation Extension Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a custom TestScript operation extension, `http://ignixa.io/testscript/waitFor`, that retries an operation while its response's HTTP status code stays at a configurable "still working" value, so a TestScript can poll `$export`/`$import`-style async jobs to completion.

**Architecture:** Follows the existing `assertionWhenResponseStatus` precedent exactly: a new immutable record (`WaitForCondition`) parsed from a child-extension tree onto `OperationExpression`, evaluated as a bounded retry loop inside `TestScriptEvaluator.VisitOperationAsync`. No new assertion type, no new fixture/provider abstraction — the polling URL comes from TestScript's existing header-to-variable extraction, and the final status is checked with the existing `response`/`responseCode` assertion criteria.

**Tech Stack:** C# / .NET (net9.0 + net10.0 multi-target), `System.Text.Json.Nodes` for parsing, xUnit + Shouldly + NSubstitute for tests.

## Global Constraints

- Extension URL is exactly `http://ignixa.io/testscript/waitFor` — do not add a package-canonical prefix.
- Defaults when a child extension is omitted: `pollingStatusCode` = 202, `maxAttempts` = 60, `intervalMs` = 1000.
- Validation ranges: `pollingStatusCode` must be 100-599, `maxAttempts` must be >= 1, `intervalMs` must be >= 0. Violations are parse-time `ParseError`s (severity `Error`), not silently clamped.
- Timeout (attempts exhausted while still polling) is a hard operation failure recorded via the existing `OperationOutcome`/`RecordOperationResult` path — no new reporting type.
- No JSON-body inspection of any kind — polling is decided purely by `TestResponse.StatusCode`.
- No `sourceId`-based header lookup — the polling URL is resolved the same way every other operation's `Url` is (via `VariableResolver`/`BuildUrl`), relying on the test author having extracted the status URL into a variable using TestScript's existing `variable` mechanism.
- One type per file (matches `ResponseStatusCondition.cs` precedent for `WaitForCondition.cs`).
- Every async method keeps propagating `CancellationToken` — no new method introduces a call that drops it.

---

### Task 1: `WaitForCondition` model + parsing

**Files:**
- Create: `src/Core/Ignixa.TestScript/Expressions/WaitForCondition.cs`
- Modify: `src/Core/Ignixa.TestScript/Expressions/OperationExpression.cs`
- Modify: `src/Core/Ignixa.TestScript/Parsing/TestScriptParser.cs`
- Test: `test/Ignixa.TestScript.Tests/Parsing/TestScriptParserTests.cs`

**Interfaces:**
- Consumes: nothing new — this task only touches the parser layer.
- Produces: `Ignixa.TestScript.Expressions.WaitForCondition` (record: `PollingStatusCode` (int), `MaxAttempts` (int), `IntervalMs` (int)) and `OperationExpression.WaitFor` (nullable `WaitForCondition`). Task 2's evaluator work reads `expression.WaitFor` and its three properties by exactly these names.

- [ ] **Step 1: Write the failing parser tests**

Add these to the end of the `TestScriptParserTests` class in
`test/Ignixa.TestScript.Tests/Parsing/TestScriptParserTests.cs` (just inside the closing `}` of the class,
after the existing tests):

```csharp
[Fact]
public void GivenWaitForExtensionWithNoChildren_WhenParsing_ThenDefaultsApply()
{
    var json = """
        {
          "resourceType":"TestScript","name":"WaitForDefaults","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
              "extension":[{"url":"http://ignixa.io/testscript/waitFor"}]}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeTrue();
    var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
    op.WaitFor.ShouldNotBeNull();
    op.WaitFor!.PollingStatusCode.ShouldBe(202);
    op.WaitFor.MaxAttempts.ShouldBe(60);
    op.WaitFor.IntervalMs.ShouldBe(1000);
}

[Fact]
public void GivenWaitForExtensionWithExplicitChildren_WhenParsing_ThenValuesOverrideDefaults()
{
    var json = """
        {
          "resourceType":"TestScript","name":"WaitForExplicit","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
              "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                {"url":"pollingStatusCode","valueInteger":404},
                {"url":"maxAttempts","valueInteger":5},
                {"url":"intervalMs","valueInteger":250}
              ]}]}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeTrue();
    var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
    op.WaitFor!.PollingStatusCode.ShouldBe(404);
    op.WaitFor.MaxAttempts.ShouldBe(5);
    op.WaitFor.IntervalMs.ShouldBe(250);
}

[Fact]
public void GivenWaitForPollingStatusCodeOutOfRange_WhenParsing_ThenReturnsParseError()
{
    var json = """
        {
          "resourceType":"TestScript","name":"WaitForInvalidStatus","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
              "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                {"url":"pollingStatusCode","valueInteger":999}
              ]}]}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeFalse();
    result.Errors.ShouldContain(e => e.Message.Contains("100") && e.Message.Contains("599"));
}

[Fact]
public void GivenWaitForMaxAttemptsLessThanOne_WhenParsing_ThenReturnsParseError()
{
    var json = """
        {
          "resourceType":"TestScript","name":"WaitForInvalidMaxAttempts","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
              "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                {"url":"maxAttempts","valueInteger":0}
              ]}]}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeFalse();
    result.Errors.ShouldContain(e => e.Message.Contains("at least 1"));
}

[Fact]
public void GivenWaitForIntervalMsNegative_WhenParsing_ThenReturnsParseError()
{
    var json = """
        {
          "resourceType":"TestScript","name":"WaitForInvalidInterval","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1",
              "extension":[{"url":"http://ignixa.io/testscript/waitFor","extension":[
                {"url":"intervalMs","valueInteger":-1}
              ]}]}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeFalse();
    result.Errors.ShouldContain(e => e.Message.Contains("non-negative"));
}

[Fact]
public void GivenOperationWithNoWaitForExtension_WhenParsing_ThenWaitForIsNull()
{
    var json = """
        {
          "resourceType":"TestScript","name":"NoWaitFor","status":"active",
          "test":[{"name":"t","action":[
            {"operation":{"type":{"code":"read"},"resource":"Patient","params":"/1"}}
          ]}]
        }
        """;

    var result = TestScriptParser.Parse(json);

    result.IsSuccess.ShouldBeTrue();
    var op = result.Value!.Tests[0].Actions[0].ShouldBeOfType<OperationExpression>();
    op.WaitFor.ShouldBeNull();
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error is expected — `WaitFor` doesn't exist yet)**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~WaitFor"`
Expected: build FAILS — `OperationExpression` has no member `WaitFor`.

- [ ] **Step 3: Create the `WaitForCondition` record**

Create `src/Core/Ignixa.TestScript/Expressions/WaitForCondition.cs`:

```csharp
namespace Ignixa.TestScript.Expressions;

/// <summary>
/// Parsed form of the <c>http://ignixa.io/testscript/waitFor</c> extension: an operation carrying
/// this is retried — the same request, sent again — while its response's HTTP status equals
/// <paramref name="PollingStatusCode"/>, up to <paramref name="MaxAttempts"/> times, sleeping
/// <paramref name="IntervalMs"/> between attempts.
/// </summary>
public sealed record WaitForCondition(int PollingStatusCode, int MaxAttempts, int IntervalMs);
```

- [ ] **Step 4: Add the `WaitFor` property to `OperationExpression`**

In `src/Core/Ignixa.TestScript/Expressions/OperationExpression.cs`, add this property (after
`EncodeRequestUrl`, before the closing brace of the record body — i.e. right before the
`AcceptAsync` method):

```csharp
    public WaitForCondition? WaitFor { get; init; }
```

- [ ] **Step 5: Add parsing support in `TestScriptParser.cs`**

Add this constant next to the other extension URL constants (near
`private const string RequiresCapabilityUrl = ...` at the top of the class):

```csharp
    private const string WaitForUrl = "http://ignixa.io/testscript/waitFor";
```

Add this new private method anywhere among the other `Parse*` helper methods in the same file:

```csharp
    private static WaitForCondition? ParseWaitForCondition(JsonArray? extensions, string path, List<ParseError> errors)
    {
        var ext = extensions?.OfType<JsonObject>().FirstOrDefault(e => e["url"]?.GetValue<string>() == WaitForUrl);
        if (ext is null) return null;

        var pollingStatusCode = ReadWaitForIntChild(ext, "pollingStatusCode", 202);
        var maxAttempts = ReadWaitForIntChild(ext, "maxAttempts", 60);
        var intervalMs = ReadWaitForIntChild(ext, "intervalMs", 1000);

        if (pollingStatusCode is < 100 or > 599)
            errors.Add(new ParseError(ParseSeverity.Error,
                $"waitFor pollingStatusCode {pollingStatusCode} is outside the valid HTTP status range 100-599", path));

        if (maxAttempts < 1)
            errors.Add(new ParseError(ParseSeverity.Error,
                $"waitFor maxAttempts must be at least 1, was {maxAttempts}", path));

        if (intervalMs < 0)
            errors.Add(new ParseError(ParseSeverity.Error,
                $"waitFor intervalMs must be non-negative, was {intervalMs}", path));

        return new WaitForCondition(pollingStatusCode, maxAttempts, intervalMs);
    }

    private static int ReadWaitForIntChild(JsonObject waitForExtension, string childUrl, int defaultValue)
    {
        var child = waitForExtension["extension"]?.AsArray()?.OfType<JsonObject>()
            .FirstOrDefault(c => c["url"]?.GetValue<string>() == childUrl);
        if (child?["valueInteger"] is JsonValue v && v.TryGetValue<int>(out var value))
            return value;
        return defaultValue;
    }
```

In `ParseOperation` (the method returning `new OperationExpression { ... }`), add one line to the object
initializer, right after `Headers = ParseHeaders(op["requestHeader"]?.AsArray(), path, errors)`:

```csharp
            Headers = ParseHeaders(op["requestHeader"]?.AsArray(), path, errors),
            WaitFor = ParseWaitForCondition(op["extension"]?.AsArray(), path, errors)
```

(Note the trailing comma moves to the `Headers` line since `WaitFor` is now the last initializer.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~WaitFor"`
Expected: PASS (6 new tests, 0 failed).

- [ ] **Step 7: Run the full TestScript test suite to check for regressions**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj`
Expected: PASS, 0 failed (all pre-existing tests plus the 6 new ones, across both net9.0 and net10.0 targets).

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.TestScript/Expressions/WaitForCondition.cs src/Core/Ignixa.TestScript/Expressions/OperationExpression.cs src/Core/Ignixa.TestScript/Parsing/TestScriptParser.cs test/Ignixa.TestScript.Tests/Parsing/TestScriptParserTests.cs
git commit -m "feat(testscript): parse waitFor operation extension"
```

---

### Task 2: Evaluator polling loop

**Files:**
- Modify: `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`
- Test: `test/Ignixa.TestScript.Tests/Evaluation/TestScriptEvaluatorTests.cs`

**Interfaces:**
- Consumes: `OperationExpression.WaitFor` (nullable `WaitForCondition` with `PollingStatusCode`, `MaxAttempts`, `IntervalMs` int properties) from Task 1. `ITestRequestProvider.ExecuteAsync(TestRequest, CancellationToken) : Task<TestResponse>` and `TestResponse.StatusCode` (int) — both pre-existing, unchanged.
- Produces: no new public surface — this task only changes `VisitOperationAsync`'s internal behavior. Downstream `assert` actions (already implemented) consume the final response exactly as before via `context.ResponseHistory`/`context.LastResponse`.

- [ ] **Step 1: Write the failing evaluator tests**

Add these to the end of the `TestScriptEvaluatorTests` class in
`test/Ignixa.TestScript.Tests/Evaluation/TestScriptEvaluatorTests.cs` (just inside the closing `}` of the
class):

```csharp
[Fact]
public async Task GivenWaitForOperation_WhenStatusLeavesPollingCode_ThenStopsPollingAndRecordsSuccess()
{
    var responses = new Queue<TestResponse>(new[]
    {
        new TestResponse { StatusCode = 202 },
        new TestResponse { StatusCode = 202 },
        new TestResponse { StatusCode = 200 }
    });
    var callCount = 0;
    _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
        .Returns(call => { callCount++; return responses.Dequeue(); });

    var definition = new TestScriptDefinition
    {
        Metadata = new TestScriptMetadata { Name = "WaitForSuccess" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "PollUntilDone",
                Actions =
                [
                    new OperationExpression
                    {
                        Type = "read",
                        Url = "_export/job-1",
                        WaitFor = new WaitForCondition(PollingStatusCode: 202, MaxAttempts: 10, IntervalMs: 0)
                    },
                    new AssertExpression { Criteria = new ResponseStatusCriteria("okay") }
                ]
            }
        ]
    };

    var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
    var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

    callCount.ShouldBe(3);
    report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
}

[Fact]
public async Task GivenWaitForOperation_WhenNeverLeavesPollingCode_ThenFailsAfterMaxAttempts()
{
    var callCount = 0;
    _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
        .Returns(call => { callCount++; return new TestResponse { StatusCode = 202 }; });

    var definition = new TestScriptDefinition
    {
        Metadata = new TestScriptMetadata { Name = "WaitForTimeout" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "PollForever",
                Actions =
                [
                    new OperationExpression
                    {
                        Type = "read",
                        Url = "_export/job-1",
                        WaitFor = new WaitForCondition(PollingStatusCode: 202, MaxAttempts: 3, IntervalMs: 0)
                    }
                ]
            }
        ]
    };

    var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
    var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

    callCount.ShouldBe(3);
    report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Error);
    report.TestResults[0].Actions[0].Message!.ShouldContain("Timed out waiting for job completion after 3 attempts");
}

[Fact]
public async Task GivenOperationWithNoWaitFor_WhenExecuting_ThenSendsExactlyOnce()
{
    var callCount = 0;
    _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
        .Returns(call => { callCount++; return new TestResponse { StatusCode = 200 }; });

    var definition = new TestScriptDefinition
    {
        Metadata = new TestScriptMetadata { Name = "NoWaitForRegression" },
        Tests =
        [
            new TestPhaseDefinition
            {
                Name = "PlainRead",
                Actions = [new OperationExpression { Type = "read", Resource = "Patient", Params = "/1" }]
            }
        ]
    };

    var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
    var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

    callCount.ShouldBe(1);
    report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~WaitFor"`
Expected: the two new `GivenWaitForOperation_*` tests FAIL (no polling behavior implemented yet — the
first assertion after the timeout test, `callCount.ShouldBe(3)`, will see `callCount == 1` for the
success-path test, or a similar early mismatch for the timeout-path test). The regression test
(`GivenOperationWithNoWaitFor_WhenExecuting_ThenSendsExactlyOnce`) PASSES already — it exercises unchanged
behavior and confirms the baseline before you touch `VisitOperationAsync`.

- [ ] **Step 3: Implement the polling loop in `VisitOperationAsync`**

In `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`, replace the entire body of
`VisitOperationAsync` (the method starting at `public async ValueTask<TestScriptContext>
VisitOperationAsync(...)`) with:

```csharp
    public async ValueTask<TestScriptContext> VisitOperationAsync(
        OperationExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        TestRequest? request = null;
        try
        {
            if (expression.Destination is not null and > 1)
                throw new NotSupportedException(
                    $"Multi-server destinations are not supported. Destination '{expression.Destination}' was requested but only a single provider is configured.");

            if (!expression.EncodeRequestUrl)
                context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                    new AssertionOutcome(false, WarningOnly: true,
                        "encodeRequestUrl=false is not supported; URL was encoded"));

            request = BuildRequest(expression, context);
            context = context.WithRequest(expression.RequestId, request);

            var response = await _provider.ExecuteAsync(request, cancellationToken);

            if (expression.WaitFor is { } waitFor)
            {
                var attempts = 1;
                while (response.StatusCode == waitFor.PollingStatusCode && attempts < waitFor.MaxAttempts)
                {
                    await Task.Delay(waitFor.IntervalMs, cancellationToken);
                    response = await _provider.ExecuteAsync(request, cancellationToken);
                    attempts++;
                }

                if (response.StatusCode == waitFor.PollingStatusCode)
                {
                    context = context.WithResponse(expression.ResponseId, response);
                    sw.Stop();
                    context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                        new OperationOutcome(
                            false,
                            response.StatusCode,
                            ErrorMessage: $"Timed out waiting for job completion after {attempts} attempts (last status: {response.StatusCode})",
                            Duration: sw.Elapsed,
                            Request: request,
                            Response: response));
                    return context;
                }
            }

            context = context.WithResponse(expression.ResponseId, response);

            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(true, response.StatusCode, Duration: sw.Elapsed, Request: request, Response: response));
            return context;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            throw;
        }
        catch (OperationCanceledException ex)
        {
            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(false, ErrorMessage: $"Request timed out or was aborted: {ex.Message}", Duration: sw.Elapsed, Request: request));
            return context;
        }
        catch (Exception ex)
        {
            sw.Stop();
            context.Recorder.RecordOperationResult(expression.Label, expression.Description,
                new OperationOutcome(false, ErrorMessage: ex.Message, Duration: sw.Elapsed, Request: request));
            return context;
        }
    }
```

The only behavioral change versus the original method: when `expression.WaitFor` is set, the same
already-built `request` is resent (via `Task.Delay` + another `_provider.ExecuteAsync` call) while the
response's status code keeps matching `PollingStatusCode`, up to `MaxAttempts` total sends. When
`WaitFor` is `null`, execution falls straight through exactly as it did before this change — the
`if (expression.WaitFor is { } waitFor)` block is skipped entirely.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj --filter "FullyQualifiedName~WaitFor"`
Expected: PASS (all `WaitFor`-named tests across both parser and evaluator files).

- [ ] **Step 5: Run the full TestScript test suite to check for regressions**

Run: `dotnet test test/Ignixa.TestScript.Tests/Ignixa.TestScript.Tests.csproj`
Expected: PASS, 0 failed, across both net9.0 and net10.0 targets. This is the critical regression check
for the `VisitOperationAsync` rewrite — every existing operation test (destination validation,
cancellation, autocreate/autodelete, header/path variable extraction, etc.) must still pass unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs test/Ignixa.TestScript.Tests/Evaluation/TestScriptEvaluatorTests.cs
git commit -m "feat(testscript): poll waitFor operations until status leaves the polling code"
```
