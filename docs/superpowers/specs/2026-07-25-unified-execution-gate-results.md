# Unified execution gate — results

**Status:** executed
**Date:** 2026-07-25
**Branch under test:** `worktree-search-sql-unified` (PR #365), tip `c207a051`
**Harness branch:** `worktree-ignixa-datalayer-sqlserver`, tip `cee3e2a5` (working tree only, reverted after measurement)
**Plan:** `docs/superpowers/plans/2026-07-25-search-sql-unified-foundation.md`, Task 9

This branch's own scope note says nothing in it executes — no test of any kind runs its emitted SQL against
a database, and it names that as "a class of defect this branch structurally cannot detect." This document
is the record of that gap being closed. Every number below came from a real SQL Server, not from asserting
on SQL text.

---

## 1. Headline

| Suite | Baseline (A's data layer on A's compiler) | Unified (A's data layer on **this** compiler) | Delta |
|---|---|---|---|
| `Ignixa.DataLayer.SqlServer.IntegrationTests` | 126 passed / 0 failed / 0 skipped | **126 passed / 0 failed / 0 skipped** | none |
| `Ignixa.Api.E2ETests` | 620 total / 568 passed / 32 failed / 20 skipped | **620 total / 569 passed / 31 failed / 20 skipped** | **+1 passed, −1 failed** |
| Adapter compile breaks | — | **0** | — |

Both baselines were measured locally in this session against fresh databases rather than taken on trust,
and both reproduced branch A's documented figures exactly.

The one E2E movement is a fix, not a regression. Failure-set diffing (below) shows **zero new failures**.

Three new integration test classes (6 tests) were written for this gate because the coverage it was
supposed to consult did not exist. All 6 pass against the unified compiler.

---

## 2. Environment

Recorded because four prior attempts at this measurement were lost to environment artifacts, not to code.

- **SQL Server**: Microsoft SQL Server 2025 (RTM-GDR) (KB5102333) 17.0.1125.2, Enterprise Developer
  Edition, local default instance `MSSQLSERVER`, Windows integrated auth. **Not Docker** — the SQL Server
  2022 image cannot deploy this project's DACPAC, which targets `Sql170`.
- **.NET SDK**: 10.0.302.
- `Platform`, `__DOTNET_PREFERRED_BITNESS`, `__DOTNET_ADD_32BIT` unset before every `dotnet` invocation
  (the known CS8034 workaround).
- `SqlServer__AutomaticSchemaDeploymentEnabled=true`.
- Four **brand-new** databases, one per measurement, dropped afterwards: `IgnixaGateBaseIntg`,
  `IgnixaGateE2EBase`, `IgnixaGateUniIntg`, `IgnixaGateE2EUni`. A stale database with schema drift
  silently produces ~590 bogus failures that look catastrophic and are pure artifact.

**One environment trap worth recording**: the base connection string's `Initial Catalog` must name a
database that already exists. `TestTenantDatabase` creates its own per-test databases, but
`SqlExecutionServiceExecutionTests` connects to the base catalog directly. Pointing the base at a
not-yet-created database yields exactly 3 failures ("Cannot open database ... requested by the login")
that look like code failures and are not. First baseline run hit this and read 123/126.

---

## 3. How A was pointed at this compiler

The plan says to retarget `Ignixa.DataLayer.SqlServer`'s `ProjectReference` across worktrees. **That does
not work**, and the reason is worth recording so nobody retries it:

`Ignixa.Search.Sql` references `..\Ignixa.Search\Ignixa.Search.csproj`. A cross-worktree project reference
therefore drags *this* branch's `Ignixa.Search` into A's build graph alongside A's own — two distinct
projects emitting the same assembly identity into one output directory. That is a duplicate-type collision,
not a measurement.

Instead the compiler source was **overlaid into A's working tree**, which is semantically the same
measurement without the assembly-identity problem:

```
git checkout worktree-search-sql-unified -- \
  src/Core/Ignixa.Search.Sql/ \
  src/Core/Ignixa.Search.Sql.Generators/ \
  src/Core/Ignixa.Search/Models/AccessConstraint.cs \
  src/Core/Ignixa.Search/Models/SearchOptions.cs \
  src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql
```

That is 26 modified + 6 added files under `Ignixa.Search.Sql`, one generator file, two `Ignixa.Search`
model files, and the catalog source. Nothing in `Ignixa.Search.Sql` is deleted between the branches, so a
`git checkout` overlay is exact.

`97.sql` has to come along because A deleted it when it switched its catalog source to its own Database
project's decomposed DDL, and this branch's `SqlCatalogGenerator` reads `97.sql` specifically
(`file.Path.EndsWith("97.sql")`, where A's reads every `*.sql` additional file).

**The plan's assumption that `97.sql` carries every table and column the ported features read is now
verified rather than asserted** — the entire 126-test integration suite and the entire 620-test E2E suite
ran against a `SqlCatalog` generated from `97.sql`, writing and reading through A's DACPAC-deployed schema,
with no missing-column failure of any kind.

A's working tree was reverted to `cee3e2a5` after measurement; nothing was committed to it.

---

## 4. Adapter deltas A must re-apply: none

This was expected to be the bulk of the task. It is empty, and that is a real result.

The plan predicted four breaks. Every one of them is real *inside* `Ignixa.Search.Sql` and invisible at
A's adapter boundary:

| Predicted break | Why it did not surface |
|---|---|
| `Lower.Run` took seven trailing positional optionals; this branch takes `LowerOptions? options` | A's adapter never calls `Lower.Run`. It calls `SearchCompiler.CompileFromOptionsAsync`, which absorbs the change in its body. |
| A's `(long Start, long End)` surrogate tuple is gone; there is a typed `SurrogateIdRange` | `CompileFromOptionsAsync` deliberately **keeps** `(long Start, long End)? surrogateIdRange` as its public parameter and converts to `SurrogateIdRange(SqlParameterRef, SqlParameterRef)` internally. The adapter boundary was preserved on purpose (Task 8 report, §"Starting state"). |
| `CompileFromOptionsAsync` predates `AccessConstraints` and `ResourceTypes` forwarding | Both were added *inside* the method body (`additionalResourceTypes: options.ResourceTypes` into `Resolve.RunAsync`, plus `LowerOptions.ResourceTypes` / `LowerOptions.AccessConstraints`). Signature unchanged. |
| `QueryPlan`'s positional tail order changed | A's adapter only reads `plan.Includes`; it never constructs a `QueryPlan`. |

`SqlServerCompiledSearchService.CompileAsync`'s 13-argument call site compiles byte-identically against
both compilers.

### The one signature change a future caller can trip on

`SearchCompiler.CompileAsync` and `SearchCompiler.CompileWithTimeProviderAsync` gained
`Expression? operationExpression = null` **before** `cancellationToken`. A caller that passes
`cancellationToken` positionally rather than by name will bind it to `operationExpression` and fail to
compile. A's data layer is unaffected (it uses `CompileFromOptionsAsync`), but this is the one place the
public surface moved, and it is silent for any caller using named arguments.

### Catalog-source note

A's csproj switch to decomposed DDL (`Ignixa.DataLayer.SqlServer.Database/Tables/*.sql`) depends on A's
Database project, which exists on neither `main` nor this branch. `97.sql` stays here. A re-applies the
switch after rebase if it wants it; §3 establishes that it does not need to for correctness.

---

## 5. Integration suite

```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

| Run | Result |
|---|---|
| Baseline (A's compiler, db `IgnixaGateBaseIntg`) | `Passed! - Failed: 0, Passed: 126, Skipped: 0, Total: 126, Duration: 12 m 2 s` |
| Unified (this compiler, db `IgnixaGateUniIntg`) | `Passed! - Failed: 0, Passed: 126, Skipped: 0, Total: 126, Duration: 12 m 10 s` |

Identical. No deviation to explain.

---

## 6. E2E suite

```
dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj
```

| Run | Result |
|---|---|
| Baseline (db `IgnixaGateE2EBase`) | `Failed! - Failed: 32, Passed: 568, Skipped: 20, Total: 620, Duration: 1 m 22 s` |
| Unified (db `IgnixaGateE2EUni`) | `Failed! - Failed: 31, Passed: 569, Skipped: 20, Total: 620, Duration: 1 m 15 s` |

**On the expected figure**: the task brief says "588 of 620 passing". 588 is `620 − 32`, which folds the
20 skipped tests into the passing count. Branch A's own gap analysis
(`.superpowers/sdd/e2e-gap-analysis.md`, measured twice against fresh databases) records
**620 / 568 / 32 / 20**, and the baseline measured here reproduced that exactly — same count, and the
failure *set* matched name-for-name including theory arguments. The 32 break down as A documented them:
13 `identifier:of-type`, 11 date/precision, 2 single-value `:not`, 2 `:count`-with-includes, 1 URI
`:below`/`:above` separator, 1 system-level `_type`, 1 cross-type `_has` architectural guard, 1
`_lastUpdated` sort scope guard.

### Failure-set diff, not just counts

Failing test names (with theory arguments) were extracted from both runs and diffed:

```
=== ONLY IN BASELINE (fixed by unification) ===
Ignixa.Api.E2ETests.Search.Basic.BasicSearchTests
  .GivenVariousTypesOfResources_WhenSearchingAcrossAllResourceTypes_ThenOnlyResourcesMatchingTypeParameterShouldBeReturned

=== ONLY IN UNIFIED (new regressions) ===
(none)
```

**The single delta is explained.** That test is the "System-level `_type` filter (1)" entry in branch A's
own gap-closure design, described there as: *"A bare system-level `_type=Patient` does not filter at all,
returning all tagged resources."* The baseline failure is `resources.Length should be 3 but was 5`.

It is closed by this branch's `ResourceTypes` forwarding, added in
`Tracing/SearchCompiler.cs` — `additionalResourceTypes: options.ResourceTypes` into `Resolve.RunAsync`
plus `ResourceTypes = options.ResourceTypes` on `LowerOptions`. The code comment there predicted exactly
this symptom before any of it had been executed:

> Without this forwarding a multi-`_type` search silently returns EVERY resource type rather than the
> requested subset [...] accepted by the API, never reaching Lower, invisible to a green build.

That is a text-only prediction confirmed by row-level execution. **The gap-closure plan written against
branch A is now stale by one item** — that group is closed here, and should not be re-planned.

Every other failure is byte-identical to baseline. No regression.

---

## 7. Gate 1 — `$export` surrogate-id partitioning (gates Task 8's decision)

Task 8 replaced A's outer-predicate splice with this branch's `SurrogateIdRange` plan input — the one
decision in the whole reconciliation that went to the *unexecuted* implementation, on design merits
(emits against `m.Sid1`, forces no `dbo.Resource` join, match-arm-only contract stated explicitly).

### Finding: the validating coverage did not exist

There is **no test on either branch that sets `SearchOptions.StartSurrogateId` / `EndSurrogateId`**. A
repo-wide search finds those identifiers only in production code and planning documents — zero hits under
`test/`. `SqlServerCompiledSearchServiceTests` covers `GetExportRangesAsync`, which is range *generation*
and bypasses the compiler entirely; nothing covered range *consumption*. `Ignixa.Api.E2ETests` contains no
`$export` tests at all.

So the `$export` partition tests this gate was told to consult are not there, and Task 8's decision was
gated on evidence that had never been collected.

### What was done

Wrote `SurrogateIdRangePartitionExecutionTests` (source preserved alongside this document), which
executes the path `ExportWorkerActivity` actually uses: `GetExportRangesAsync` for the windows, then one
search per window with `StartSurrogateId`/`EndSurrogateId` set, asserting the contract the whole operation
rests on — **non-overlapping and exhaustive** — plus a window below all data returning nothing.

### Outcome: **PASS. Task 8's decision holds.**

| Test | A's compiler | This compiler |
|---|---|---|
| `GivenExportRangesOverAResourceType_WhenEachRangeIsSearched_ThenThePartitionsAreDisjointAndExhaustive` | pass | **pass** |
| `GivenASurrogateIdRangeBelowAllData_WhenSearched_ThenNoResourcesAreReturned` | pass | **pass** |

Both shapes satisfy the partition contract at row level. The `SurrogateIdRange` change is row-equivalent
to the splice it replaced, so the decision was taken on design merit without a correctness cost. Nothing
to revert.

---

## 8. Gate 2 — untyped-reference declared-target narrowing

This branch changed which rows come back: `ReferenceColumnEquality` previously lowered an untyped
reference value to an id-only predicate; it now additionally constrains the row's
`ReferenceResourceTypeId` to the search parameter's declared target types (admitting stored NULLs only
when the parameter has more than one declared target). Text assertions cannot say whether that is right.

### Finding: A's suites never exercised the collision case

Branch A's E2E `ReferenceSearchTests` does cover untyped values
(`GivenAnUnqualifiedReferenceId_...`, `GivenQualifiedAndUnqualifiedReferences_WhenSearched_ThenSameResultsReturned`)
but every id in its fixture is server-assigned and distinct, and every query is additionally scoped by
`_tag`, so a cross-type id collision could not arise. The one test in the repo that builds a cross-type id
collision is a **compartment** test with typed index-side references — a different code path.

New coverage here was therefore, as the brief anticipated, a legitimate outcome.

### What was done

Wrote `UntypedReferenceCollisionDifferentialTests`: `Patient/{X}` and `Practitioner/{X}` share a natural
id; one Observation's `subject` points at `Patient/{X}`, another's at `Practitioner/{X}`; the query is
`subject={X}` with **no type prefix**. `Observation.subject` declares `Patient|Group|Device|Location` —
`Practitioner` is not among them, so the Practitioner-referencing row cannot be a legitimate match. A
second test asserts the narrowing costs nothing in the ordinary no-collision case.

### Outcome: **PASS, and the test is discriminating.**

| Test | A's compiler | This compiler |
|---|---|---|
| `GivenAnUntypedReferenceSearchWithANaturalIdCollisionAcrossResourceTypes_...` | **FAIL** | **pass** |
| `GivenAnUntypedReferenceSearchWithNoCollision_...` | pass | pass |

Under A's compiler:

```
Shouldly.ShouldAssertException : newResults.Select(r => r.ResourceId)
    should be
["untyped-real-d449b967c8d2476d92d17ffc48cba404"]
    but was (case sensitive comparison)
["untyped-real-d449b967c8d2476d92d17ffc48cba404", "untyped-decoy-99ff3331f5ec40c7b68239bc910dcad5"]
```

A returns a row whose `subject` points at a `Practitioner`, for a search parameter that cannot target
`Practitioner`. That is a false positive, and this branch removes it. **The rows the narrowing deletes are
rows that were never matches.** The change is correct, now demonstrated rather than argued.

---

## 9. Gate 3 — `EmitVisibleSinceFilter` row filter, and `_since`

Two known deltas from Task 7, both flagged because they are semantic changes to E2E-proven code:

- `EmitVisibleSinceFilter` now emits `AND r.IsHistory = 0 AND r.IsDeleted = 0` where A emitted no row
  filter. Argued row-identical under a maintained index, differing only in a stale-index degraded state.
- `_since` had unit coverage only. The captured legacy corpus cannot reach it (its one captured URL is
  `_since=3000`, not a parseable instant), so no `_since` query had ever been executed against a database
  on either branch. A's `Transactions.VisibleDate` semantics were kept deliberately.

### Finding: the check named for this does not exist

The brief nominates `$everything` E2E as the check on the row-filter argument. **There is no `$everything`
E2E test**, and no `$everything` integration test, on either branch. Coverage is unit-level only
(`PatientEverythingHandlerTests` against a mocked search service, and three `EndToEndCompilationTests`
cases that assert on SQL text). Likewise no `_since` E2E or integration test anywhere.

So neither of these two deltas was exercised by the E2E run in §6, and reporting §6 as their validation
would have been wrong.

### What was done

Wrote `PatientEverythingSinceExecutionTests`, executing the `$everything` traversal and its `_since`
filter against a real database through `SqlServerCompiledSearchService`. The cutoff is read back from
`dbo.Transactions.VisibleDate` rather than taken from the test host's clock, so the assertion tests the
filter instead of racing clock skew.

### Outcome: **PASS under both compilers — the row filter is row-identical, as argued.**

| Test | A's compiler | This compiler |
|---|---|---|
| `GivenAPatientCompartment_WhenEverythingIsSearched_ThenReturnsThePatientAndItsCompartmentMembers` | pass | **pass** |
| `GivenAPatientCompartmentWithASinceCutoff_WhenEverythingIsSearched_ThenOnlyMembersVisibleSinceThatTransactionAreReturned` | pass | **pass** |

The added `IsHistory`/`IsDeleted` clauses changed no rows: the filter only ever intersects the compartment
branch, which is sourced from `dbo.ReferenceSearchParam` and reaches current versions only. The stale-index
argument stands, and now has an executed result behind it rather than only reasoning.

`_since` itself ran against a database for the first time on either branch, and returned the correct
members — the late one, not the early one, with the patient itself unfiltered because `_since` is scoped
to the compartment branch only.

---

## 10. Two pre-existing defects found while building gate 3

Neither is caused by this reconciliation — both reproduce identically on branch A — but both were found by
executing paths nobody had executed, which is what this gate is for.

**10.1 `$everything` cannot compile as its handler wires it.**
`PatientEverythingHandler` builds `SearchOptions` with `ResourceType = null` (its comment: *"Multi-resource
type search"*). `Lower` rejects exactly that:

> `$everything is not supported in system-level search -- it is anchored on the Patient/Group type whose
> compartment it expands, so it has no meaning without one.`

That guard is byte-identical on both branches (`Lowering/Lower.cs`). Through
`SqlServerCompiledSearchService` the operation therefore throws `RequestNotValidException` before it
reaches SQL. Supplying `ResourceType = "Patient"` compiles and returns correct multi-type results —
`LowerPatientEverything` does not consult it, the null-guard is its only reader — so the fix is plausibly
one line in the handler. This is very likely why `$everything` has no E2E coverage.

**10.2 `_since` matches nothing on the `SqlServerFhirRepository` write path.**
`CreateOrUpdateAsync` opens a `dbo.Transactions` row per write (via `MergeResourcesBeginTransaction`) but
never commits it, so `VisibleDate` stays NULL on every row. `_since` compares against exactly that column,
so on this write path the filter matches nothing regardless of the predicate. Only the `MergeResources`
path sets `VisibleDate`. The gate test commits the ledger explicitly so its assertion is about the emitted
filter and not about the merge pipeline — but the production gap is real.

Both belong in a follow-up, not here.

---

## 11. New coverage — where it lives

The three test classes were written in A's working tree (the only place they can compile — this branch has
no `Ignixa.DataLayer.SqlServer`) and reverted with the rest of the harness. Their source is preserved
verbatim next to this document so A can adopt it on rebase:

- `docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/SurrogateIdRangePartitionExecutionTests.cs.txt`
  → `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SurrogateIdRangePartitionExecutionTests.cs`
- `docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/UntypedReferenceCollisionDifferentialTests.cs.txt`
  → `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/UntypedReferenceCollisionDifferentialTests.cs`
- `docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/PatientEverythingSinceExecutionTests.cs.txt`
  → `test/Ignixa.DataLayer.SqlServer.IntegrationTests/PatientEverythingSinceExecutionTests.cs`

Adopting them takes the integration suite from 126 to 132. They are the only executed coverage that exists
for surrogate-id range consumption, untyped-reference target narrowing, and `_since`.

---

## 12. Verdict

The unified compiler executes. Branch A's data layer builds against it with **zero adapter changes**,
its integration suite is unchanged at 126/126, and its E2E suite moves by exactly one test — in the
right direction, closing a gap A had already root-caused and scheduled.

Of the two decisions this gate existed to arbitrate:

- **Task 8's `SurrogateIdRange`** is row-equivalent to the splice it replaced. The decision stands.
- **The declared-target narrowing** removes rows that were never matches. The correctness claim holds,
  and now has a failing-under-A / passing-under-B test pinning it.

Both Task 7 deltas that worried the plan — the `EmitVisibleSinceFilter` row filter and `_since` — behave
as argued when executed.

The branch's own scope note said there was a class of defect it structurally could not detect. It has now
been looked for, in the three places most likely to hide it, and it is not there.
