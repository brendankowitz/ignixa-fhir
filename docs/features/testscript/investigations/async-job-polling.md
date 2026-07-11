# Investigation: Async Job Polling Assertions

**Feature**: testscript
**Status**: Superseded
**Created**: 2026-07-11

## Approach

FHIR TestScript has no native concept of a long-running operation. Every `operation`/`assert` pair is
synchronous request-response. That's a real gap for testing `$export`, `$import`, and (once built)
`$reindex`, all of which return `202 Accepted` + `Content-Location` immediately and require polling a
status endpoint until the job reaches a terminal state — exactly the `WaitForJobCompletionAsync` pattern
`microsoft/fhir-server`'s C# E2E tests hand-roll per call site (see
[`ReindexTests.cs`](https://github.com/microsoft/fhir-server/blob/main/test/Microsoft.Health.Fhir.Shared.Tests.E2E/Rest/Reindex/ReindexTests.cs)).

Proposed: a new operation-level custom extension, `http://ignixa.io/testscript/waitFor`, following the
same pattern already established by `assertionAnyOfGroup` / `assertionWhenResponseStatus`
(ADR 2607) — parse into a typed condition on `OperationExpression`, evaluate it as a loop in
`TestScriptEvaluator.VisitOperationAsync`.

**Extension shape** (child extensions, same style as `assertionWhenResponseStatus`):

| Child | Type | Meaning |
|---|---|---|
| `sourceId` | valueString | Optional. Poll the URL from an earlier response's `Content-Location` header (the `$export`/`$import` kickoff response), instead of the operation's own `url`/`params`. |
| `statusPath` | valueString | Path to the status field in the polled response body. Default `status` — both `GetJobStatusResult`-backed export/import status bodies expose a top-level `status` string (`ExportEndpoints.cs:360-395`, `ImportEndpoints.cs:206-281`). |
| `terminalStatus` | valueString (repeatable) | Statuses that stop polling, e.g. `Completed`, `Failed`, `Cancelled`. |
| `maxAttempts` | valueInteger | Default ~60. |
| `intervalMs` | valueInteger | Default ~1000. |

**Evaluator change**: `VisitOperationAsync` gains a branch — if the operation carries a `WaitFor`
condition, resolve the polling URL once (from `sourceId`'s stored `Content-Location`/`Location` header,
via the same `context.ResponseHistory` lookup `ResolveAssertionResponse` already uses), then loop:
`GET`, extract status, compare to `terminalStatus`, sleep `intervalMs` (cooperating with
`cancellationToken`) if not terminal and attempts remain. Store the final response under the operation's
`responseId` exactly as today, so downstream `assert` actions work unmodified. On attempt exhaustion,
record an `OperationOutcome` failure ("timed out waiting for job completion after N attempts") through
the existing recorder — no new reporting plumbing needed.

## Tradeoffs

| Pros | Cons |
|------|------|
| Reuses the exact extension-parsing pattern already proven and merged for `assertionAnyOfGroup`/`assertionWhenResponseStatus` (PR #330) — reviewers already know the shape | Status body isn't a FHIR resource (it's a Bulk-Data-flavored ad hoc JSON), so `statusPath` can't reuse the FHIRPath-over-typed-element machinery (`element.ToElement(schemaProvider)`) that every other assertion uses — needs a raw-JSON-pointer style extractor, a genuinely new code path |
| Keeps `operation`/`assert` actions unchanged downstream — the rest of the test just sees a normal completed response | `$reindex` isn't implemented server-side yet (`BackgroundJobType.Reindex` is a placeholder enum value, no endpoint) — this investigation can only be validated end-to-end against `$export`/`$import` today |
| No new fixture/provider abstraction required — reuses `ITestRequestProvider` as-is | No existing polling helper anywhere in the C# test suite to crib from (`grep` for `WaitForJob*`/`PollUntil*` across `test/` = 0 hits) — this is genuinely greenfield design, not a port of an existing internal pattern |
| Generic across any 202+Content-Location job, not just reindex | Polling inside a single `operation` action call blocks that action's execution for up to `maxAttempts * intervalMs` — needs a sane default ceiling so a hung server doesn't hang the whole TestScript run past normal CI timeouts |

## Alignment

- [x] Follows architectural layering rules — stays entirely inside `Ignixa.TestScript` (Core), no `Ignixa.Api`/`Hl7.Fhir.*` dependency introduced
- [x] Developer Experience — a TestScript author writes one extension block, gets retry-until-terminal for free; no C# code needed
- [ ] Specification compliance — this is explicitly outside FHIR TestScript's spec (no such primitive exists); must be clearly documented as an Ignixa custom extension, same disclaimer as ADR 2607's other extensions
- [x] Consistent with existing patterns — mirrors `assertionWhenResponseStatus`'s extension-tree parsing exactly (`TestScriptParser.cs` `ParseResponseStatusCondition`-style child-extension walk)

## Evidence

- `$export` kickoff: `src/Application/Ignixa.Api/Endpoints/ExportEndpoints.cs:28-45` — `POST /$export` (and tenant/Group variants), returns `Results.Accepted(statusUrl, { jobId, status: "queued" })` with `Content-Location` set (~line 200/320).
- `$export` status: `GET /tenant/{tenantId}/_export/{jobId}` — `ExportEndpoints.cs:40,341-401`, backed by `GetJobStatusQuery` (`src/Application/Ignixa.Application.BackgroundOperations/Jobs/GetJobStatusQuery.cs:14-30`) — `JobType` is a plain string, not a shared enum, so `$reindex` support would slot in the same way once it exists.
- `$import` mirrors `$export` exactly: `src/Application/Ignixa.Api/Endpoints/ImportEndpoints.cs:33-42` (kickoff), `:37,179-287` (status), `:206-281` (response body shape).
- `$reindex` does not exist server-side: no endpoint under `src/Application/Ignixa.Api/Endpoints`; `BackgroundJobType.Reindex = 4` is a forward-declared enum value only (`src/Application/Ignixa.Domain/Models/BackgroundJobType.cs:37`). `docs/features/reindex/readme.md` has picked DurableTask orchestration (same pattern as export/import) but implementation hasn't started.
- `TestScriptParser.cs:387` reads `type.code` as a free string with no whitelist — no parser change needed to accept an operation `type` of `$export`/`$reindex`; only Evaluator-side handling is missing.
- No existing async-job polling test helper anywhere in `test/` (checked `WaitForJob*`, `PollUntil*`, `_export/`, `_import/`, `GetExportStatus`, `GetImportStatus` — zero hits); `test/Ignixa.Api.E2ETests` has no export/import tests yet either.

## Verdict

**Superseded** — the actual implementation (see [design spec](../../../superpowers/specs/2026-07-11-testscript-waitfor-operation-design.md)) took a simpler approach: polling keys off the response's HTTP status code directly rather than a body `statusPath`, and the polling URL comes from TestScript's existing header-extraction `variable` mechanism rather than a new `sourceId`-based lookup. Both simplifications avoid problems this investigation raised (the non-FHIR-resource body, and duplicating existing variable-extraction logic). The rest of this document is retained as historical record of the alternatives considered.

*Pending evaluation.* Viable pattern, consistent with the recently-merged extension precedent, but two things should be resolved before committing to an ADR: (1) how `statusPath` extracts from a non-FHIR-resource JSON body without a parallel raw-JSON evaluator, and (2) whether this is worth building against `$export`/`$import` now or should wait until `$reindex` actually exists, since reindex was the motivating example.
