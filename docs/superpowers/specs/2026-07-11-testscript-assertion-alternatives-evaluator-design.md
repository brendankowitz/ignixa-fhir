# Design: Evaluator Execution for `assertionAnyOfGroup` / `assertionWhenResponseStatus`

**Date**: 2026-07-11
**Status**: Approved for implementation
**Related**: [Issue #324](https://github.com/brendankowitz/ignixa-fhir/issues/324) (umbrella design + acceptance criteria), PR #330 (parsing/validation of these two extensions, merged), `ignixa-lab`'s `docs/superpowers/specs/2026-07-10-ignixa-testscript-engine-gaps-design.md`

## Problem

PR #330 added parsing and structural validation for `assertionAnyOfGroup` and
`assertionWhenResponseStatus` — `AssertExpression.AnyOfGroupId` and `.WhenResponseStatus` are populated,
and the parser rejects malformed groups (fewer than 2 members, mismatched sourceId/direction). But
`TestScriptEvaluator` doesn't act on either field yet: `VisitAssertAsync` evaluates and records every
assertion independently, so a group of `warningOnly` alternatives still fails open on an unexpected
status — exactly the problem issue #324 was opened to fix. Until this lands, authoring a suite against
these extensions expecting strict OR semantics is a trap: the JSON parses cleanly and silently does
nothing beyond what plain adjacent `warningOnly` asserts already did.

## Decision

Add evaluator-side execution: assertions sharing a non-null `AnyOfGroupId` within one test aggregate into
a single strict OR result. `WhenResponseStatus` is evaluated generically for *any* assertion (grouped or
not) — an assertion whose condition doesn't match is **not applicable** (skipped, not failed or passed),
independent of whether it belongs to a group. This is broader than issue #324's motivating example
(Subscription delete readback, always paired with a group) but the parser doesn't enforce that pairing,
and a standalone conditional assertion is a reasonable, useful primitive on its own.

Both paths — standalone and grouped — route through one new evaluation primitive so conditional semantics
behave identically either way (see Evaluation below).

## Data Model

### `Ignixa.TestScript.Reporting.AssertionOutcome` (`src/Core/Ignixa.TestScript/Reporting/AssertionOutcome.cs`)

Add one field, default preserves every existing call site:

```csharp
public sealed record AssertionOutcome(
    bool Passed,
    bool WarningOnly,
    string? Message = null,
    bool IsError = false,
    bool Applicable = true);
```

### New: `AssertionGroupMemberResult` (`src/Core/Ignixa.TestScript/Reporting/AssertionGroupMemberResult.cs`)

One-record-per-file style, matching `ResponseStatusCondition.cs`/`WaitForCondition.cs` precedent:

```csharp
namespace Ignixa.TestScript.Reporting;

/// <summary>
/// One member's outcome within an <c>assertionAnyOfGroup</c> aggregate. Surfaced as diagnostic detail
/// alongside the group's single top-level <see cref="ActionResult"/> — members are never reported as
/// independent top-level results.
/// </summary>
public sealed record AssertionGroupMemberResult(
    string? Description,
    bool Applicable,
    bool Passed,
    string? Message);
```

### `Ignixa.TestScript.Reporting.ActionResult` (`src/Core/Ignixa.TestScript/Reporting/ActionResult.cs`)

Add two optional fields, defaults preserve every existing call site:

```csharp
public sealed record ActionResult(
    string? Label,
    string? Description,
    TestScriptOutcome Outcome,
    string? Message = null,
    TimeSpan Duration = default,
    TestActionKind Kind = TestActionKind.Assertion,
    HttpExchange? Exchange = null,
    string? GroupId = null,
    IReadOnlyList<AssertionGroupMemberResult>? Members = null);
```

### `Ignixa.TestScript.Reporting.ITestScriptResultRecorder` / `TestScriptResultRecorder`

One new method (only one implementer in the codebase today — `TestScriptResultRecorder` — so this is a
low-blast-radius interface change). It takes the same `AssertionOutcome` shape `RecordAssertionResult`
already does, rather than a raw `TestScriptOutcome`, so the two methods share one
pass/warning/fail/error decision rule instead of duplicating it:

```csharp
void RecordAssertionGroupResult(
    string groupId,
    string? label,
    string? description,
    AssertionOutcome outcome,
    IReadOnlyList<AssertionGroupMemberResult> members);
```

`TestScriptResultRecorder.RecordAssertionResult` (line 53) has its `outcome.IsError`/`Passed`/
`WarningOnly` → `TestScriptOutcome` mapping extracted into a small private static helper (e.g.
`DetermineOutcome(AssertionOutcome)`); `RecordAssertionGroupResult` calls the same helper, then appends an
`ActionResult` with `Kind = TestActionKind.Assertion`, `GroupId = groupId`, `Members = members`. The
aggregate's own `Applicable` is always `true` — inapplicability lives on individual members, not the
group result itself. A "no member was applicable" or "a member's sourceId failed to resolve" aggregate
uses `IsError: true`; a resolved-but-all-failed aggregate uses plain `Passed: false`.

## Evaluation

### Shared primitive

New private method on `TestScriptEvaluator`
(`src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`), sitting alongside
`EvaluateAssertionWithMessage` (currently at line 575):

```csharp
private (bool Applicable, bool Passed, string? Message) EvaluateAssertionMember(
    AssertExpression assertion, TestScriptContext context)
{
    if (assertion.WhenResponseStatus is { } condition)
    {
        if (!context.ResponseHistory.TryGetValue(condition.SourceId, out var response))
            throw new InvalidOperationException(
                $"assertionWhenResponseStatus sourceId '{condition.SourceId}' refers to no known response");

        if (!condition.Statuses.Contains(response.StatusCode))
            return (false, false, null);
    }

    var (passed, message) = EvaluateAssertionWithMessage(assertion, context);
    return (true, passed, message);
}
```

Throwing on an unresolvable `sourceId` reuses the exact pattern `ResolveAssertionResponse` (line ~607) and
`ResolveAssertionRequest` (line ~616) already use for missing sourceIds elsewhere in this file — it's
caught by `VisitAssertAsync`'s existing `catch (Exception ex)` (line 410) and recorded as
`IsError: true`. This is what makes a forward reference (an operation later in the test, not yet in
`ResponseHistory` when this assert runs) surface as an execution error rather than silently "not
applicable" — matching issue #324's acceptance criteria.

### Standalone path (`VisitAssertAsync`, currently line 395)

```csharp
public ValueTask<TestScriptContext> VisitAssertAsync(
    AssertExpression expression, TestScriptContext context, CancellationToken cancellationToken)
{
    try
    {
        var (applicable, passed, message) = EvaluateAssertionMember(expression, context);
        context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
            new AssertionOutcome(passed, expression.WarningOnly, applicable ? message : null,
                Applicable: applicable));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
            new AssertionOutcome(false, expression.WarningOnly, ex.Message, IsError: true));
    }
    return ValueTask.FromResult(context);
}
```

This only fires for assertions with **no** `AnyOfGroupId` — grouped assertions are intercepted one level
up, in `ExecuteActionsAsync`, and never reach the per-action visitor dispatch for recording purposes (see
below). `TestScriptResultRecorder.RecordAssertionResult` (line 53) gets one new line: when
`!outcome.Applicable`, the outcome maps to `TestScriptOutcome.Skip` ahead of the existing
error/pass/warning/fail checks — this is the only recorder-side change needed for the standalone case.

### Grouped path (`ExecuteActionsAsync`, currently line 302)

Restructure to precompute group membership up front (mirrors what
`TestScriptParser.ValidateAssertionGroups` already does at parse time) and special-case grouped asserts:

```csharp
private async Task<TestScriptContext> ExecuteActionsAsync(
    IReadOnlyList<ActionExpression> actions,
    IReadOnlyList<VariableDefinition> variables,
    TestScriptContext context,
    CancellationToken cancellationToken)
{
    var lastGroupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
    for (var i = 0; i < actions.Count; i++)
        if (actions[i] is AssertExpression { AnyOfGroupId: { } gid })
            lastGroupIndex[gid] = i;

    var pendingGroups = new Dictionary<string, List<(AssertExpression Assertion, bool Applicable, bool Passed, string? Message)>>(
        StringComparer.Ordinal);

    for (var i = 0; i < actions.Count; i++)
    {
        var action = actions[i];

        if (action is AssertExpression { AnyOfGroupId: { } groupId } assertion)
        {
            var (applicable, passed, message) = EvaluateGroupMemberSafe(assertion, context);
            if (!pendingGroups.TryGetValue(groupId, out var members))
                pendingGroups[groupId] = members = [];
            members.Add((assertion, applicable, passed, message));

            if (i == lastGroupIndex[groupId])
                RecordGroupResult(context, groupId, members);

            continue;
        }

        context = await action.AcceptAsync(this, context, cancellationToken);
        if (action is OperationExpression)
            context = VariableExtractor.ExtractFromResponse(variables, context, schemaProvider);
    }

    return context;
}
```

`EvaluateGroupMemberSafe` wraps `EvaluateAssertionMember` with the same try/catch shape
`VisitAssertAsync` uses today, so a `sourceId` resolution failure on one member surfaces as that member's
`Message` with an error flag rather than throwing out of `ExecuteActionsAsync` — the group's aggregate
outcome computation (below) treats an errored member as inapplicable-with-error, distinct from
not-applicable-by-condition, so "one member's condition referenced a bad sourceId" doesn't get silently
absorbed into "no applicable member passed."

`RecordGroupResult` aggregates:

- Any member recorded an evaluation error → aggregate `Error`, message names the failing member.
- No member is applicable (all conditions failed to match, none errored) → aggregate `Error`,
  `"assertionAnyOfGroup '{groupId}': no member was applicable — condition(s) never matched"` (issue #324:
  "an OR group with no applicable members is an execution error").
- At least one applicable member passed → aggregate `Pass`, message names the matched member's
  description. Individual members' own `warningOnly` flags never weaken this — issue #324's core
  requirement.
- Applicable members exist but none passed → aggregate `Fail`, message summarizes each applicable
  member's actual result.

Each branch constructs the appropriate `AssertionOutcome` (see above) and calls
`recorder.RecordAssertionGroupResult(groupId, label, description, outcome, members.Select(ToGroupMemberResult).ToList())`.
Label/description use the group's **matched** member on pass, otherwise the first member — there's no
independently meaningful "group label" in the source TestScript, so this keeps the top-level entry
human-readable without inventing new authoring surface.

## Known Limitation (explicitly out of scope for this PR)

Issue #324 states cross-test `sourceId` references should be invalid. `TestScriptContext.ResponseHistory`
is not scoped per-test today — `context` threads across the whole `ExecuteAsync` run, including from one
test's actions into the next's (`TestScriptEvaluator.cs` line ~124). A condition referencing a prior
test's `responseId` will resolve rather than error. This is a pre-existing property of `TestScriptContext`
un­related to the OR-group/conditional feature itself, and scoping `ResponseHistory` per test is a larger,
separately-motivated change (it would also affect plain `sourceId` assertion/response resolution, which
already has the same cross-test-reachability behavior today, group feature or not). Called out here and
in the PR description rather than silently left undocumented; not fixed in this change.

## Reporting: FHIR `TestReport` Rendering

`TestReportResourceGenerator.GenerateAction` (`src/Core/Ignixa.TestScript/Reporting/TestReportResourceGenerator.cs`,
line 59) gains: when `action.Members is { Count: > 0 }`, attach each member as a child `extension` on the
action's JSON object (url `http://ignixa.io/testscript/assertionGroupMember`, each carrying the member's
description/applicable/passed/message as nested extensions) rather than emitting them as separate
top-level `action` entries. This keeps `TestReport.setup/test[].action` a valid FHIR resource — the spec
has no native concept of a grouped assertion result — while member diagnostics remain inspectable rather
than disappearing into the aggregate's single `message` string.

