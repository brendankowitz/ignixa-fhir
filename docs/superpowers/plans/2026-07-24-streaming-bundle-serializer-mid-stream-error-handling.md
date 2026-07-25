# StreamingBundleSerializer mid-stream error handling — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `StreamingBundleSerializer` from emitting truncated, unparseable HTTP 200 responses when serialization fails mid-stream; fail with a proper status code where nothing has been sent, and with a FHIR-conformant fatal `OperationOutcome` where it has.

**Architecture:** Entries are staged in a scratch `FhirJsonWriter` over a reusable `ArrayBufferWriter<byte>` and copied into the response writer only once complete, so the response writer is never mid-entry. A guarded region spanning prologue-through-loop catches failures and branches on `UnderlyingWriter.BytesCommitted`: zero means nothing reached the client, so `Reset()` discards the buffer and the exception rethrows for `FhirExceptionMiddleware` to turn into a real status code; non-zero means the status is committed, so an error entry is appended, the bundle is closed validly, and the exception then rethrows to be logged.

**Tech Stack:** C# / .NET 10 (multi-targeted net9.0/net10.0), `System.Text.Json` (`Utf8JsonWriter`, `ArrayBufferWriter<byte>`), xUnit + Shouldly, ASP.NET Core Minimal API.

**Design doc:** `docs/superpowers/specs/2026-07-24-streaming-bundle-serializer-mid-stream-error-handling-design.md` — signed off after 6 adversarial review rounds. It is the authority for every FHIR shape and constraint decision; this plan does not restate its reasoning. Section references below (§1-§10) point into it.

## Global Constraints

- **Build must be 0 warnings, 0 errors** (`dotnet build All.sln`). Warnings are errors in this repo.
- **Environment:** unset `Platform`, `__DOTNET_PREFERRED_BITNESS`, `__DOTNET_ADD_32BIT` before any `dotnet` command (known net10.0 CS8034 workaround in this repo).
- **Targeting:** `Ignixa.Application` and `Ignixa.Application.Tests` are **net10.0 only**. Only `src/Core/**` multi-targets net9.0/net10.0 (ADR 2607, enforced by `RuntimeMultiTargetingGuardTests`). Do not expect or chase two-TFM results for the Application projects; Tasks 1-7 all live there.
- **Test baseline — do not chase these.** A bare `dotnet test All.sln` exits nonzero in this environment for reasons unrelated to this work, all confirmed environmental: `Ignixa.SqlOnFhir.Tests` (uninitialized submodule content), `RepoGuards.Tests` (missing conformance suites directory), `Ignixa.DataLayer.SqlServer.IntegrationTests` and `Ignixa.SchemaUpgrade.Cli.Tests` (require `TEST_SQL_CONNECTION_STRING`), `Ignixa.Api.E2ETests` (requires a live environment), and an occasional `Validation.Tests` file-lock race between parallel TFM runs. **The signal that matters for Tasks 1-7 is `Ignixa.Application.Tests` green**, plus `Ignixa.Api.Tests` for Tasks 8-9. Verify any *new* failure is genuinely yours by reading its actual error text before assuming.
- **Async parameters are named `cancellationToken`**, never `ct` (CLAUDE.md; CA1725 is enforced).
- **No inline comments** unless explaining a non-obvious invariant (CLAUDE.md).
- **One type per file.** New public types get their own file.
- Test naming: `GivenContext_WhenAction_ThenResult`. AAA with Shouldly. No `#region`.
- **Do not modify** `FhirExceptionMiddleware`, the compiler (`Ignixa.Search.Sql`), or anything in §10's out-of-scope list.
- **`SerializeStreamAsync` must never rethrow** (§8). Its caller depends on that.
- File under change throughout: `src/Application/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleSerializer.cs` (referred to below as *the serializer*).
- **Test visibility:** `src/Application/Ignixa.Application/AssemblyInfo.cs:8` grants `[InternalsVisibleTo("Ignixa.Application.Tests")]`. Tasks 1 and 3 rely on it — anything a test calls directly must be `internal` or `public`, never `private`.
- **Existing `ct` parameters stay as they are.** The `cancellationToken` naming rule binds new parameters only. The three `HistoryEndpoints` handlers Task 8 touches declare `CancellationToken ct` (`:133`, `:182`, `:232`); renaming them is out of scope churn.
- **`BundleEntryResponse` is ambiguous** — three types share the name. Task 7 means `Ignixa.Application.Features.Bundle.BundleEntryResponse` (the record with `required int StatusCode`), not either `Ignixa.Serialization` type.

