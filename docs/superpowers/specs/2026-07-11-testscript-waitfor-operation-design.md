# Design: `waitFor` Operation Extension for TestScript

**Date**: 2026-07-11
**Status**: Approved for implementation
**Related**: [async-job-polling investigation](../../features/testscript/investigations/async-job-polling.md), [ADR 2607: TestScript Extensions](../../adr/adr-2607-testscript-extensions.md), PR #330 (`assertionAnyOfGroup`/`assertionWhenResponseStatus`)

## Problem

FHIR TestScript has no primitive for testing long-running async operations (`$export`, `$import`,
eventually `$reindex`). These return `202 Accepted` immediately and require polling a status endpoint
until the job completes — the same pattern `microsoft/fhir-server`'s C# E2E tests implement by hand via a
`WaitForJobCompletionAsync` helper. Ignixa's TestScript engine has no equivalent, so authoring a test for
these operations isn't currently possible without writing custom C# around the TestScript engine.

## Decision

Add a new operation-level custom extension, `http://ignixa.io/testscript/waitFor`, following the same
extension-parsing precedent already established by `assertionAnyOfGroup`/`assertionWhenResponseStatus`
(merged in PR #330). An operation carrying this extension is retried — the exact same request, sent again
— while its response's HTTP status code equals a configurable "still working" code, up to a configurable
attempt ceiling, sleeping a configurable interval between attempts.

**Key simplification over the original investigation doc**: `$export`/`$import`'s status endpoint signals
completion via the HTTP status code itself (`202` while running, `200` once the manifest is ready) — not
via a JSON body field. Polling on status code means the response body never needs inspecting during the
loop, avoiding the problem the investigation flagged (the job-status body isn't a FHIR resource, so
FHIRPath-based assertions don't apply to it). Once polling stops, the *existing* `response`/`responseCode`
assertion criteria already handle checking the final status — no new assertion type needed.

**Also simplified**: the polling URL is not resolved via a new `sourceId`-based header lookup. TestScript
already supports extracting a response header into a variable (the existing `variable` mechanism). A test
author extracts `Content-Location` from the kickoff operation into `${statusUrl}` using that
already-working feature, then the polling operation just targets `url: "${statusUrl}"` like any normal
operation. `waitFor` only adds the retry-until-status-changes behavior — it doesn't touch URL resolution
at all.

## Data Model

New file `src/Core/Ignixa.TestScript/Expressions/WaitForCondition.cs`, mirroring the existing
`ResponseStatusCondition.cs` one-record-per-file style:

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

`OperationExpression` (`src/Core/Ignixa.TestScript/Expressions/OperationExpression.cs`) gains one new
property:

```csharp
public WaitForCondition? WaitFor { get; init; }
```

## Parsing

`TestScriptParser.ParseOperation` (`src/Core/Ignixa.TestScript/Parsing/TestScriptParser.cs:385`) does not
currently read `op["extension"]` at all. Add that, plus:

```csharp
private const string WaitForUrl = "http://ignixa.io/testscript/waitFor";
```

New helper, modeled on `ParseResponseStatusCondition`'s child-extension walk:

```csharp
private static WaitForCondition? ParseWaitForCondition(JsonArray? extensions, string path, List<ParseError> errors)
{
    var ext = extensions?.OfType<JsonObject>().FirstOrDefault(e => e["url"]?.GetValue<string>() == WaitForUrl);
    if (ext is null) return null;

    var pollingStatusCode = ReadIntChild(ext, "pollingStatusCode", 202);
    var maxAttempts = ReadIntChild(ext, "maxAttempts", 60);
    var intervalMs = ReadIntChild(ext, "intervalMs", 1000);

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
```

(`ReadIntChild` is a small local helper reading a named child extension's `valueInteger`, falling back to
the given default when the child is absent — exact signature is an implementation detail, not a design
decision.)

Wire into `ParseOperation`: `WaitFor = ParseWaitForCondition(op["extension"]?.AsArray(), path, errors)`.

## Evaluation

`TestScriptEvaluator.VisitOperationAsync` (`src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs:318`)
currently builds the request once, sends it once, records the outcome. Restructure so the
build-once/send-and-record-one-attempt logic is reusable, then loop when `WaitFor` is set:

- Build the request once (unchanged — URL/headers/body don't change between polling attempts).
- Send it. If `expression.WaitFor is null`, behave exactly as today (no behavior change for existing
  tests).
- If `expression.WaitFor is { } waitFor`: after each send, if `response.StatusCode == waitFor.PollingStatusCode`
  and attempts remain, `await Task.Delay(waitFor.IntervalMs, cancellationToken)` and send again. Otherwise
  stop polling and record the normal success outcome (same as today — store request/response, extract
  variables, etc.).
- If attempts are exhausted while still polling: record an `OperationOutcome` failure via the existing
  `context.Recorder.RecordOperationResult` call — `"Timed out waiting for job completion after {N}
  attempts (last status: {code})"` — no new reporting type needed, this is the same failure path every
  other operation failure already uses.

`OperationCanceledException` from `Task.Delay` when `cancellationToken` fires propagates the same way
existing `OperationCanceledException` handling in this method already does — no special casing.

## Error Handling

- Malformed extension values (out-of-range `pollingStatusCode`, non-positive `maxAttempts`, negative
  `intervalMs`) are parse-time errors, matching the existing pattern for `assertionWhenResponseStatus`'s
  status range validation — fail the parse, don't silently clamp.
- A timeout (attempts exhausted, still polling) is an operation-time failure, not a parse-time one —
  recorded as a normal failed `OperationOutcome`, which fails the test the same way any other failed
  operation does today.
- Network/transport exceptions during any individual polling attempt are handled by the existing
  try/catch in `VisitOperationAsync` — no new exception handling path; a transient network blip during
  polling surfaces as today's existing operation-failure behavior, it does not get its own retry-on-error
  semantics (only retry-on-specific-status-code is in scope here).

## Testing

**Parser** (`test/Ignixa.TestScript.Tests/Parsing/TestScriptParserTests.cs`):
- Extension present with no children → all three fields default to 202/60/1000.
- Extension present with explicit children → values parsed correctly.
- Each of the three fields out of range individually → a `ParseError` mentioning that field.
- Operation with no `waitFor` extension → `WaitFor` is `null` (no regression in existing operation
  parsing tests).

**Evaluator** (new test file alongside existing evaluator tests):
- Fake `ITestRequestProvider` returning 202 for the first N sends, then 200 → assert exactly N+1 sends
  occurred and the final stored response is the 200 one.
- Fake provider that always returns 202 → assert exactly `MaxAttempts` sends occurred and the operation's
  recorded outcome is a failure mentioning "Timed out".
- Operation with no `WaitFor` → assert exactly one send occurs (regression check for the
  `VisitOperationAsync` refactor — existing behavior must be provably unchanged).

## Out of Scope (per investigation + brainstorming decisions)

- `$reindex` itself — doesn't exist server-side yet; this feature is generic and will work against it
  once it does, but isn't validated against it in this round.
- Any JSON-body-based terminal-status check (`statusPath`) — rejected in favor of HTTP-status-code
  polling; can be revisited later if a future job signals completion only through its body, but no such
  case exists in this codebase today.
- Subscription/callback verification — separate investigation
  ([subscription-callback-verification.md](../../features/testscript/investigations/subscription-callback-verification.md)),
  explicitly deferred pending a Subscriptions ADR.
