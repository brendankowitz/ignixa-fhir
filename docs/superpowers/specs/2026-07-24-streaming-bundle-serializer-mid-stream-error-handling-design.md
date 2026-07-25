# StreamingBundleSerializer mid-stream error handling — design

**Status:** revised after adversarial review round 6; the two blocking items from that round (§6's post-guard throw sources, and the warning-`fullUrl` specification that fell out of the round-5 rewrite) are addressed in §9. Round 6 stated these fixes are verifiable by inspection without a further full review round.
**Date:** 2026-07-24
**Branch:** `worktree-ignixa-datalayer-sqlserver`

## Problem

All four `StreamingBundleSerializer.Serialize*Async` methods wrap a `FhirJsonWriter` (over `Utf8JsonWriter`) in `await using`, then enumerate results with an unprotected `await foreach`. When anything throws mid-loop:

1. The exception unwinds through `await using`'s compiler-generated `finally`.
2. That calls `writer.DisposeAsync()`, which **unconditionally flushes the writer's buffered, syntactically incomplete JSON** to the live HTTP response stream.
3. That write sets `HttpContext.Response.HasStarted = true`.
4. `FhirExceptionMiddleware` catches the exception, logs it (`:36-45`), checks `HasStarted` (`:52-57`), sees `true`, and returns without writing a replacement body.

Net result: a truncated, unparseable HTTP 200 instead of a clean 400/500. Confirmed by isolated repro and a live reproduction against a real search request.

This surfaced as 17 of the 32 remaining E2E failures on this branch, triggered by two already-tracked compiler gaps (`identifier:of-type`, system-level `:not`). But **the truncation is a separate, general defect**: any exception during serialization — SQL timeout, dropped connection, corrupt stored resource bytes, a future compiler gap — produces the same corrupt response.

## Goals

- **Valid JSON on every path.** No code path may dispose the writer with structurally incomplete buffered content.
- Where nothing has reached the client, fail as a **proper status-coded error**, not a 200.
- Where the response has started, complete the body with a fatal `OperationOutcome` in a **FHIR-conformant** shape for the bundle type and version.
- Happy-path output byte-identical to today, with three deliberate carve-outs: the Stu3 history `response` fix (§4), the adjacent helper corrections (§9), and `_pretty=true` whitespace (§1).

## Non-goals

- Fixing the two tracked compiler modifier gaps (`identifier:of-type`, system-level `:not`). This changes only how their failures are reported.
- Changing `FhirExceptionMiddleware`. Its `HasStarted` guard is correct; the bug is the serializer wrongly making `HasStarted` true.
- **Broader history-bundle FHIR conformance** — see §9.

## Established facts

Verified empirically on .NET 10 across multiple independent review rounds, and against current source:

- **`Utf8JsonWriter` refuses to emit mismatched-depth JSON.** From a mid-entry state, `WriteEndArray()`/`WriteEndObject()` throw `InvalidOperationException`. This is what makes §1 necessary.
- **`Utf8JsonWriter.Reset()` discards all pending bytes**, including from a broken state; subsequent disposal writes zero bytes. It resets depth to 0 and cannot close an in-progress structure.
- **`WriteRawValue(bytes, skipInputValidation: true)` into an open array emits correct comma-separated elements** (measured: three buffered entries parse as a three-element array). It still throws `InvalidOperationException` if called where no value is legal, so the validation skip trusts only payload content — not position.
- **A scratch `Utf8JsonWriter`'s buffer is empty until `Flush()`** (measured: `WrittenCount == 0` while `BytesPending == 70`). The copy-out must flush first.
- **`Reset()` + `ArrayBufferWriter.Clear()` between entries is safe**; no state bleeds into the next entry. `ArrayBufferWriter` does **not** shrink, so peak retention equals the largest single entry for the request's lifetime — acceptable, since `SearchEntryResult.ResourceBytes` already materializes each resource in full.
- **`BytesCommitted` distinguishes the two worlds.** `SerializeWithPaginationAsync` flushes only above 50 MB pending (`:97`, `:213-216`), so for virtually every real failure — including all 17 E2E cases — `BytesCommitted == 0` and `Response.HasStarted == false`. Setting `ContentType` does not start the response. The threshold check reads the main writer's `BytesPending`, which still grows by each copied blob, so it remains meaningful under §1.
- **`SerializeHistoryAsync` and `SerializeAsync` flush per entry** (`:345`, `:86`), so `BytesCommitted > 0` from the second entry onward.
- **`FlushAsync(canceledToken)` throws `OperationCanceledException` immediately.**
- **`FhirJsonWriter.WriteRawProperty` validates** (`skipInputValidation: false`, `FhirJsonWriter.cs:204`) — the throw source for corrupt stored bytes, deliberately retained.
- **`FhirJsonWriter` only constructs over a `Stream`** (`FhirJsonWriter.cs:30-49`); §1 requires an `IBufferWriter<byte>`-based construction path.
- `FhirJsonWriter.UnderlyingWriter` (`:38`) is `internal`, same assembly — reachable.
- `WriteBundleFooterAsync` (`:900-906`) closes entry array and bundle object together; callers `SerializeAsync` (`:90`) and `SerializeStreamAsync` (`:438`).
- `IssueComponent` is a positional record (`SearchOptions.cs:178-184`); `BundleLink.Url` is `string?` (`BundleLink.cs:51`).
- **Caller contracts:** the seven `SerializeWithPaginationAsync` call sites and the three `SerializeHistoryAsync` call sites are unwrapped, so rethrowing changes nothing for them. **`SerializeStreamAsync` is the exception** — `FhirEndpoints.cs:1149-1191` wraps it in `try { … } catch (Exception ex) { logger.LogError(…); return Results.StatusCode(500); }` and calls `await streamingContext.CompleteAsync()` (`:1180`) after it. §8 depends on this.

### FHIR constraints governing the error entry

This codebase supports Stu3, R4, R4B, and R5, and the bundle invariants differ between them, including their keys. Three earlier revisions of this document got citations wrong. What follows was fetched per version from the `bundle.profile.json` StructureDefinitions (3.0.2, 4.0.1, 4.3.0, 5.0.0) and value set pages, and independently re-verified in round 5 with no discrepancies.

**Stu3, R4, and R4B share these keys and texts:**
- **`bdl-5`** — entry content: `resource.exists() or request.exists() or response.exists()`.
- **`bdl-7`** — `fullUrl` uniqueness: unique within a bundle, or entries sharing a `fullUrl` must differ in `meta.versionId`. **R4/R4B exempt history bundles; Stu3 does not.**
- **`bdl-2`** — `entry.search` only when `type = 'searchset'`.
- **`bdl-8`** — `fullUrl.contains('/_history/').not()`.

**Where Stu3 and R4/R4B diverge — the one genuine conflict:**
- Stu3 **`bdl-4`**: `entry.response.empty() or type = 'batch-response' or type = 'transaction-response'` — **`response` prohibited in history bundles.**
- R4/R4B **`bdl-4`**: *"entry.response mandatory for batch-response/transaction-response/history, otherwise prohibited"* — **required.** R4 reversed Stu3.
- Stu3 **`bdl-3`**: `entry.request.empty() or type = 'batch' or type = 'transaction' or type = 'history'` — `request` permitted for history.
- R4/R4B **`bdl-3`**: *"entry.request mandatory for batch/transaction/history, otherwise prohibited."*

**R5 renumbers entirely** (no `bdl-3`, `bdl-4`, or `bdl-6`):
- **`bdl-16`**: *"Issue.severity for all issues within the OperationOutcome must be either 'information' or 'warning'."* — a `fatal` issue cannot go in `Bundle.issues`.
- **`bdl-3a`**: `document`/`message`/`searchset`/`collection` entries must contain resources and must not have `request`/`response`.
- **`bdl-3b`**: `type = 'history' implies entry.all(request.exists() and response.exists() and ((request.method in ('POST' | 'PATCH' | 'PUT')) = resource.exists()))`.
- **`bdl-3d`**: `transaction-response`/`batch-response` entries must contain `response`.
- **`bdl-14`** (no PATCH in history), **`bdl-15`** (`fullUrl` populated), **`bdl-18`** (self link required for searchsets).

**All four versions:** `SearchEntryMode` is `match | include | outcome`. `Bundle.entry.response.outcome` exists (0..1, `Resource`), defined as *"An OperationOutcome containing hints and warnings produced as part of processing this entry in a batch or transaction."* Carrying a **fatal** outcome there in a **history** bundle is off-label against that wording, but no invariant in any version constrains `response.outcome` by bundle type or severity, and real servers place transaction errors there. Accepted deliberately.

## Design

### 1. Per-entry buffering — the structural fix

Each entry is written into a **scratch writer over a reusable `ArrayBufferWriter<byte>`**, not directly into the response writer. Once the entry is complete, the scratch writer is **flushed** (its buffer is empty until then) and its bytes are copied into the main writer as one raw array element via `WriteRawValue(scratchBytes, skipInputValidation: true)` — the content was produced by this same code, so re-validation is waste. The scratch writer is `Reset()` and the buffer `Clear()`ed between entries, so one allocation serves the whole enumeration.

This requires an `IBufferWriter<byte>`-based construction path on `FhirJsonWriter`, which today only builds over a `Stream` (`FhirJsonWriter.cs:30-49`). The loop bodies call `FhirJsonWriter`-typed helpers (`WriteResourceBytes`, `ResourceElementsSerializer.WriteFilteredResourceProperty`, the fluent API), so the scratch writer must be the same type.

**This is what makes valid JSON achievable on every path.** The main writer only ever transitions between complete entries, so it is never mid-entry when an exception arrives. A failure inside entry writing — `WriteRawProperty`'s validating `WriteRawValue` on corrupt stored bytes, or `ResourceElementsSerializer.WriteFilteredResourceProperty` failing at arbitrary nested depth (`:616`) — dirties only the scratch buffer, which is discarded. Validation of stored resource bytes is preserved: it still runs, still throws, and now fails safely.

**Costs, both accepted deliberately:**
- One extra copy of each entry's bytes, on a path documented as zero-copy passthrough. For a page of 20-100 resources at typical FHIR sizes this is a few hundred KB per request, negligible against database and network cost.
- **`_pretty=true` output is no longer byte-identical**: the scratch writer indents from depth 0, so entry internals lose their outer nesting indentation. The result is valid, correctly-indented-at-top-level JSON differing only in whitespace within entries. `_pretty` is reachable in production (`CompartmentEndpoints.cs:268-269`, `OperationEndpoints.cs:503`). Golden-snapshot tests pin `pretty=false`.

### 2. Two-tier recovery, keyed on `BytesCommitted`

**Tier 1 — nothing committed (`BytesCommitted == 0`).** `Reset()` the main writer to discard the buffer, then **rethrow**. `FhirExceptionMiddleware` sees `HasStarted == false` and produces a correct status code with a version-correct `OperationOutcome`. Virtually all `SerializeWithPaginationAsync` failures take this path.

**Tier 2 — response already started (`BytesCommitted > 0`).** The status line is committed. Write the error entry (§3), close the structure, flush, **then rethrow**.

The rethrow produces the log record: `FhirExceptionMiddleware` logs *before* checking `HasStarted` (`:36-45`), then returns without writing (`:52-57`), so the completed body survives and the connection is not aborted. This is why no `ILogger` parameter is added. **Every failure path rethrows** — except `SerializeStreamAsync`, whose caller contract forbids it (§8).

Tier 2 implies the entry array is open: bytes are only committed from inside the loop (or from a flush that necessarily follows `WriteStartArray`), and §6 confines the guard to the array-open region. So the catch can always append an error entry.

### 3. Error entry rendering

The exception becomes one `IssueComponent("fatal", "exception", Diagnostics: $"Bundle serialization failed: {ex.Message}")`.

A new `internal` helper `WriteOperationOutcomeEntry(writer, issue, bundleType, fhirVersion, fullUrl, selfUrl)` writes one balanced entry, shaped by bundle type and — for history only — version. **Every shape includes `fullUrl` except batch/transaction-response**, which delegates to the existing `WriteErrorEntry` and is exempt from R5 `bdl-15` (that constraint scopes `fullUrl` to bundle types where entries identify resources; batch/transaction-response entries carry only operation results). The `fullUrl` argument is consequently unused on that one branch:

- **`"searchset"`** → OperationOutcome as `resource`, `search.mode = "outcome"`; no `request`/`response`. Satisfies `bdl-2`, R5 `bdl-3a`.
- **`"history"`, R4/R4B/R5** → `request` (`method: "GET"`, `url: selfUrl`), `response` (`status: "500"`) carrying the OperationOutcome in **`response.outcome`**; no `resource`, no `search`. The placement is load-bearing: R5 `bdl-3b` requires `(request.method in ('POST'|'PATCH'|'PUT')) = resource.exists()`, so `GET` with a `resource` fails while `GET` without one gives `false = false`. Satisfies R4/R4B `bdl-3`+`bdl-4` and R5 `bdl-14`.
- **`"history"`, Stu3** → OperationOutcome as `resource`, `request` (`method: "GET"`, `url: selfUrl`); **no `response`**, satisfying Stu3 `bdl-4`, with `bdl-5` met twice over. Stu3 has no method/resource correspondence rule.
- **`"batch-response"` / `"transaction-response"`** → the existing `WriteErrorEntry` shape: `response.status = "500 Internal Server Error"` (its literal current text, `:451`) plus OperationOutcome as `resource`. Satisfies Stu3/R4/R4B `bdl-4` and R5 `bdl-3d`.
- **any other type** → `fullUrl` plus OperationOutcome as `resource` only.

`selfUrl` for history resolves to the `Url` of the `links` entry whose relation is `"self"`; when `links` is null or empty, carries no self relation, **or that entry's `Url` is null or empty**, the literal `"_history"`.

**`fullUrl` uniqueness** (`bdl-7`): the error entry uses `urn:uuid:00000000-0000-0000-0000-0000000000e0`, a well-formed UUID URN distinct from the warning entry's, satisfying even Stu3's unexempted form. At most one error entry per serialization, so a constant is safe. It contains no `/_history/`, satisfying `bdl-8`.

The existing `WriteBundleIssues` / `WriteBundleIssuesPreR5` warning helpers are not touched; the error path does not flow through either.

### 4. Stu3 history conformance (happy path)

`SerializeHistoryAsync` writes a `response` element on **every** history entry for **every** version (`:336-340`), with no version branch — so every Stu3 history bundle this server has emitted violates Stu3 `bdl-4`. An earlier revision proposed inheriting that for the error entry and calling it out of scope. That was wrong: this is our serializer, nothing external forces the element, and the fix is one branch.

`response` is written for R4/R4B/R5 (where `bdl-4` requires it) and **suppressed for Stu3** (where `bdl-4` prohibits it). `request` continues for all versions.

**Information loss is real for deleted versions, and unavoidable.** For a normal entry, a conformant Stu3 client reads version and timestamp from `resource.meta.versionId` / `meta.lastUpdated`. But a deleted version has no resource bytes, so `WriteResourceBytes` (`:603-609`) emits a `{resourceType, id}` stub carrying **no `meta`** — meaning `lastModified`, today available only via `response.lastModified`, is genuinely lost for Stu3 deleted entries. The deletion signal itself survives via `request.method = "DELETE"`. This loss is forced by Stu3 `bdl-4`: a conformant Stu3 history bundle simply cannot carry per-entry response metadata. Stated here rather than glossed.

Blast radius inside the repo is zero: no test calls `SerializeHistoryAsync`, and nothing in `test/Ignixa.Api.E2ETests` asserts on `entry.response` in a history bundle or touches `_history`.

### 5. Version plumbing

`SerializeHistoryAsync` gains `ISchema? schemaProvider = null`, mirroring `SerializeWithPaginationAsync`'s existing parameter and its `schemaProvider != null ? (FhirVersion)schemaProvider.Version : FhirVersion.R4` derivation (`:140`).

**Only that method.** `SerializeAsync` and `SerializeStreamAsync` do **not** get the parameter: the searchset, batch-response, and catch-all error shapes are version-invariant, and §4 touches history only, so neither method's path would read it. An earlier revision proposed adding it to all three "for uniformity" — that is the same unused parameter this document already deleted once.

Both therefore pass `FhirVersion.R4` (the existing default at `:140`) as `WriteOperationOutcomeEntry`'s `fhirVersion` argument. The value is inert for them: it is read only on the history branch, which neither method reaches — `SerializeStreamAsync` always renders the batch-response shape, and `SerializeAsync` has no production caller and no history call site.

The three `SerializeHistoryAsync` call sites are **not** a mechanical thread-through: none of the private handlers has `versionContext`, `fhirSpec`, or `tenantConfig` in scope, and `HandleGetResourceHistory` (`HistoryEndpoints.cs:127-133`) lacks `IFhirRequestContextAccessor` entirely. Each needs `[FromServices] IFhirVersionContext` and `[FromServices] IFhirRequestContextAccessor`, `fhirSpec` derived via `FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion)` per `FhirEndpoints.cs:585-599`, and null-handling for missing tenant config. The three tenant-agnostic lambdas (`:95-97`, `:104-106`, `:112-114`) already inject the accessor and must forward it — roughly six touch points.

**Separately, two of the seven `SerializeWithPaginationAsync` call sites pass no `schemaProvider`** — `$everything` (`OperationEndpoints.cs:507-516`) and compartment search (`CompartmentEndpoints.cs:272-281`) — so `fhirVersion` falls back to R4, rendering *today's warning issues* in the pre-R5 shape for R5 tenants. A live pre-existing defect, fixed here because these are the same call sites:
- `CompartmentEndpoints`: `ExecuteSearchCompartmentAsync` has `fhirSpec`/`tenantConfig` (`:239-243`) but the file has no `IFhirVersionContext` anywhere; it must be injected and forwarded from the four wrappers (`:145`, `:184`, `:321`, `:358`).
- `OperationEndpoints`: `HandlePatientEverything` (`:435-444`) lacks `versionContext`, `fhirContextAccessor`, and `tenantId`. Both routes bind the same method group, so one signature covers both.

### 6. Guarded region

**The guard opens immediately after `FhirJsonWriter.Create` and closes just before `WriteEndArray()`.** Two consequences make this the right boundary:

- It covers the **prologue** (`WriteBundleHeader`, `WriteStartArray`, `WriteBundleIssuesPreR5` — `:163-171`), which an earlier revision left outside. A throw there (for instance `EnsureArg` rejecting an empty string in a warning issue's `Location`/`Expression`, `:749-754`) would otherwise take the dispose-flush truncation path. Such a throw is necessarily tier 1 — nothing can have been committed that early — so `Reset()`+rethrow handles it even though no entry array exists yet to hold an error entry.
- It closes **before** the footer, so every recoverable failure finds the entry array open and the catch can always append an error entry.

**Post-loop link computation is restructured, not merely moved.** `SerializeWithPaginationAsync` currently computes the continuation token and builds `nextLink`/`relatedLink` — including `new Uri(baseUrl, UriKind.Absolute)` (`:251`), a real `UriFormatException` source — *after* `WriteEndArray()` (`:219`) and after `WriteBundleIssues` (`:221`). Guarding that region is impossible without reintroducing a dead end, since an error entry cannot be appended once the array is closed.

Instead, the **pure string computation is hoisted to just before `WriteEndArray()`**, inside the guard: continuation token, `nextLink`, and `relatedLink` are computed into locals while the array is still open. Every input to that computation (`hasMore`, `currentOffset`, `pageSize`, `filteredQueryString`, `baseUrl`, `hasMoreIncludes`, `includesOffset`, `includesCount`, `searchOptions.ResourceType`) is loop or prologue state, so the hoist is mechanical. Only the writes that consume them — `WriteEndArray`, `WriteBundleIssues`, `WriteBundleLinksFromStrings`, `WriteEndObject`, final flush — remain after the guard.

**Those remaining writes are not inherently throw-free, and §9 makes them so.** Two of them pass caller-supplied strings to `FhirJsonWriter`'s validating writers, which `EnsureArg` against empty values (`FhirJsonWriter.cs:132-133`, `:143`):

- `WriteBundleIssues` — the R5 path — writes `issue.Severity`, `issue.Code`, and each `Location`/`Expression` string (`:662-693`). `SearchOptions.BundleIssues` is caller-supplied, so an empty `Location` throws here. Note the asymmetry this creates without a fix: the identical input throws *inside* the guard on R4 (via the prologue's `WriteBundleIssuesPreR5`) but *outside* it on R5, mid-`issues` object — straight back to dispose-flush truncation.
- `SerializeHistoryAsync`'s `WriteBundleLinks` (`:357`) writes `link.Url ?? string.Empty` (`:545`); `BundleLink.Url` is `string?`, and this design itself treats null/empty self-link URLs as reachable (§3, test 11).

§9 removes both throw sources at their origin rather than widening the guard — widening it cannot help, since an error entry cannot be appended once the array is closed. With those fixes, the post-guard region genuinely contains no throw source and Goal 1 holds on every path.

`SerializeHistoryAsync`'s post-loop region (`:348-361`) likewise stays outside the guard, safe once §9 lands. On its tier-2 error path the guard exits before that region, so the error bundle carries no `link` array — acceptable, since `bdl-18`'s self-link requirement is searchset-only and no history invariant requires links.

### 7. Cancellation

`catch (OperationCanceledException)` differs in one respect only: **no error entry is written** (the client is gone). Otherwise it follows the general path — tier 1 `Reset()`s and rethrows, tier 2 closes the body and rethrows. Cancellation is never swallowed.

**All tier-2 footer flushes use `CancellationToken.None`**, since `FlushAsync` on an already-canceled token throws immediately and would defeat the body completion. This does not make the flush infallible: a dead socket still raises `IOException`, which would replace the original exception. Accepted — at that point the client is unreachable either way.

### 8. `SerializeStreamAsync`: buffering only, contract preserved

It receives **per-entry buffering** (§1) — it carries the identical mid-entry flaw via its own `WriteRawProperty` call (`:504`), and scoping that out as "the batch path" was arbitrary.

It does **not** join the two-tier rethrow contract. Its existing behavior — catch, write `WriteErrorEntry`, always write the footer, never rethrow (`:408-438`) — is deliberately preserved, because its only caller (`FhirEndpoints.cs:1149-1191`) wraps it in a catch that returns `Results.StatusCode(500)` and, critically, calls `await streamingContext.CompleteAsync()` (`:1180`) afterward. Making it rethrow would produce a body-less 500 in tier 1, a secondary `InvalidOperationException` in tier 2 (a status result against a started response), convert cancellation into a 500, and **skip `CompleteAsync`, leaking background tasks on exactly the new failure path**. Buffering fixes its real bug without touching any of that.

### 9. Adjacent corrections in touched helpers

Three small fixes to helpers this work already modifies. The first two are required by §6; the third is a correctness fix in the same method.

**Empty-string tolerance in `WriteBundleIssues` and `WriteBundleIssuesPreR5`.** Both currently pass `Location` and `Expression` values straight to `WriteStringValue`, which rejects empty strings. Both must **skip** empty or whitespace-only entries instead of writing them. This removes §6's R5 post-guard throw source and, in the pre-R5 helper, converts a guarded crash into correct output. `Severity` and `Code` are non-nullable positional record components (`SearchOptions.cs:178-184`) and are left as-is.

**Empty-URL tolerance in `WriteBundleLinks`.** It currently writes `link.Url ?? string.Empty` (`:545`) into a writer that rejects empty strings. It must **skip** links whose `Url` is null, empty, or whitespace, rather than emitting a `link` entry with no usable URL. This removes §6's history post-guard throw source.

**Warning-entry `fullUrl` correction.** `WriteBundleIssuesPreR5` hardcodes `fullUrl: "urn:uuid:operation-outcome"` (`:728`). The `urn:uuid` namespace requires an RFC 4122 UUID, and `operation-outcome` is not one, so this value is malformed in every version. It becomes `urn:uuid:00000000-0000-0000-0000-0000000000d0` — a well-formed UUID URN, distinct from the error entry's `…e0` (§3), preserving `bdl-7` uniqueness when a bundle carries both.

This last one changes existing happy-path output for any searchset that already emits warning entries, which is why golden snapshots (test 17) must be captured either without warning entries or after this change lands.

### 10. Deliberately out of scope

Round 5 surfaced two further pre-existing conformance defects in the history happy path. Both are **out of scope**, stated explicitly so no reader infers that §4 achieved history conformance:

- **`bdl-8` violation, all four versions.** `SerializeHistoryAsync` writes `fullUrl = "Type/id/_history/vN"` (`:320-324`), but `bdl-8` requires `fullUrl.contains('/_history/').not()`. Fixing it means moving version identity into `resource.meta.versionId` — which the deleted-entry stub does not carry (§4) — and interacts with Stu3's unexempted `bdl-7` uniqueness rule.
- **Deleted entries violate R5 `bdl-3b`**: the `{resourceType, id}` stub means `resource.exists()` is true while `request.method` is `DELETE`.

Both are happy-path FHIR conformance bugs unrelated to error handling, both change history output for every version rather than one, and both need their own design work on version identity for deleted entries. Bundling them here would widen the blast radius of a truncation fix without improving it. Tracked as a follow-up.

- **`BundleLink.GetRelationRaw()` is an unclassified post-guard throw source.** It reads `MutableNode["relation"]?.GetValue<string>()` and can throw if `relation` is not a string. `ResolveHistorySelfUrl` short-circuits at the first self link, so a malformed `relation` on a *later* link would not throw during resolution but would throw in the post-guard link filter — the dispose-flush truncation path. Reaching it requires bypassing `SetRelationRaw(string)` via the low-level `SetProperty` escape hatch, which no production caller does, so this is theoretical. Noted because §6's inventory of post-guard throw sources did not classify it, and the claim that the post-guard region "genuinely contains no throw source" is therefore very slightly weaker than stated.

Also unchanged: the flush cadence of any method, and the `_pretty` whitespace deviation (§1).

## Testing

New tests alongside `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerPaginationTests.cs`, written failing-first:

1. **Tier 1, enumerator throws** — exception propagates, output stream left empty. One per rethrowing method.
2. **Tier 1, throw on first `MoveNextAsync`** — the zero-entries case matching the live `identifier:of-type` reproduction.
3. **Tier 1, mid-entry throw** — corrupt resource bytes through the validating `WriteRawValue`; empty stream, propagated exception.
4. **Empty-string tolerance in issue rendering** (§9) — a warning issue with an empty `Location` string serializes successfully, on **both** a pre-R5 tenant (via `WriteBundleIssuesPreR5`, inside the guard) and an R5 tenant (via `WriteBundleIssues`, after it), with the empty value skipped and the rest of the issue intact. Fails against today's code in both paths — as a guarded crash pre-R5, and as a post-guard truncated response on R5.
4b. **Tier 1, prologue throw** (§6) — an exception raised during the prologue, before any entry, yields an empty stream and a propagated exception, confirming the guard covers the prologue and that such a throw is necessarily tier 1.
5. **Tier 2, enumerator throws after a committed flush** — valid JSON, fatal OperationOutcome entry, original exception propagates.
6. **Tier 2, mid-entry throw** — the case §1 exists to fix: valid, parseable JSON with a complete error entry, no truncation, exception propagates. Fails against a non-buffered implementation.
7. **Searchset entry shape** — OperationOutcome in `resource`, `search.mode = "outcome"`, `fullUrl` present, no `request`/`response`. Asserted across all four `FhirVersion` values.
8. **History entry shape, R4/R4B/R5** — `request` (`GET`, url) and `response` (`500`) present, OperationOutcome under `response.outcome`, no `resource`, no `search`.
9. **History entry shape, Stu3** — OperationOutcome in `resource`, `request` present, **no `response`**.
10. **Stu3 happy-path conformance (§4)** — a normal Stu3 history bundle has no `response` on any entry; the same bundle in R4 does. Fails against today's code.
11. **Self-link resolution** — from `links`, plus all three fallbacks to `"_history"`: links null, no self relation, self entry with null/empty `Url`.
12. **`fullUrl` uniqueness** (`bdl-7`) — warnings plus an error produce distinct `fullUrl`s, both well-formed UUID URNs (`…d0` for the warning entry per §9, `…e0` for the error entry per §3).
12b. **Empty-URL link tolerance** (§9) — a history bundle whose `links` contain an entry with a null or empty `Url` serializes successfully, omitting that link rather than throwing. Fails against today's code, where it throws after the guard and truncates.
13. **Cancellation** — tier 1 rethrows with an empty stream; tier 2 yields valid JSON with no error entry and still rethrows.
14. **Link suppression** — tier 2 error path emits `self` but no `next`/`related`.
15. **Link-building failure** (§6) — a non-absolute `baseUrl` with includes pending, run twice: at the default threshold (tier 1: empty stream, propagated exception) and at a forced small `flushThresholdBytes` (tier 2: valid JSON with an error entry).
16. **`SerializeStreamAsync` mid-entry throw** (§8) — valid JSON with its existing `WriteErrorEntry` shape, **no rethrow**, pinning the preserved caller contract. Shape satisfies Stu3/R4/R4B `bdl-4` and R5 `bdl-3d`.
17. **Happy-path regression** — for a normal enumeration with `pretty=false`, output matches a golden snapshot captured from the current implementation before any change and checked in. Captured for searchset and R4 history; the Stu3 history snapshot is captured post-§4 by construction. Searchset snapshots must be captured without warning entries, or after §8's `fullUrl` correction, to avoid pinning the value being fixed.

Existing suites must stay green. The E2E count is expected to change shape: the 17 affected tests should stop failing with `JsonException` and surface clean 400s, then pass or fail on the merits of the untouched compiler gaps. Re-baselining is verification, not a gate.

## Risks

- **Observable behavior change.** Clients previously receiving a truncated 200 now get a proper 400/500 (tier 1) or a well-formed 200 with a fatal OperationOutcome (tier 2).
- **Stu3 history bundles lose their `response` element** (§4), including `lastModified` for deleted versions. A conformance fix, but visible to any Stu3 client reading `response.*` rather than `resource.meta`.
- **`_pretty=true` whitespace changes** within entries (§1).
- **One extra copy per entry** (§1), and peak buffer retention equal to the largest entry.
- **Signature change**: `ISchema? schemaProvider` on `SerializeHistoryAsync` only (optional-with-default), plus roughly six touch points in `HistoryEndpoints.cs`, five or six in `CompartmentEndpoints.cs`, one handler in `OperationEndpoints.cs`.
- **Exception text reaches the client** in `diagnostics` on the tier-2 path, matching existing behavior.
- **Warning-entry `fullUrl` changes** from `urn:uuid:operation-outcome` to a well-formed UUID URN (§9), altering existing output for any searchset carrying warning issues.
- **Empty `Location`/`Expression`/link `Url` values are now skipped** rather than rejected (§9). Today they crash; the change is strictly an improvement, but bundles that previously failed will now render with those values absent.