### Test helper conventions

The test snippets below reference helpers that **do not exist yet** and must be created. Three different tasks write three different test files, so these conventions are binding to keep them consistent:

- **Tasks 4, 6, and 7 share one file** (`StreamingBundleSerializerFailureTests.cs`). Task 4 creates the helpers; Tasks 6 and 7 reuse them and must not redefine them.
- `CreateEntry(string id)` → a `SearchEntryResult` with valid minimal resource bytes. `SearchMode` defaults to `Match` (enum 0), so it need not be set.
- `NewOptions()` → `new SearchOptions()`. `MaxItemCount` already defaults to 10; do not set it to 0.
- `Stu3Schema()` / `R4Schema()` / `R5Schema()` → minimal `ISchema` fakes (the interface has three members) whose `Version` maps to the corresponding `FhirVersion`. One fake type parameterized by version, not three.
- `EntriesWithCorruptResourceJsonAsync()` → entries whose resource bytes are deliberately invalid JSON (e.g. `"{unclosed"u8.ToArray()`), so `WriteRawProperty`'s validating `WriteRawValue` throws mid-entry. This is the throw source design §1 exists to contain — do not substitute a different failure mode.
- **Snapshot inputs must be fully deterministic**: fixed `LastModified` timestamps and fixed ids. History output renders `lastModified` from `SearchEntryResult.LastModified`, so `DateTimeOffset.UtcNow` would make goldens unstable.

---

### Task 1: `FhirJsonWriter` buffer-writer construction path

**Files:**
- Modify: `src/Application/Ignixa.Application/Features/Bundle/Serialization/FhirJsonWriter.cs`
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/FhirJsonWriterBufferTests.cs` (create)

**Interfaces:**
- Produces: `FhirJsonWriter.Create(IBufferWriter<byte> bufferWriter, bool pretty = false)` — mirrors the existing `Create(Stream, bool)` factory, returning a writer whose `UnderlyingWriter` targets the buffer. Every later task's scratch writer uses this.

`FhirJsonWriter` currently constructs only over a `Stream` (`:30-49`). `Utf8JsonWriter` has an `IBufferWriter<byte>` constructor, so this is an additive overload, not a redesign.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void GivenABufferWriter_WhenWritingAndFlushing_ThenBytesLandInTheBuffer()
{
    // Arrange
    var buffer = new ArrayBufferWriter<byte>();

    // Act
    using (var writer = FhirJsonWriter.Create(buffer))
    {
        writer.WriteStartObject();
        writer.WriteString("resourceType", "Patient");
        writer.WriteEndObject();
        writer.UnderlyingWriter.Flush();
    }

    // Assert
    Encoding.UTF8.GetString(buffer.WrittenSpan).ShouldBe("""{"resourceType":"Patient"}""");
}
```

- [ ] **Step 2: Run it and confirm it fails to compile** (no such overload).

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~FhirJsonWriterBufferTests"`

- [ ] **Step 3: Add the overload.** Mirror the existing `Create(Stream, bool)` exactly — same `JsonWriterOptions` (the encoder setting and `Indented = pretty`), same disposal semantics, except the writer targets the buffer. Add a second private constructor rather than branching inside the existing one.

- [ ] **Step 4: Add a `Reset`-and-reuse test**, since every later task reuses one scratch writer across entries:

```csharp
[Fact]
public void GivenAReusedWriterAndBuffer_WhenResetBetweenEntries_ThenNoStateBleedsForward()
{
    // Arrange
    var buffer = new ArrayBufferWriter<byte>();
    using var writer = FhirJsonWriter.Create(buffer);
    writer.WriteStartObject();
    writer.WriteString("first", "1");
    writer.WriteEndObject();
    writer.UnderlyingWriter.Flush();
    buffer.Clear();
    writer.UnderlyingWriter.Reset(buffer);

    // Act
    writer.WriteStartObject();
    writer.WriteString("second", "2");
    writer.WriteEndObject();
    writer.UnderlyingWriter.Flush();

    // Assert
    Encoding.UTF8.GetString(buffer.WrittenSpan).ShouldBe("""{"second":"2"}""");
}
```

- [ ] **Step 5: Run both tests — expect pass.**
- [ ] **Step 6: Build `All.sln` — expect 0/0. Commit.**

