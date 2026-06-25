# SQL-on-FHIR Conformance `test_report.json` — Design

**Date:** 2026-06-17
**Status:** Approved (design)
**Area:** `test/Ignixa.SqlOnFhir.Tests/`, `.github/actions/dotnet-build-and-test/`

## Goal

On every `dotnet test` run, emit a `test_report.json` in the format defined by the
SQL-on-FHIR reference implementation ([sql-on-fhir.js — "Adding your implementation"](https://github.com/FHIR/sql-on-fhir.js#adding-your-implementation)),
so Ignixa's ViewDefinition conformance can be reported to the
[implementations registry](https://sql-on-fhir.org/extra/impls.html). The file must be:

- Produced locally as a side effect of running the existing conformance tests.
- Uploaded as a build artifact on both push-to-main (`ci.yml`) and PR/branch builds (`pr-build.yml`).

## Report Format (fixed by the spec)

A map keyed by test-file name (with `.json` extension), per
`test/Ignixa.SqlOnFhir.Tests/sql-on-fhir-tests/test_report/README.md`:

```json
{
  "logic.json": {
    "tests": [
      { "name": "filtering with 'and'", "result": { "passed": true } },
      { "name": "filtering with 'or'", "result": { "passed": false, "reason": "skipped" } }
    ]
  }
}
```

- Top level: `{ "<file>.json": { "tests": [ ... ] } }`.
- Each entry: `{ "name": <test title>, "result": { "passed": <bool>, "reason"?: <string> } }`.
- `reason` is optional and present on non-passing entries.
- A JSON schema exists at `sql-on-fhir-tests/test_report/test-report.schema.json` (validation is out of scope here — see Out of Scope).

## Problem

The existing `OfficialSqlOnFhirTestRunner` runs every official test case as an xunit
`[Theory]` with `[MemberData]`, but it **asserts and throws** per case
(`Assert.Fail` / `Assert.Equal`). That structure cannot produce a per-test
pass/fail-with-reason map, because a thrown assertion ends the case before any
result can be recorded.

The fix is to **separate evaluation from assertion** and **collect outcomes in an
xunit collection fixture** that writes the report once, after the last case runs.

## Approach: collection fixture (single execution pass)

xunit is **2.9.3** (v2) — no assembly fixtures. The mechanism is a **collection
fixture**: `ICollectionFixture<T>` + `[CollectionDefinition]` + `[Collection]` on the
runner. The fixture is constructed once, injected into the test class constructor,
and its `Dispose` runs after the last case in the collection — that is where the
file is written.

The tests run **once**: each `[Theory]` case records its outcome into the fixture
*before* asserting, so failures are captured too, and the existing gate still fails
the build on any conformance regression.

### Components (one type per file, in `test/Ignixa.SqlOnFhir.Tests/`)

1. **`SqlOnFhirTestCaseOutcome.cs`** — `record SqlOnFhirTestCaseOutcome(bool Passed, string? Reason)`.

2. **`SqlOnFhirTestCaseEvaluator.cs`** — `static SqlOnFhirTestCaseOutcome Run(SqlOnFhirTestFile testFile, SqlOnFhirTestCase testCase)`.
   Holds the logic currently inside the `[Theory]` body (load resources, schema-column
   check, `EvaluateBatch` + row comparison, and the `ExpectError` path), but **returns
   a failure reason instead of calling `Assert.Fail`**. This is the single source of truth
   for "did this case pass, and if not, why."

3. **`SqlOnFhirReportCollector.cs`** — the collection fixture (`IDisposable`).
   - Thread-safe accumulation: a `lock` over an insertion-ordered
     `Dictionary<string, List<SqlOnFhirTestEntry>>` (cheap; v2 runs a single class's
     cases sequentially within its collection, so contention is effectively nil — the
     lock is for correctness, not throughput).
   - `Record(string fileName, string testName, SqlOnFhirTestCaseOutcome outcome)`.
   - Output path resolution: `SOF_TEST_REPORT_PATH` environment variable if set,
     else `<test-assembly-dir>/test_report.json` (same base dir the runner already uses
     to locate `sql-on-fhir-tests/tests`).
   - `Dispose()` serializes the accumulated map and writes the file.

4. **`SqlOnFhirReportCollection.cs`** — `[CollectionDefinition("SqlOnFhirReport")] public class SqlOnFhirReportCollection : ICollectionFixture<SqlOnFhirReportCollector> { }`.

5. **Serialization records** — exact JSON shape via `System.Text.Json` + `[JsonPropertyName]`:
   - `SqlOnFhirFileReport.cs` — `{ "tests": List<SqlOnFhirTestEntry> }`.
   - `SqlOnFhirTestEntry.cs` — `{ "name": string, "result": SqlOnFhirTestResult }`.
   - `SqlOnFhirTestResult.cs` — `{ "passed": bool, "reason": string? }` (`reason` omitted when null via `JsonIgnoreCondition.WhenWritingNull`).

6. **`OfficialSqlOnFhirTestRunner.cs`** (existing, refactored) —
   - Decorated `[Collection("SqlOnFhirReport")]`; constructor takes `SqlOnFhirReportCollector`.
   - The `[Theory]` body becomes:
     ```csharp
     var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, sqlTestCase);
     _collector.Record(fileNameWithExtension, sqlTestCase.Title, outcome);
     Assert.True(outcome.Passed, outcome.Reason);
     ```
   - Skipped / no-ViewDefinition cases are recorded as `passed: false, reason: "skipped"`
     (faithful to the sof-js convention) before any early return.
   - **Gate behavior unchanged**: a conformance mismatch still throws and fails the build.

> Note: `GetOfficialTestCases()` currently yields the file name via
> `Path.GetFileNameWithoutExtension`. The report key must include the `.json` extension
> (per the format example), so the runner appends `.json` (or the MemberData is adjusted
> to also carry the full file name). Confirm during implementation.

### Data flow

```
GetOfficialTestCases()  ──┐
                          ├─ per case ─→ SqlOnFhirTestCaseEvaluator.Run ─→ outcome
[Theory] case body  ──────┘                                                  │
   ├─ collector.Record(file, name, outcome)   (always, before assert)        │
   └─ Assert.True(outcome.Passed, outcome.Reason)   (gate)                    │
                                                                              ▼
SqlOnFhirReportCollector.Dispose()  ─→  Dictionary<string, FileReport>  ─→  JSON file
   (runs after last case in the collection)
```

## Output location

- **Default:** `<test-assembly-dir>/test_report.json` — i.e. under `bin/.../`, already
  gitignored. CI finds it with a `**/test_report.json` glob.
- **Override:** `SOF_TEST_REPORT_PATH` env var (local convenience; lets CI pin a path if desired).
- **Not committed** to the repo (decision: artifact-only, no repo churn / merge conflicts).

## CI wiring

Single new step in `.github/actions/dotnet-build-and-test/action.yml`, after the
existing TRX upload:

```yaml
- name: Upload SQL-on-FHIR conformance report
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: sql-on-fhir-test-report
    path: '**/test_report.json'
    if-no-files-found: 'ignore'
    retention-days: 30
```

Because both `ci.yml` and `pr-build.yml` run unit tests through this composite action,
the artifact is produced on push-to-main **and** PR/branch builds with one edit.

## Behavior characteristics (not tradeoffs)

- **Report reflects the tests that ran.** Under a `--filter` that subsets the suite,
  the report is partial — by design ("results of this run"). In CI nothing filters out
  the SqlOnFhir tests, so the uploaded artifact is always complete. The **canonical**
  conformance report for registry publication is the CI artifact, not a locally-filtered run.
- **Multi-TFM.** The project targets `net9.0;net10.0`, so a full `dotnet test` writes one
  report per TFM (separate bin dirs). The `**/test_report.json` glob uploads both with
  distinct paths; no collision. Acceptable as-is.

## Testing

- Existing `[Theory]` preserves correctness coverage (logic merely moved into the
  evaluator; behavior identical).
- **`SqlOnFhirTestCaseEvaluatorTests`** — one known-pass and one known-fail synthetic
  case asserting `outcome.Passed` and a non-null `Reason` on failure.
- **`SqlOnFhirReportSerializationTests`** — serialize a sample map and assert the JSON
  structure/keys (`tests`/`name`/`result`/`passed`, `reason` omitted when null).
- These do not depend on fixture-dispose timing.

## Out of scope

- Validating the generated report against `test-report.schema.json` (no JSON-schema
  validator dependency in the repo today). Possible follow-up.
- Publishing the report to a public CORS-enabled URL and registering Ignixa in
  `implementations.json` on sql-on-fhir.org. The artifact is the prerequisite;
  registration is a separate manual step.
- Refreshing the pinned test suite (v2.0.0 → v2.1.0-pre). Independent of this change.

## Documentation

Update `docs/features/sql-on-fhir/readme.md`: note the conformance harness now emits a
`test_report.json` (artifact + local path / `SOF_TEST_REPORT_PATH`), and mention the CI
artifact name `sql-on-fhir-test-report`.
