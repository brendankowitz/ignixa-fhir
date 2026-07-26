# `Patient/$everything` completion — branch-A handoff

**Written from:** `worktree-search-sql-unified`, tip `ae9f0599` (Task 3 of
`docs/superpowers/plans/2026-07-25-patient-everything-completion.md`).
**For:** whoever runs `worktree-ignixa-datalayer-sqlserver` (branch A) after it rebases onto this branch.
**You were not here for this.** Read this whole document before running anything — the short version is
that this branch fixed two real `$everything` bugs and added compiler-level tests for them, but this
branch cannot execute a single query against a database, so none of that is proven against real data yet.
That proof is entirely yours to produce.

---

## 1. Why this document exists instead of an E2E test

This branch's `src/DataLayer/` holds `BlobStorage`, `FileSystem`, `InMemoryIndex`, and
`SqlEntityFramework` only. There is no `Ignixa.DataLayer.SqlServer` project, no `SqlServerCompiledSearchService`,
and production `ISearchService` here resolves to the legacy EF implementation. `Ignixa.Search.Sql` (the
compiler this plan's Tasks 1 and 2 fixed) is reachable **only** through `SearchCompiler` and the unit/
lowering test suites.

An earlier draft of this plan's Task 3 called for a `$everything` E2E test written here. That would have
exercised the legacy EF path — not the compiled traversal the two bugs below live in — and would have
looked like validation while proving nothing about the code that changed. It was corrected before any test
was written; see `.superpowers/sdd/2026-07-25-patient-everything-completion/task-3-brief.md`.

`SqlServerCompiledSearchService` exists only on branch A. Real end-to-end proof of everything in this
document has to run there, after A rebases onto this branch's fixes.

---

## 2. What this branch fixed (Tasks 1 and 2)

1. **Task 1** — `PatientEverythingHandler` built `SearchOptions` with `ResourceType = null` ("Multi-resource
   type search"). `Lower.cs` rejects a null `ResourceType` for a `PatientEverythingExpression` outright —
   the operation is anchored on the compartment root type, and the many returned types come from the
   traversal, not from a null anchor. Fixed to `ResourceType = "Patient"`.
2. **Task 2** — `LowerPatientEverything`'s outbound referenced-type expansion (`generalPractitioner`,
   `managingOrganization`, etc.) was seeded from the compartment branch alone. The seed patient is not a
   member of its own compartment — no `ReferenceSearchParam` row points from the patient at itself — so a
   `generalPractitioner`/`managingOrganization` reachable only from the patient row (no compartment member
   happens to reference the same target) was silently dropped. Fixed to seed from
   `Union(patientItself, filteredCompartment)`. This bug is shared with the legacy EF generator
   (`PatientEverythingQueryGenerator`), so it is not a regression introduced by unification.

Both are real defects, not test-harness artifacts — confirmed by reading `Lower.cs` and
`StructuralContext.cs` directly, not inferred from a failing test alone (see `task-1-report.md` and
`task-2-report.md` in the same directory as the brief).

---

## 3. What Task 3 verified on this branch, and what it genuinely proves

Extended `test/Ignixa.Search.Sql.Tests/Lowering/PatientEverythingLoweringTests.cs` (already substantial
after Tasks 1 and 2) with one more case:

**`GivenAConstrainedReferencedType_WhenReachedThroughEverythingsExpansion_ThenTheConstraintStillNarrowsThatType`**
— the pre-existing `AccessConstraint` coverage (`GivenAConstrainedMemberType_...`) only constrained a
*compartment-member* type (Observation), narrowed via `CompartmentSource`. It never touched a type
reachable *only* through the `ReferencedTypeExpansion` CTE (Practitioner/Organization/Location/Medication —
Task 2's expansion). Those are a structurally different row-producing stage, and `AccessConstraintApplier
.ApplyToTypes` wraps the *whole* `$everything` match set (patient-itself ∪ filtered-compartment ∪
expansion) in one pass, so an untested path here would be a real gap, not a redundant one. The new test
asserts the narrowed base (`Except`/`Intersect` left operand) still reaches the `ReferencedTypeExpansion`
CTE — proving the constraint wraps the expansion's output, not just the branches beneath it.

**What this proves:** the CTE graph and emitted SQL shape are structurally correct — the right joins, the
right set operations, the right CTE kinds, parameter ordinals matching between `Explain()` and the emitted
SQL, and now an access constraint on an expansion-only type narrowing the right stage.

**What this does not prove, and cannot prove from here:** that any of this returns the right *rows* against
a real `dbo.Resource`/`dbo.ReferenceSearchParam`/`dbo.Transactions`. Lowering tests assert on `QueryPlan`
shape and SQL text; they never execute. Row-level proof is exactly what Sections 4 and 5 below ask for.

The existing pinned `Explain()` golden
(`GivenAFullyFeaturedPatientEverythingPlan_WhenExplained_ThenEveryParameterOrdinalMatchesTheEmittedSql`,
already updated by Task 2 for the seed-union CTE) was left as-is — extended by the new test's own
assertions rather than re-pinned, per the brief's instruction not to duplicate that mechanism.

**Corpus drift measured, not assumed:** `Ignixa.Search.Sql.Tests` — **804 passed / 0 failed** on both
net9.0 and net10.0 (baseline was 803/0; the delta is exactly the one test added above).
`DivergenceBaseline.DivergingQueries` count is unchanged at 59 — this task touched no corpus/divergence
accounting.

---

## 4. `$everything` E2E — write this, expect it to pass

**Why it can't run here:** needs `SqlServerCompiledSearchService`, a live SQL Server, and
`Ignixa.DataLayer.SqlServer.IntegrationTests`' `TestTenantDatabase` fixture — none of which exist on this
branch.

**Why the existing gate test (Section 6) doesn't already cover this:** the six preserved gate tests'
`$everything` coverage (`PatientEverythingSinceExecutionTests`) calls its `EverythingAsync` helper with
`includeReferencedResources: false` in **both** of its tests. Neither one ever exercises the
`ReferencedTypeExpansion` path — Task 2's fix has **no executed row-level coverage anywhere**, on either
branch. This is the actual gap; write a new test rather than editing the adopted one, so the seam Task 2
patched has a test that would have caught the pre-fix bug.

**Scenario — this is Task 2's isolation case, stated precisely so the seed bug stays visible:**

- Seed one Patient.
- Seed several compartment members of different types (e.g. an Observation and an Encounter, both
  `subject`/`patient`-referencing the seed Patient) — this exercises multi-type compartment traversal, not
  just the expansion.
- Set the Patient's `generalPractitioner` and `managingOrganization` to a Practitioner and an Organization
  that **no compartment member references** — if any compartment member also references the same
  Practitioner/Organization, the compartment branch alone would surface them and the seed-union fix
  becomes untestable (this is exactly the mistake Task 2's own compiler-level test avoids; carry the same
  discipline into the E2E version).
- Optionally include a Location and/or Medication referenced only from a compartment member (not from the
  Patient row) to prove the expansion also reaches through compartment members, not only through the
  patient-itself branch.
- Run `$everything` with `includeReferencedResources: true` (however that's threaded to
  `PatientEverythingExpression` in A's current handler/service wiring — confirm the flag reaches the
  service call before trusting a negative result).
- **Assert the returned bundle equals exactly** `{patient} ∪ {compartment members} ∪ {referenced
  Practitioner, Organization, Location, Medication}` — no more, no fewer. Seed at least one unrelated
  "stranger" Patient with its own compartment member (same shape as the existing gate test's stranger
  case) to prove the traversal doesn't leak across patients.

**Expected outcome: pass.** Both fixes are believed correct — Task 1 and Task 2 confirmed the code reads
right by inspection, and the compiler-level tests confirm the CTE graph and SQL shape. A failure here would
mean the fix doesn't hold at the row level, which would be new information worth investigating immediately,
not a known risk being worked around.

---

## 5. `_since` E2E — write this, expect it to FAIL

**Why it can't run here:** same reason as Section 4 — no compiled search service, no live database.

**Why the existing gate test doesn't already cover this:** the adopted
`GivenAPatientCompartmentWithASinceCutoff_...` test (Section 6) **works around** the defect below by
issuing a raw `UPDATE dbo.Transactions SET VisibleDate = ...` before running the search, specifically so
its assertion is about the emitted `_since` SQL filter and not about the merge pipeline. That's the right
test for what it's testing, and you should still adopt it (Section 6) — but it means the production write
path's defect has never been exercised end-to-end without a workaround. This section asks for that missing
test.

**The defect, precisely** (already documented in
`docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md` §10.2, restated here because it's
the reason this test is expected to fail): `SqlServerFhirRepository.CreateOrUpdateAsync` opens a
`dbo.Transactions` row per write via `MergeResourcesBeginTransaction`, but never commits it. Only the
`MergeResources` stored-procedure path (with `@TransactionId` supplied so it internally calls
`MergeResourcesCommitTransaction`) sets `VisibleDate`. On the plain `CreateOrUpdateAsync` write path,
`VisibleDate` stays `NULL` forever. `_since` filters on exactly that column
(`EmitVisibleSinceFilter` / `VisibleSinceFilter`), so on this write path the filter matches nothing,
regardless of the predicate or cutoff value.

**Scenario:**

- Seed a Patient and one or more compartment members through **the actual production write path** used by
  the rest of A's integration/E2E suite for `$everything` (i.e. whatever `CreateOrUpdateAsync`-based
  helper is idiomatic there — do not patch `VisibleDate` manually, that's what the adopted gate test
  already does and is not what this test is for).
- Run `$everything` with `_since` set to a cutoff that should, if `VisibleDate` were populated normally,
  admit at least one of the seeded members (e.g. "1 hour ago", or read back `SYSDATETIMEOFFSET()` before
  seeding).
- Assert the compartment member(s) are **absent** from the result (only the patient-itself row returns,
  since the patient branch is never filtered by `_since`).

**Expected outcome: FAIL to return the member — i.e., the assertion above (member absent) should PASS,
which is the uncomfortable way of saying the filter matches nothing.** Word this test so a reader
immediately understands a "pass" here is documenting a known defect, not validating correct behavior — for
example, name it something like
`GivenASinceQueryAgainstTheProductionWritePath_WhenEverythingIsSearched_ThenNoMemberIsReturnedBecauseVisibleDateIsNeverCommitted`,
and open its arrange section with a comment pointing at this document and at
`SqlServerFhirRepository.CreateOrUpdateAsync`.

**If this test instead returns the member correctly:** stop and investigate before assuming it's good news.
Either the defect was fixed elsewhere without this document being updated, or the test isn't actually
exercising the plain `CreateOrUpdateAsync` path (e.g. a shared fixture silently routes through
`MergeResources` with a transaction id, or a prior seeding call in the same test committed the ledger as a
side effect). A silently-passing `_since` test here is a sign the test is wrong, not the code.

**This is a known pre-existing defect, not something to fix as part of closing out this plan.** It belongs
in its own follow-up (fixing `CreateOrUpdateAsync`'s transaction lifecycle is a `SqlServerFhirRepository`
change, well outside `$everything`/`Ignixa.Search.Sql` scope). This test's job is to convert "we found this
by reading code" into "we have an executed regression test that will turn green the day someone fixes it."

---

## 6. The six preserved gate tests — adopt all six, none belong here

`docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/` holds three `.cs.txt` files, compiled by
nothing on this branch:

| File | Tests | Target namespace/type |
|---|---|---|
| `PatientEverythingSinceExecutionTests.cs.txt` | `GivenAPatientCompartment_WhenEverythingIsSearched_ThenReturnsThePatientAndItsCompartmentMembers`, `GivenAPatientCompartmentWithASinceCutoff_WhenEverythingIsSearched_ThenOnlyMembersVisibleSinceThatTransactionAreReturned` | `Ignixa.DataLayer.SqlServer.IntegrationTests` |
| `SurrogateIdRangePartitionExecutionTests.cs.txt` | `GivenExportRangesOverAResourceType_WhenEachRangeIsSearched_ThenThePartitionsAreDisjointAndExhaustive`, `GivenASurrogateIdRangeBelowAllData_WhenSearched_ThenNoResourcesAreReturned` | `Ignixa.DataLayer.SqlServer.IntegrationTests` |
| `UntypedReferenceCollisionDifferentialTests.cs.txt` | `GivenAnUntypedReferenceSearchWithANaturalIdCollisionAcrossResourceTypes_WhenSearchedOnBothEngines_ThenTheCompilerExcludesTheUndeclaredTarget`, `GivenAnUntypedReferenceSearchWithNoCollision_WhenSearchedOnBothEngines_ThenBothEnginesStillReturnTheMatch` | `Ignixa.DataLayer.SqlServer.IntegrationTests.Differential` |

Six tests total, matching the plan's count. **The split is trivial: all six belong to branch A, zero are
adoptable here.** Every one of them:

- references `SqlServerCompiledSearchService`, `SqlServerSymbolResolver`,
  `SqlServerSearchIndexReferenceDataCache`, or `DifferentialTestHarness` — types that live in
  `Ignixa.DataLayer.SqlServer` / its integration-test project, neither of which exists on this branch;
- requires a live SQL Server via `TestTenantDatabase` — this branch has no such fixture, and the brief
  explicitly forbids building one just to force these tests to compile here.

There is no partial-adoption case to reason about (e.g. "the SQL-text assertions could run standalone") —
every test in all three files executes against a real database and asserts on returned rows, not on SQL
text alone (`UntypedReferenceCollisionDifferentialTests` is explicit about this: it asserts at row level
"because it is the only kind of evidence that can say which rows the change removed, and whether removing
them was right").

**Adoption is mechanical** — these were already written and run to green against a branch-A working tree
during the unified-execution-gate exercise
(`docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md`, which measured
`Ignixa.DataLayer.SqlServer.IntegrationTests` at 126/126 before adoption and describes moving to 132 with
these six added) and then reverted, preserved here verbatim so they wouldn't have to be rewritten:

```
docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/SurrogateIdRangePartitionExecutionTests.cs.txt
  → test/Ignixa.DataLayer.SqlServer.IntegrationTests/SurrogateIdRangePartitionExecutionTests.cs
docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/UntypedReferenceCollisionDifferentialTests.cs.txt
  → test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/UntypedReferenceCollisionDifferentialTests.cs
docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/PatientEverythingSinceExecutionTests.cs.txt
  → test/Ignixa.DataLayer.SqlServer.IntegrationTests/PatientEverythingSinceExecutionTests.cs
```

Rename `.cs.txt` → `.cs`, drop the files at the target paths (creating the `Differential/` subfolder if it
doesn't already exist there), and run. They were green against branch A once already; re-verify rather than
assume, since the rebase may have moved code underneath them (`SqlServerCompiledSearchService`'s
constructor shape, `TestTenantDatabase`'s helper surface, etc.) — a mechanical adoption that doesn't
compile is a normal outcome to plan for, not a sign the tests are wrong.

Note `PatientEverythingSinceExecutionTests` already covers the same `_since`-filter-narrows-the-compartment-
branch assertion as Section 5, but through the workaround path (manual `VisibleDate` UPDATE). Adopt it as-is
— it's correct for what it tests — and add Section 5's test alongside it as a separate class or a separate
fact, not a replacement.

---

## 7. Summary — what to do, in order

1. Rebase `worktree-ignixa-datalayer-sqlserver` onto this branch's tip (`ae9f0599` or later).
2. Adopt the six gate tests (Section 6) — rename, drop in place, run, fix any compile breaks from the
   rebase.
3. Write the `$everything` E2E test (Section 4) — expected to **pass**.
4. Write the `_since` production-write-path E2E test (Section 5) — expected to **fail** (member absent),
   documenting the pre-existing `CreateOrUpdateAsync` transaction-commit defect rather than working around
   it.
5. File a follow-up for the `CreateOrUpdateAsync`/`VisibleDate` defect (Section 5) if one doesn't already
   exist — this document is not that follow-up, it only proves the defect is real and executable.

## 8. What remains genuinely unproven after this document is acted on

Even after Sections 3-6 are complete, still unproven anywhere in the codebase:

- `$everything` combined with `_type` filtering *and* `includeReferencedResources` together, executed
  against real data (compiler-level coverage exists; no E2E combination test is asked for above).
- `$everything`'s conditional clinical-date filter (`ApplyConditionalDateFilter`) at row level — only
  unit/lowering-tested on this branch, and Sections 4-5 above don't ask for it either. Worth a follow-up
  E2E test if `$everything`'s date-range parameters see real traffic.
- `Group/$everything` — per `task-1-report.md`, the expression layer supports multiple patient ids
  (`PatientEverythingExpression`'s multi-id constructor) but no handler or endpoint currently constructs
  that path anywhere in the codebase. Not this plan's scope; flagged so it isn't mistaken for coverage that
  exists.