```bash
git add src/Application/Ignixa.Application/Features/Bundle/Serialization/FhirJsonWriter.cs test/Ignixa.Application.Tests/Features/Bundle/Serialization/FhirJsonWriterBufferTests.cs
git commit -m "feat(serialization): add IBufferWriter construction path to FhirJsonWriter

Needed so bundle entries can be staged in a scratch buffer before being
committed to the response stream."
```

---

### Task 2: Adjacent helper corrections (§9)

**Files:**
- Modify: the serializer — `WriteBundleIssues`, `WriteBundleIssuesPreR5`, `WriteBundleLinks`
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerHelperTests.cs` (create)
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerSnapshotTests.cs` (create)
- Test fixtures: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/Snapshots/*.json` (create)

These three fixes remove the throw sources §6 depends on being absent, and correct a malformed constant. They are independent of the buffering work and land first so later tasks inherit a throw-free epilogue.

**This task also captures the happy-path golden snapshots**, and must do so **in Step 1, before changing anything.** Design test 17 exists to prove the buffering rewrite leaves happy-path output byte-identical; a golden captured *after* the rewrite proves nothing. This matters most for R4 history, which no existing test covers at all — searchset is additionally protected by `StreamingBundleSerializerPaginationTests` staying green in Task 4.

**Interfaces:** no signature changes. Behavior changes only.

- [ ] **Step 1: Capture happy-path goldens from the unmodified implementation.**

Write `StreamingBundleSerializerSnapshotTests.cs` asserting byte-identical output against checked-in fixtures, for a fixed deterministic multi-entry input (see Test helper conventions — fixed timestamps and ids), with **`pretty: false`**. Design §1 documents that `_pretty=true` whitespace inside entries legitimately changes under buffering, so a pretty golden would pin the wrong thing.

Capture two: **searchset** and **R4 history**. The searchset input must carry **no warning issues**, so Step 4's `fullUrl` correction does not invalidate it. The Stu3 history golden is deliberately *not* captured here — that output is intentionally changing in Task 5, and its golden is captured there.

Run the tests and confirm they pass against today's code before proceeding. These fixtures are the regression baseline for Tasks 4-7.

- [ ] **Step 2: Write failing tests** for all three corrections.

```csharp
[Fact]
public async Task GivenAnIssueWithAnEmptyLocation_WhenSerializingPreR5_ThenTheEmptyValueIsSkipped()
{
    // Arrange
    var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d", Location: ["", "Patient.name"]) };

    // Act
    var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

    // Assert
    var locations = JsonDocument.Parse(json).RootElement
        .GetProperty("entry")[0].GetProperty("resource")
        .GetProperty("issue")[0].GetProperty("location");
    locations.GetArrayLength().ShouldBe(1);
    locations[0].GetString().ShouldBe("Patient.name");
}
```

Write the R5 mirror (`WriteBundleIssues`, asserting via the `issues` property rather than an entry), and:

```csharp
[Fact]
public async Task GivenAHistoryLinkWithAnEmptyUrl_WhenSerializing_ThenTheLinkIsOmitted()
{
    // Arrange -- deliberately NOT a "next" link: SerializeHistoryAsync strips those
    // whenever !hasMore || entryCount == 0 (:350-355), which would filter the link
    // before WriteBundleLinks runs and make this test vacuous.
    var links = new[] { CreateLink("self", "http://x/_history"), CreateLink("prev", null) };

    // Act
    var json = await SerializeHistoryWithLinksAsync(links);

    // Assert
    var relations = JsonDocument.Parse(json).RootElement.GetProperty("link")
        .EnumerateArray().Select(l => l.GetProperty("relation").GetString()).ToList();
    relations.ShouldBe(["self"]);
}

[Fact]
public async Task GivenASearchsetWithWarningIssues_WhenSerializing_ThenTheOutcomeEntryFullUrlIsAWellFormedUuidUrn()
{
    // Arrange
    var issues = new[] { new IssueComponent("warning", "incomplete", Diagnostics: "d") };

    // Act
    var json = await SerializeSearchsetWithIssuesAsync(issues, FhirVersion.R4);

    // Assert
    var fullUrl = JsonDocument.Parse(json).RootElement.GetProperty("entry")[0].GetProperty("fullUrl").GetString();
    fullUrl.ShouldBe("urn:uuid:00000000-0000-0000-0000-0000000000d0");
    Guid.TryParse(fullUrl!["urn:uuid:".Length..], out _).ShouldBeTrue();
}
```

- [ ] **Step 3: Run them — expect failures.** The first three throw (`EnsureArg` rejects empty strings); the fourth asserts against today's `urn:uuid:operation-outcome`.

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~StreamingBundleSerializerHelperTests"`

- [ ] **Step 4: Implement.** In `WriteBundleIssues` and `WriteBundleIssuesPreR5`, skip `Location`/`Expression` values that are null, empty, or whitespace — and skip emitting the array entirely if nothing survives the filter. In `WriteBundleLinks`, skip links whose `Url` is null, empty, or whitespace. In `WriteBundleIssuesPreR5`, change the hardcoded `fullUrl` (`:728`) to `urn:uuid:00000000-0000-0000-0000-0000000000d0`.

Leave `Severity` and `Code` unguarded — they are non-nullable positional components (`SearchOptions.cs:178-184`), and Task 4's prologue-throw test depends on an empty `Severity` still throwing.

- [ ] **Step 5: Run the tests — expect pass. Re-run the Step 1 snapshot tests — expect still passing** (the searchset golden is warning-free by construction, so the `fullUrl` change must not affect it; if it does, the golden was captured wrong).
- [ ] **Step 6: Run the full `Ignixa.Application.Tests` project** to catch any existing assertion pinning the old `fullUrl`.
- [ ] **Step 7: Build `All.sln` — expect 0/0. Commit.**

---

### Task 3: `WriteOperationOutcomeEntry` helper

**Files:**
- Modify: the serializer (add the helper — `internal`, see Interfaces below)
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/OperationOutcomeEntryShapeTests.cs` (create)

**Interfaces:**
- Produces: `internal static void WriteOperationOutcomeEntry(FhirJsonWriter writer, IssueComponent issue, string bundleType, FhirVersion fhirVersion, string fullUrl, string selfUrl)` — writes exactly one balanced bundle entry. Called by Tasks 4-7 from inside an open `entry` array.

**`internal`, not `private`** — this task's tests call it directly, and `[InternalsVisibleTo("Ignixa.Application.Tests")]` (`AssemblyInfo.cs:8`) is what makes that reachable. Nothing public routes to this helper until Task 4, so a `private` helper would be untestable in this task.

The `fullUrl` argument is always design §3's error-entry constant `urn:uuid:00000000-0000-0000-0000-0000000000e0`; callers in Tasks 4-7 pass it verbatim.

Shapes are specified in §3 and are **not** to be re-derived. Reproduced here as the authority for this task:

| `bundleType` | Shape |
|---|---|
| `searchset` | `fullUrl`, OperationOutcome as `resource`, `search.mode = "outcome"`. No `request`/`response`. |
| `history`, R4/R4B/R5 | `fullUrl`, `request` {`method: "GET"`, `url: selfUrl`}, `response` {`status: "500"`, `outcome`: OperationOutcome}. No `resource`, no `search`. |
| `history`, Stu3 | `fullUrl`, OperationOutcome as `resource`, `request` {`method: "GET"`, `url: selfUrl`}. **No `response`.** |
| `batch-response` / `transaction-response` | Existing `WriteErrorEntry` shape: `response.status = "500 Internal Server Error"`, OperationOutcome as `resource`. |
| anything else | `fullUrl`, OperationOutcome as `resource` only. |

The OperationOutcome body is `{resourceType: "OperationOutcome", issue: [{severity, code, diagnostics}]}` in every case.

- [ ] **Step 1: Write failing tests** — one per row above, asserting element presence *and absence* (absence is the load-bearing half: a `resource` on the R5 history shape violates `bdl-3b`). Assert the searchset shape identically for all four `FhirVersion` values.
- [ ] **Step 2: Run — expect failure to compile** (no such method).
- [ ] **Step 3: Implement the helper.** Branch on `bundleType`, then on `fhirVersion >= FhirVersion.R4` for the history case only. Delegate the batch-response case to the existing `WriteErrorEntry` rather than duplicating it.
- [ ] **Step 4: Run — expect pass. Build `All.sln` — 0/0. Commit.**

---

### Task 4: Buffering + two-tier recovery in `SerializeWithPaginationAsync`

**Files:**
- Modify: the serializer — `SerializeWithPaginationAsync`
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerFailureTests.cs` (create)

The core task. Implements §1, §2, and §6 for the highest-traffic method.

**Interfaces:**
- Consumes: `FhirJsonWriter.Create(IBufferWriter<byte>, bool)` (Task 1), `WriteOperationOutcomeEntry` (Task 3).
- Produces: the buffering + guard structure Tasks 5-7 replicate.

Structure to implement:

1. Allocate one `ArrayBufferWriter<byte>` and one scratch `FhirJsonWriter` before the guard.
2. Open the `try` **immediately after** `FhirJsonWriter.Create` for the main writer — covering the prologue (§6).
3. In the loop, write each entry into the scratch writer; on completion `Flush()` the scratch writer, copy via `writer.UnderlyingWriter.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true)`, then `buffer.Clear()` and `scratch.UnderlyingWriter.Reset(buffer)`. The existing `BytesPending >= flushThresholdBytes` check stays on the **main** writer and is unchanged.
4. **Hoist** the continuation-token / `nextLink` / `relatedLink` computation (currently `:223-259`) to just before `WriteEndArray()`, still inside the guard. Only the writes that consume those locals stay after it.
5. Close the `try` just before `WriteEndArray()`.
6. `catch (OperationCanceledException)` and `catch (Exception)`: branch on `writer.UnderlyingWriter.BytesCommitted == 0`.
   - **Tier 1:** call the **parameterless** `writer.UnderlyingWriter.Reset()` — the main writer is stream-backed; passing the scratch buffer would silently redirect it. Then rethrow.
   - **Tier 2:** for non-cancellation, call `WriteOperationOutcomeEntry` (the array is still open); then close the array and bundle, flush with `CancellationToken.None`, and rethrow. For cancellation, skip the error entry but still close, flush, and rethrow.
   - Tier 2 must **skip** `nextLink`/`relatedLink` (§5 of the design's per-method notes) — emit `self` only.

- [ ] **Step 1: Write the failing tests.** Use a helper that yields N entries then throws:

```csharp
private static async IAsyncEnumerable<SearchEntryResult> ThrowAfterAsync(int count, Exception ex)
{
    for (var i = 0; i < count; i++)
    {
        yield return CreateEntry($"p{i}");
        await Task.Yield();
    }
    throw ex;
}
```

Cover, at minimum:

```csharp
[Fact]
public async Task GivenAnEnumeratorThatThrowsBeforeAnyFlush_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
{
    // Arrange
    var stream = new MemoryStream();
    var boom = new InvalidOperationException("boom");

    // Act
    var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
        stream, "searchset", null, ThrowAfterAsync(2, boom), NewOptions(), "http://x", "");

    // Assert
    (await act.ShouldThrowAsync<InvalidOperationException>()).ShouldBeSameAs(boom);
    stream.ToArray().ShouldBeEmpty();
}

[Fact]
public async Task GivenAnEnumeratorThatThrowsAfterACommittedFlush_WhenSerializing_ThenTheBundleIsValidAndCarriesAFatalOutcome()
{
    // Arrange
    var stream = new MemoryStream();

    // Act
    var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
        stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
        NewOptions(), "http://x", "", flushThresholdBytes: 1);

    // Assert
    await act.ShouldThrowAsync<InvalidOperationException>();
    var root = JsonDocument.Parse(stream.ToArray()).RootElement;
    root.GetProperty("entry").EnumerateArray()
        .Any(e => e.TryGetProperty("search", out var s) && s.GetProperty("mode").GetString() == "outcome")
        .ShouldBeTrue();
}
```

Plus: throw on first `MoveNextAsync`; **mid-entry** throw (corrupt `ResourceBytes` driven through `WriteRawProperty`'s validating write) at both tiers — the tier-2 variant is the test that fails against a non-buffered implementation and is the point of §1; cancellation at both tiers; `next`-link suppression; and a non-absolute `baseUrl` with includes pending, run at both the default and a forced-1-byte threshold.

Two further tests from the design's list that are easy to lose:

```csharp
[Fact]
public async Task GivenAThrowDuringThePrologue_WhenSerializing_ThenNothingIsWrittenAndTheExceptionPropagates()
{
    // Arrange -- empty Severity throws inside WriteBundleIssuesPreR5, which Task 2
    // deliberately left unguarded; this fires before any entry, so it must be tier 1.
    var options = NewOptions();
    options.BundleIssues = [new IssueComponent("", "incomplete", Diagnostics: "d")];
    var stream = new MemoryStream();

    // Act
    var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
        stream, "searchset", null, TwoEntriesAsync(), options, "http://x", "", schemaProvider: R4Schema());

    // Assert
    await act.ShouldThrowAsync<Exception>();
    stream.ToArray().ShouldBeEmpty();
}

[Fact]
public async Task GivenWarningIssuesAndATierTwoFailure_WhenSerializing_ThenBothOutcomeEntriesHaveDistinctFullUrls()
{
    // Arrange
    var options = NewOptions();
    options.BundleIssues = [new IssueComponent("warning", "incomplete", Diagnostics: "d")];
    var stream = new MemoryStream();

    // Act
    var act = () => StreamingBundleSerializer.SerializeWithPaginationAsync(
        stream, "searchset", null, ThrowAfterAsync(2, new InvalidOperationException("boom")),
        options, "http://x", "", schemaProvider: R4Schema(), flushThresholdBytes: 1);

    // Assert
    await act.ShouldThrowAsync<InvalidOperationException>();
    var fullUrls = JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("entry")
        .EnumerateArray()
        .Where(e => e.TryGetProperty("fullUrl", out var f) && f.GetString()!.StartsWith("urn:uuid:", StringComparison.Ordinal))
        .Select(e => e.GetProperty("fullUrl").GetString())
        .ToList();
    fullUrls.ShouldBe(["urn:uuid:00000000-0000-0000-0000-0000000000d0", "urn:uuid:00000000-0000-0000-0000-0000000000e0"]);
}
```

- [ ] **Step 2: Run — expect the truncation-shaped failures** (`JsonException` on parse, non-empty streams where empty is expected).
- [ ] **Step 3: Implement** per the structure above.
- [ ] **Step 4: Run — expect pass.**
- [ ] **Step 5: Run the full `Ignixa.Application.Tests` project** — the existing `StreamingBundleSerializerPaginationTests` must stay green, proving the happy path is unchanged.
- [ ] **Step 6: Build `All.sln` — 0/0. Commit.**

---

### Task 5: `SerializeHistoryAsync` — buffering, recovery, Stu3 conformance, version parameter

**Files:**
- Modify: the serializer — `SerializeHistoryAsync`
- Test: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerHistoryTests.cs` (create)

**Interfaces:**
- Produces: `SerializeHistoryAsync(..., IReadOnlyList<FhirBundleLink>? links = null, ISchema? schemaProvider = null, bool pretty = false, int pageSize = 20, CancellationToken cancellationToken = default)` — insert `schemaProvider` directly after `links`. Optional-with-default, and all three existing call sites use fully named arguments, so they keep compiling untouched until Task 8 threads real values.

Three changes in one task, because they share the method's structure and one test cycle:
1. Per-entry buffering and the two-tier guard, mirroring Task 4. Note this method **flushes per entry** (`:345`), so tier 2 is the common case from the second entry onward.
2. Add `ISchema? schemaProvider = null` and derive `fhirVersion` exactly as `SerializeWithPaginationAsync` does (`:140`).
3. **§4 conformance fix:** write the happy-path `response` element (`:336-340`) only when `fhirVersion >= FhirVersion.R4`; suppress it for Stu3. `request` continues for all versions.

- [ ] **Step 1: Write failing tests.**

```csharp
[Fact]
public async Task GivenAStu3HistoryBundle_WhenSerializing_ThenNoEntryCarriesAResponseElement()
{
    // Arrange
    var stream = new MemoryStream();

    // Act
    await StreamingBundleSerializer.SerializeHistoryAsync(
        stream, "history", null, TwoEntriesAsync(), links: null, schemaProvider: Stu3Schema());

    // Assert
    JsonDocument.Parse(stream.ToArray()).RootElement.GetProperty("entry")
        .EnumerateArray().ShouldAllBe(e => !e.TryGetProperty("response", out _));
}

[Fact]
public async Task GivenAnR4HistoryBundle_WhenSerializing_ThenEveryEntryCarriesAResponseElement()
{
    // ... same shape, R4 schema, asserting response IS present
}
```

Plus the error-path tests: tier-2 history error entry shape for R4/R4B/R5 (`request` + `response.outcome`, no `resource`, no `search`) and for Stu3 (`resource` + `request`, no `response`); self-link resolution from `links` and all three fallbacks to `"_history"` (links null, no self relation, self entry with null/empty `Url`); tier-1 empty-stream behavior.

- [ ] **Step 2: Run — expect failures** (the Stu3 test fails against today's unconditional `response`; the shape tests fail to compile until the parameter exists).
- [ ] **Step 3: Implement all three changes.**
- [ ] **Step 4: Run — expect pass. Build `All.sln` — 0/0. Commit.**

---

### Task 6: `SerializeAsync` — buffering and recovery

**Files:**
- Modify: the serializer — `SerializeAsync`
- Test: add to `StreamingBundleSerializerFailureTests.cs`

Dead code (no production or test callers), fixed rather than deleted per explicit decision — it is `public` on a `public static` class. Same buffering and two-tier structure as Task 4. It flushes per entry (`:86`), so tier 2 is reachable from the second entry.

No `schemaProvider` parameter (§5); it passes `FhirVersion.R4` to `WriteOperationOutcomeEntry`, which is inert because it never renders the history shape. It passes design §3's error-entry constant `urn:uuid:00000000-0000-0000-0000-0000000000e0` as `fullUrl`, and `string.Empty` as `selfUrl` (non-optional, but read only on the history branch this method never takes).

Its footer goes through `WriteBundleFooterAsync` (`:90`), which closes array and object together — that still works, because the error entry is written *inside* the array before the footer runs.

- [ ] **Step 1: Write failing tests** — tier 1 (empty stream, propagated exception) and tier 2 (valid JSON with an outcome entry) for both an enumerator throw and a mid-entry throw.
- [ ] **Step 2: Run — expect truncation-shaped failures.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run — expect pass. Build `All.sln` — 0/0. Commit.**

---

### Task 7: `SerializeStreamAsync` — buffering only, contract preserved

**Files:**
- Modify: the serializer — `SerializeStreamAsync`
- Test: add to `StreamingBundleSerializerFailureTests.cs`

**This method must NOT rethrow.** Its only caller (`FhirEndpoints.cs:1149-1191`) wraps it in a catch returning `Results.StatusCode(500)` and calls `await streamingContext.CompleteAsync()` (`:1180`) afterward; rethrowing would produce a body-less 500, a secondary exception against a started response, and skip `CompleteAsync`, leaking background tasks (§8).

Apply **only** per-entry buffering, so a mid-entry failure in `WriteEntryResponse` → `WriteRawProperty` (`:504`) no longer corrupts the main writer. Its existing try/catch (`:408-428`), `WriteErrorEntry` call, and always-run `WriteBundleFooterAsync` (`:438`) stay exactly as they are.

- [ ] **Step 1: Write the failing test.**

```csharp
[Fact]
public async Task GivenAMidEntryFailure_WhenStreamingABatchResponse_ThenTheBundleIsValidAndNoExceptionEscapes()
{
    // Arrange
    var stream = new MemoryStream();

    // Act — must NOT throw
    await StreamingBundleSerializer.SerializeStreamAsync(
        stream, "batch-response", EntriesWithCorruptResourceJsonAsync());

    // Assert
    var root = JsonDocument.Parse(stream.ToArray()).RootElement;
    root.GetProperty("entry").EnumerateArray()
        .Any(e => e.GetProperty("response").GetProperty("status").GetString() == "500 Internal Server Error")
        .ShouldBeTrue();
}
```

- [ ] **Step 2: Run — expect a `JsonException` on parse** (today the corrupt entry breaks the writer and the footer write throws).
- [ ] **Step 3: Implement buffering only.** Do not touch the catch, the error entry, or the footer.
- [ ] **Step 4: Run — expect pass. Confirm by inspection that `SerializeStreamAsync` contains no `throw` on the failure path. Build `All.sln` — 0/0. Commit.**

---

### Task 8: Thread `schemaProvider` into `HistoryEndpoints`

**Files:**
- Modify: `src/Application/Ignixa.Api/Endpoints/HistoryEndpoints.cs`

**This is not a mechanical thread-through.** None of the three private handlers has `versionContext`, `fhirSpec`, or `tenantConfig` in scope, and `HandleGetResourceHistory` (`:127-133`) lacks `IFhirRequestContextAccessor` entirely.

Roughly six touch points:
- Three private handlers (`:127-133`, `:177-182`, `:228-232`) gain `[FromServices] IFhirVersionContext versionContext` and, where missing, `[FromServices] IFhirRequestContextAccessor fhirContextAccessor`.
- Each derives `fhirSpec` via `FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion)` and obtains the provider via `versionContext.GetSchemaProvider(fhirSpec, tenantId)`, following the established pattern at `FhirEndpoints.cs:585-599` — including its null-handling for missing tenant config.
- The three tenant-agnostic lambdas (`:95-97`, `:104-106`, `:112-114`) already inject the accessor and must forward the new services.
- All three `SerializeHistoryAsync` calls (`:158`, `:209`, `:258`) pass `schemaProvider:`.

- [ ] **Step 1: Read `FhirEndpoints.cs:585-599` and copy its pattern exactly**, including null-handling.
- [ ] **Step 2: Update the three handlers, three lambdas, and three call sites.**
- [ ] **Step 3: Build `All.sln` — 0/0.** The build is the real completeness signal here: this codebase's target-typed `=> new(...)` convention means a literal grep for call sites misses some.
- [ ] **Step 4: Run `Ignixa.Api.Tests` and `Ignixa.Application.Tests` — expect green. Commit.**

---

### Task 9: Thread `schemaProvider` into `$everything` and compartment search

**Files:**
- Modify: `src/Application/Ignixa.Api/Endpoints/CompartmentEndpoints.cs`
- Modify: `src/Application/Ignixa.Api/Endpoints/OperationEndpoints.cs`

Fixes a **live pre-existing defect** independent of the error path: these two of the seven `SerializeWithPaginationAsync` call sites pass no `schemaProvider`, so `fhirVersion` falls back to R4 (`:140`) and R5 tenants get pre-R5-shaped *warning* issues today.

- `CompartmentEndpoints`: `ExecuteSearchCompartmentAsync` already has `fhirSpec`/`tenantConfig` (`:239-243`), but the file has no `IFhirVersionContext` anywhere. Inject it and forward from the four wrapper handlers (`:145`, `:184`, `:321`, `:358`). Pass at `:272-281`.
- `OperationEndpoints`: `HandlePatientEverything` (`:435-444`) lacks `versionContext`, `fhirContextAccessor`, **and** `tenantId` — it needs the full `FhirEndpoints.cs:585-599` pattern. Both routes bind the same method group, so one signature change covers both. Pass at `:507-516`.

- [ ] **Step 1: Update `CompartmentEndpoints` — inject, forward from four wrappers, pass at the call site.**
- [ ] **Step 2: Update `OperationEndpoints` — full pattern on `HandlePatientEverything`, pass at the call site.**
- [ ] **Step 3: Add a regression test in `test/Ignixa.Api.Tests`**, at endpoint level (not serializer level — Task 2 already covers the serializer's R5 rendering). It must prove the *endpoint* now supplies a schema provider: issue a compartment search against an R5 tenant through the existing API test harness and assert the response bundle renders warning issues via the R5 `issues` property rather than a pre-R5 `search.mode = "outcome"` entry. Follow whatever tenant/version fixture pattern the existing `Ignixa.Api.Tests` compartment or search tests already use; do not invent a new harness.
- [ ] **Step 4: Build `All.sln` — 0/0. Run `Ignixa.Api.Tests` — green. Commit.**

---

### Task 10: Snapshot verification, full regression, E2E re-baseline

**Files:**
- Modify: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/StreamingBundleSerializerSnapshotTests.cs` (created in Task 2)
- Test fixtures: `test/Ignixa.Application.Tests/Features/Bundle/Serialization/Snapshots/stu3-history.json` (create)

The searchset and R4 history goldens were captured in **Task 2, Step 1, before any code changed** — that ordering is what gives them regression value. This task verifies they still hold and adds the one golden that could not be captured early.

- [ ] **Step 1: Re-run the Task 2 snapshot tests — expect pass.** Searchset and R4 history output must be byte-identical to the pre-change goldens. A failure here means the buffering rewrite changed happy-path output, which §1 says it must not (with `pretty: false`).
- [ ] **Step 2: Add the Stu3 history golden.** Its output deliberately changed in Task 5 (no `response` element per §4), so it is captured now, post-change, by construction. Assert it contains no `response` on any entry.
- [ ] **Step 3: Run the full solution.**

```bash
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```

Expect green except the two known pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 4: Run the E2E suite against a fresh database and re-baseline.**

Set `TEST_SQL_CONNECTION_STRING` to a **new** database name (never reuse an existing one — stale schema silently produces ~590 bogus failures) plus `SqlServer__AutomaticSchemaDeploymentEnabled=true`.

```bash
dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

Expected: the 17 tests that previously failed with `JsonException` (`IdentifierOfTypeTests` ×13, system-level `:not` ×2, `ChainingSearchTests` reverse-chain, `SortTests` datetime-sort) now surface **clean 400s** and fail on the merits of the untouched compiler gaps — or pass. The count should not rise above 32. **This is a re-baseline, not a pass/fail gate** — the underlying compiler gaps are explicitly out of scope (§10).

- [ ] **Step 5: Record the new count and categorization. Commit.**
