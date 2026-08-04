# Phase F — revised plan for the remaining work

**Supersedes tasks 5–9 of** `docs/superpowers/plans/2026-07-26-datalayer-sqlserver-phase-f.md`.
Tasks 0–4 of that plan are complete and unchanged; this document replaces everything after them.

**Why revise.** The original plan was written from a file listing. Executing tasks 0–4 contradicted it four
times, and the two largest tasks are still ahead. Three things learned are load-bearing for the rest:

1. **The EF layer has drifted from the deployed schema, and much of it is unreachable.** Nine inherited
   defects in four tasks: an entity model that cannot insert (`BackgroundJobEntity` omits a `NOT NULL`
   primary-key column), a registration that cannot resolve (`ISystemRepository` needs an unregistered
   `FhirDbContext`), seven methods with a permanently inert `fhirVersion`, a version sort that is lexical
   while claiming semantic, a duplicate-key check by message substring, and a mapper reading 11 of 17
   columns. "Port as-is" assumed there was working behaviour to port. Often there wasn't.
2. **Deferred wiring is accumulating on one task.** Tasks 3 and 4 both deferred their registration to the
   composition-root task, which also owns a storage rename and a consumer signature change. That task is
   becoming the riskiest in the phase while being planned as a single unit.
3. **The remaining 2,645 lines have no test coverage at all.** There is no terminology test project and no
   terminology test class; the subject appears only incidentally in E2E fixtures. Tasks 5–6 are the largest,
   least covered, and most data-destructive work in the phase, and the EF oracle disappears at deletion.

---

## Revised Global Constraints

Replacing the original's "port behaviour as-is" with the rule that actually emerged. For each behaviour
found, classify before writing code:

- **Working and reachable** → port exactly, including quirks. Test against EF first, prove green there,
  then repoint. (Task 1's model.)
- **Working but its premise is false** → reproduce, and make the falsity explicit in code with a pinning
  test. Never silently reproduce a defect a reader would take for intent. (Task 4's `fhirVersion`.)
- **Not functional** → it cannot be ported. Write a correct implementation against the schema, take only the
  behavioural *rules* from the EF source, and say so in the class doc. (Task 2's model.)

**Fixing a defect during a port requires evidence the fix is safe**, not just that the defect is real. The
`fhirVersion` filter is real and its "fix" empties `/metadata`; that was only visible after checking what
callers pass against what is stored. Establish the blast radius first.

Everything else from the original Global Constraints stands: the acceptance baselines, `SqlCatalog` for
identifiers, no compiler changes, and the environment settings.

**One baseline changes.** Main has moved since the original plan (`#368` touches the compiler this branch
shares). Baselines are re-measured after Task R below, not before.

---

## Revised ordering, and why

The original ran terminology → move → composition root → delete. Two changes:

**The composition root moves earlier and splits.** Everything it constructs *except* terminology is already
ported. Relocating that part now lands Tasks 1–4's wiring immediately instead of stockpiling it, shrinks the
keystone, and means Tasks 5–6 can wire as they go rather than adding to the pile.

**The main merge happens before the composition-root work, not after deletion.** `#368` touches
`Ignixa.Search.Sql`, and merging after Task 8 would conflict directly with the file that task rewrites.

Revised order: **R → 5a → 5b → 6 → 7 → 8 → 9 → 10**.

---

### Task R: Merge `origin/main`

Small, and it must precede the composition-root work.

- [ ] Merge `origin/main` into the branch. Resolve the compiler tree to main's, as the earlier merge did —
      main is authoritative for `Ignixa.Search.Sql`.
- [ ] Re-measure and record every baseline. `#368` ("Type IsPartial as bit in the include limit stage")
      changes emitted SQL, so the compiler count and possibly E2E may legitimately move.
- [ ] Record the new numbers as the acceptance baselines for the rest of the phase.

---

### Task 5a: Relocate the composition root, except terminology

Lands the wiring Tasks 1–4 deferred.

**Files:** create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerServiceFactory.cs`; modify
`DataLayerRegistration.cs`, `ConformanceServicesRegistration.cs`.

- [ ] Read `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` in full and list what must survive:
      schema deployment, search-parameter catalog sync, once-per-tenant reference-cache preloading. Only the
      `FhirDbContext`/`dbContextOptions` construction is genuinely dead. That list goes in the task report
      before the replacement is written.
- [ ] Register `SqlServerSearchIndexReferenceDataCache` per tenant so `ISystemRepository` and
      `IPackageResourceRepository` can resolve — the specific blocker both deferrals named.
- [ ] Wire `SqlServerSystemRepository` (Task 3) and `SqlServerPackageResourceRepository` (Task 4).
- [ ] Leave terminology on EF: the factory still hands `ImportTerminologyResourceActivity` what it needs
      until Task 6 lands.
- [ ] Verify against the Task R baselines, plus a **real application start**.

---

### Task 5b: Build the terminology oracle — *tests only, no port*

**This is the task that makes 5–6 safe, and it is new.** 2,645 lines with no coverage cannot be ported
against nothing, and the EF implementation stops existing at Task 9. Its behaviour has to be captured while
it still runs.

**Files:** create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/` — tests written
**against the EF implementation only**. No production code changes in this task at all.

- [ ] Cover `SqlTerminologyService`'s seven operations: `LookupCode`, `ExpandValueSet`, `ValidateCode`,
      `ValidateBinding`, `TranslateCode`, `Subsumes`, `GetImportStatus`. Each needs its negative case
      (unknown code, unknown system) — these return typed results rather than throwing, and the exact shape
      is easy to get wrong.
- [ ] Seed a CodeSystem with a **two-level hierarchy** so `SubsumesAsync` has something real to traverse,
      and a ValueSet that references it so `ExpandValueSetAsync` does too.
- [ ] Cover `SqlCodeSystemImporter`'s three entry points, including **both insert strategies**: a CodeSystem
      below the 1,000-concept `BulkInsertThreshold` and one above it must produce identical row state via
      different code paths.
- [ ] Cover idempotency: importing the same resource twice must not duplicate.
- [ ] Cover a ValueSet that references a CodeSystem imported in the same test — the cross-reference
      resolution is where the importer does most of its work.
- [ ] **Every test must be green against EF before this task is done.** A test that cannot be made green
      against EF is a finding: record it, it means that behaviour does not work today.

---

### Task 6: Port terminology

Now a port against a real oracle rather than a rewrite in the dark. Three commits: service, then importer
CodeSystem/ValueSet, then ConceptMap.

**The atomicity constraint, which the original plan missed.** `ISqlExecutionService` has **no transaction
API** and opens a fresh connection per call, so nothing can span calls. EF's `SaveChangesAsync` is atomic
per call, meaning the importer's nine boundaries are already nine separate transactions and partial imports
are already possible — which is why `TerminologyImportStatus` exists as a column. The port must therefore:

- [x] Map every `SaveChangesAsync` call site to exactly one atomic unit, and implement each as a single
      batch with explicit `BEGIN TRANSACTION`/`COMMIT TRANSACTION` in the command text. A 200k-concept
      insert that fails halfway must not leave a half-populated CodeSystem with no record that it is partial.
      *Done as three stored procedures — `dbo.ImportTermCodeSystem`, `dbo.ImportTermValueSet`,
      `dbo.ImportTermConceptMap` — each taking its rows as a table-valued parameter. A procedure is the only
      place the sequence can be atomic given no client-side transaction API, and a TVP is a parameter rather
      than session state, which a `#temp` table is not: pooled reuse issues `sp_reset_connection` and drops
      temp objects even on the same SPID.*
- [x] ~~Keep the 1,000-concept threshold and both insert paths, so behaviour does not change at the
      boundary.~~ **Deliberately not done.** The threshold *was* the defect: only the bulk path ran parent
      resolution, so every CodeSystem at or below 1,000 concepts imported flat and `$subsumes` answered
      "not-subsumed" for every pair in it. One path now serves both sizes and the boundary has no behavioural
      meaning left. `GivenACodeSystemAtTheOldThreshold_WhenImported_ThenSizeNoLongerDecidesAnything` pins it.
- [ ] **Add a volume test.** Real CodeSystems (SNOMED, LOINC) are orders of magnitude past the threshold;
      the insert path's performance and memory profile are part of its contract, not an implementation
      detail. Record timing so a regression is visible. *Still open — carried to the follow-ups register.*
- [x] Repoint Task 5b's oracle tests. Assertions must not change.

**Defects found and fixed while porting**, each pinned by a test. The first is the one with user-visible
reach; the rest were all failure modes that produced wrong data or an opaque error rather than a diagnosis.

| # | Defect | Effect |
|---|--------|--------|
| 1 | Parent resolution ran on only one of two insert paths | Every CodeSystem ≤ 1,000 concepts imported flat: `$subsumes` wrong for every pair, and every compose `is-a` filter over one resolved to nothing |
| 2 | Exclude filters used a weaker evaluator whose default arm left the query unrestricted | `exclude` with any filter it did not understand — e.g. SNOMED's `property: concept, op: is-a` — selected **every code in the system** and removed them all |
| 3 | Unsupported filter operators matched everything | A narrow include quietly became "the whole CodeSystem" |
| 4 | `descendent-of` resolved through the same helper as `is-a` | Every `descendent-of` filter was wider by exactly the named code |
| 5 | Missing system URI resolved to `SystemId` 0, which no `dbo.System` row has | An expansion entry without a system, or a ConceptMap group without a target, failed the **entire** import on a foreign key violation reported as an opaque SQL error |
| 6 | `expansion.contains` read one level deep | A grouped expansion imported as its group headers alone, or as nothing |
| 7 | Unresolvable `exclude`d ValueSets were passed over silently | The expansion kept codes that were meant to be removed while reporting itself complete |
| 8 | Only R4's `equivalence` was read, not R5's `relationship` | Every R5 mapping stored as "equivalent", including ones saying the opposite |
| 9 | `ImportErrorMessage` was written message-plus-stack-trace into `NVARCHAR(1000)` | The truncation exception was swallowed by a nested catch, losing the error entirely |

---

### Task 7: Move `HybridTerminologyService`

Unchanged from the original: a namespace move, not a port. Confirm no EF dependency first; if any exists, it
is a port and needs its own cycle.

---

### Task 8: Finish the composition root

What 5a deliberately left: terminology wiring, `ImportTerminologyResourceActivity`'s signature change (the
one consumer that takes a `FhirDbContext` directly), and the storage-type rename to `SqlServer` with
`SqlEntityFramework` kept as a deprecated synonym.

---

### Task 9: RESULT — PASSED 2026-07-30 at `2829091b`

Every test project enumerated from `All.sln` — 28 suites, **zero aborts anywhere**, exit codes checked
individually rather than inferred from a summary line.

| Suite | Result |
|---|---|
| `DataLayer.SqlServer.IntegrationTests` | **313** / 0 |
| `Search.Sql.Tests` | 1096 per TFM |
| `Application.Tests` | 1180 / 0 / 1 skip |
| `FhirPath.Tests` | 4005 / 0 / 1 skip, both TFMs |
| `FhirFakes.Tests` | 1428, both TFMs |
| `Validation.Tests` | 678, both TFMs |
| `FhirMappingLanguage.Tests` | 659 / 1 skip, both TFMs |
| `TestScript.Tests` | 379, both TFMs |
| `DeId.Tests` | 203 / 1 skip, both TFMs |
| `Api.Tests` 151 · `DataLayer.SqlServer.Tests` 70 · `RepoGuards` 17×2 · 14 others | all green |
| `SqlEntityFramework.IntegrationTests` | 95 / 0 / 8 skip — *disappears with the project* |
| **`Api.E2ETests`** | **571 / 29 / 20 of 620** — documented gaps, 2 fewer than pre-merge |
| **`SqlOnFhir.Tests`** | **54 / 2** — upstream submodule drift, not ours |

**The app-start check, which no suite covers.** Every composition-root registration changed this phase, and a
DI resolution failure surfaces nowhere else. `dotnet run` on `Ignixa.Web`: bound both ports, `Application
started`, tenant package preload completed, capability statements built for tenants 0 and 1,
`ConformanceStateSyncService` polling. **Zero resolution failures.** Then over HTTP against a real database:

```
GET  /metadata                  → 200, valid CapabilityStatement
POST /Patient                   → 201
GET  /Patient?family=GateCheck  → 200, Bundle containing the written resource
```

Write path and search path both live through `SqlServerTenantServiceFactory`. A second startup against
already-imported state also ran the ported package-load sync cleanly and invalidated the capability cache —
the path changed from swallow to rethrow, exercised where a naive implementation would throw on every restart.

---

### Task 9: Pre-deletion gate — *verification only*

**New, and non-negotiable.** Deletion is the one irreversible step; it removes the rollback lever for a
live, unflagged read cutover with 31 documented search gaps still open. Before Task 10 touches anything:

- [ ] `dotnet build All.sln` — 0/0.
- [ ] Every suite at the baselines below, **measured 2026-07-29 at tip `9e6ded2b`** rather than quoted from
      memory:

      | Suite | Baseline | Notes |
      |---|---|---|
      | `Ignixa.Application.Tests` | 1125 pass / 0 fail / 1 skip — 1126 total | |
      | `Ignixa.Api.Tests` | 135 / 0 / 0 | |
      | `Ignixa.Search.Sql.Tests` | 849 per TFM, net9.0 + net10.0 | multi-targeted; two result lines |
      | `Ignixa.DataLayer.SqlServer.Tests` | 20 / 0 / 0 | |
      | `Ignixa.DataLayer.SqlServer.IntegrationTests` | **260 / 0 / 0** | was quoted as 187; see below |
      | `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` | 95 / 0 / 8 skip — 103 total | disappears at Task 10 |
      | `Ignixa.Api.E2ETests` | **569 / 31 / 20 — 620 total** | confirmed real; 31 are the documented gaps |
      | `Ignixa.SqlOnFhir.Tests` | **54 / 2 / 0 — 56 total**, both TFMs | see below — NOT ours, do not try to fix at the gate |
      | `Ignixa.RepoGuards.Tests` | 17 / 0 / 0, both TFMs | was 16/1 until `PackageStability` was corrected |
      | `Ignixa.Validation.Tests` | 678 / 0 / 0, both TFMs | slowest non-SQL suite, ~2.5 min |
      | `Ignixa.FhirPath.Tests` | 3966 total (1 skip), both TFMs | |
      | `Ignixa.FhirFakes.Tests` | 1428 / 0 / 0, both TFMs | |
      | `Ignixa.DeId.Tests` | 204 total (1 skip), both TFMs | |
      | `Ignixa.FhirMappingLanguage.Tests` | 536 total (1 skip), both TFMs | |
      | `Ignixa.TestScript.Tests` | 379 / 0 / 0, both TFMs | |
      | `Ignixa.Extensions.Tests` | 89 / 0 / 0, both TFMs | |
      | `Ignixa.Models.Tests` / `.R4.Tests` | 130 / 63 | |
      | `Ignixa.NarrativeGenerator.Tests` | 110, both TFMs | |
      | `Ignixa.PackageManagement.Tests` | 86, both TFMs | |
      | `Ignixa.Serialization.Tests` | 85 | |
      | `Ignixa.Search.Sql.Generators.Tests` | 11, both TFMs | |
      | `Ignixa.Application.Experimental.Tests` | 43 | |
      | `Ignixa.ConformanceMatrix.Cli.Tests` | 68 | |
      | `Ignixa.SchemaUpgrade.Cli.Tests` | 13 | |
      | `Ignixa.SqlOnFhir.Cli.Tests` | 36 | |
      | `Ignixa.FhirFakes.Cli.Tests` | 28 | |
      | `Ignixa.Validation.Cli.Tests` | 14 | |

      **28 suites, ~11,000 tests. 26 green; two carry known failures.** The first census covered six suites
      chosen by hand and reported them "all green" while `RepoGuards` was failing on this branch and
      `SqlOnFhir` was failing for an unrelated reason — the same error as the `187` baseline, a specific
      number that looked authoritative. Enumerate test projects from `All.sln`; do not curate the list.

      `Ignixa.SqlOnFhir.Tests`'s two failures — `OfficialSqlOnFhirTestRunner` and
      `SqlOnFhirReportCoverageTests` — are driven by `test/Ignixa.SqlOnFhir.Tests/sql-on-fhir-tests`, a git
      submodule tracking upstream `FHIR/sql-on-fhir.js`. Upstream corpus drift, unrelated to this phase.
      Expected at the gate; investigate only if the count changes.

      The integration baseline was **187/187 and that number never existed**. The fixture's unserialised
      `CREATE DATABASE` timed out under sixteen-way xUnit parallelism, so every run silently lost a random
      subset. Fixed in `bbf2bc0e`. The E2E baseline, suspected of the same defect, was re-measured and is
      genuine.

- [ ] **How to read a suite result.** `dotnet test` prints `Passed!` on its summary line even when the run
      aborts, and exits 0 if some tests passed first — the crash notice is on the *preceding* line. Grepping
      for `Passed!`/`Failed!` therefore reports a clean suite for a run that died halfway; this happened
      three times in this phase. Check for `aborted`/`crashed` anywhere in the output **and** check the exit
      code. Do **not** compare the reported total against `--list-tests` output: that lists a `[Theory]` once
      while the run counts each `[InlineData]` case, so it undercounts (801 vs 849 on `Search.Sql.Tests`) and
      would fail every run on a theory-heavy suite.
- [ ] **E2E at exactly the Task R baseline, matching on failing test names**, not just counts.
- [ ] **A real application start** against a real tenant database, exercising terminology import and package
      load — the two areas whose ports have the least prior coverage. Phase B's ~10 missing tables were
      found by running the app, not by any test.
- [ ] Confirm nothing outside the EF project references `FhirDbContext` or the project. Any hit means an
      earlier task is incomplete: finish it rather than deleting around it.

If any of these fail, **stop**. Task 10 does not start.

---

### Task 10: DONE — `21502c35`, 2026-08-04

149 files, 33,540 deletions. Integration **313 → 224**, reconciling exactly as 313 − 50 differential − 39
oracle, both counted from the deleted files rather than taken from this plan. `TerminologyOracleFixture`
became `TerminologyTestFixture` — surgery, since six surviving test files depend on it, and "oracle" would
have described a job that no longer exists.

**Phase F is complete. One data layer.**

What it cost and produced, because the ratio is the useful part:

- **~20 defects found.** Four were introduced *by the port* and caught only by comparing against the EF
  implementation — the argument for keeping it readable until the end, and the reason deletion was gated on
  the disposition table rather than on the suites being green.
- **6 tests that passed for the wrong reason**, two of them written during this phase. The worst was the
  differential hard-delete sweep, which asserted 15 tables were empty against a resource that had no search
  indices at all — it would have passed if delete swept nothing. Mutation-checking is the only reason the
  replacements are trustworthy.
- **A regression on `main`, found here.** #381 moved the token overflow split to `MaxLength` (256) while
  every row generator writes at 128, so overflowing token codes silently stop matching. Main's tests derive
  the expected width from the same value the rule reads, so they agreed with the wrong number. Fixed in
  `5c9556d2` with a guard that compares the compiler's constant against the generators' behaviour — **this
  fix exists only on this branch.**

- [ ] Amend `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md` — it assumes the legacy
      engine is available for row-level comparison when closing the remaining search gaps. It is not.
- [ ] Amend `docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md` — its differential
      evidence is now historical rather than reproducible.

---

### Superseded plan text for Task 10

As the original Task 9: the project, the 50 differential facts, the two EF test projects, the E2E project
reference, and `All.sln` entries. Plus:

- [ ] Amend `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md` — it assumes the legacy
      engine is available for row-level comparison when closing the remaining 31 search gaps.
- [ ] Amend `docs/superpowers/specs/2026-07-25-unified-execution-gate-results.md` — its differential
      evidence becomes historical rather than reproducible.

---

## Follow-ups register

Pinned by tests or comments during this phase, deliberately not fixed. Recorded here so they do not
evaporate when the plan closes.

| Item | Where | Why deferred |
|---|---|---|
| `fhirVersion` inert on 7 package methods | `SqlServerPackageResourceRepository` | Needs version normalisation + set-membership; naive equality empties `/metadata` |
| `tenantId` inert on `PackageVersionExistsAsync` | same | `dbo.PackageResource` has no tenant column; `ImplementationGuideProvider` assumes otherwise |
| `MapEntityToModel` reads 11 of 17 columns | same | Inherited; callers have never seen those fields populated |
| Background jobs default to in-memory | `BackgroundJobsModule` | Job state does not survive restart; switching the default is a production change |
| `CreateOrUpdateAsync` never commits its transaction row | `SqlServerFhirRepository` | `VisibleDate` stays NULL, so `_since` matches nothing on that path |
| Device and Location inert in the Patient compartment | `R4CompartmentDefinitions` | Zero linking parameters; silently absent from `$everything` |
| 31 E2E search gaps, 5 groups | gap-closure design | Their oracle disappears at Task 10 |
| No volume test for CodeSystem import | `dbo.ImportTermCodeSystem` | Largest case exercised is 1,001 concepts; real CodeSystems are orders of magnitude past it and the TVP's memory profile at that size is unmeasured |
| Compose filter ops `not-in`, `generalizes`, `exists` unevaluated | `SqlServerValueSetComposer` | Now reported through `IsPartialExpansion` rather than silently guessed, so the gap is visible; implementing them is a feature, not a port |
| **Token split-point fix is only on this branch** | `TokenColumnEquality`, `5c9556d2` | `main` ships the regression: compiler splits at 256, every row generator writes at 128, so overflowing token codes silently stop matching. Main's tests derive the width from the value the rule reads and cannot catch it. **Cherry-pick candidate, independent of Phase F.** |
| Package repository and importer straddle two tenants | `SqlServerTerminologyImporterFactory` | Repository registered at tenant 1, importer reads the same `dbo.PackageResource` rows at partition 0. They agree only because partition 0 inherits tenant 1's connection string. Give partition 0 its own database and the importer looks in the wrong one — and `PackageResourceId` is a per-database IDENTITY, so it could find a *different* row rather than none |
| `TokenNumberNumber` single-point components unsearchable | row generator vs lowering rule | Generator writes `SingleValue2/3` leaving `LowValue2/HighValue2` null; the lowering rule reads only Low/High. Pre-existing on both engines; the new composite test uses genuine ranges to route around it |
| `_sqlEfFactory` constructor parameter name is stale | `CompositeRepositoryFactory`, `CompositeSearchServiceFactory` | Autofac can bind by parameter name, so renaming is a public-signature change rather than a rename |
| `GivenAResourceWithHistory_WhenHardDeleteResourceAsyncCalled_ThenAllVersionsAndSearchIndexRowsAreGone` is misnamed | `SqlServerFhirRepositoryExpiryTests` | Checks `dbo.Resource` and `dbo.ResourceTtl` only. The 15-table sweep it claims is now a sibling test; fold or rename |
| `ResourceWriteClaimRowGenerator` yields no rows | `RowGenerators` | Documented Phase 1 stub, so `dbo.ResourceWriteClaim` is unreachable from the write path. The delete-sweep test inserts into it directly to cover the delete SQL |

---

## What would make this fail

Named plainly, because they are the things worth watching for:

- **Task 5b is skipped or rushed.** It produces no shippable code, which makes it the tempting one to cut.
  It is the only thing standing between a 2,645-line port and no way to know it is right.
- **Task 6's atomicity is assumed rather than mapped.** Nine boundaries, no transaction API, and the failure
  mode is silent partial terminology data rather than an exception.
- **Task 9 is treated as a formality.** It is the last point at which the rollback lever still exists.
- **The follow-ups register is not carried forward** into whatever tracks work after this phase.