## Testing

**Evaluator** (new test file, e.g. `test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesTests.cs`,
following the existing `TestScriptEvaluatorTests.cs` fixture-building conventions):

- Group where the preferred (first) member passes → aggregate `Pass`, matched member is the first.
- Group where only the fallback member passes → aggregate `Pass`, matched member is the fallback, no
  warning noise recorded despite the fallback's own `warningOnly: true`.
- Group where no member passes → aggregate `Fail`, message lists all applicable members' actual results.
- Group where a conditional member's condition never matches and no other member passes → aggregate
  `Error` ("no member was applicable").
- Group with a member whose `WhenResponseStatus.SourceId` doesn't resolve → aggregate `Error` naming that
  member, not silently treated as inapplicable.
- Standalone (no group) assertion with `WhenResponseStatus` whose condition matches → evaluated and
  recorded normally (`Pass`/`Fail` per its own criteria).
- Standalone assertion with `WhenResponseStatus` whose condition doesn't match → recorded as `Skip`, not
  `Pass` or `Fail`.
- Regression: a test with zero grouped/conditional assertions → byte-identical recorded results to today
  (no behavior change for the other 264 existing tests).

**Worked example fixture**: one `.json` TestScript fixture (mirroring issue #324's own
`deleted-resource-readback`/`subscription-delete-readback` example) exercised through
`TestScriptEvaluator.ExecuteAsync` end-to-end via a fake `ITestRequestProvider`, asserting on the final
`TestScriptReport` shape — a living usage reference beyond the unit-level tests above.

## Out of Scope

- Migrating any of `ignixa-lab`'s workaround components (`WarningOnlyStatusAlternativeEnforcer`,
  `StatusAlternativeEnforcementPlan`, `TestScriptContentNormalizer`, `RunScopedDefinitionPreparer`) — those
  live in a different repository and are a separate follow-up once this ships in a release.
- Shorthand normalization and consistent variable interpolation (issue #324's other two gaps) — untouched
  by this change.
- Scoping `TestScriptContext.ResponseHistory` per test (see Known Limitation above).
