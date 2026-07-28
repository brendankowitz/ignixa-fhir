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

- [ ] Map every `SaveChangesAsync` call site to exactly one atomic unit, and implement each as a single
      batch with explicit `BEGIN TRANSACTION`/`COMMIT TRANSACTION` in the command text. A 200k-concept
      insert that fails halfway must not leave a half-populated CodeSystem with no record that it is partial.
- [ ] Keep the 1,000-concept threshold and both insert paths, so behaviour does not change at the boundary.
- [ ] **Add a volume test.** Real CodeSystems (SNOMED, LOINC) are orders of magnitude past the threshold;
      the bulk path's performance and memory profile are part of its contract, not an implementation detail.
      Record timing so a regression is visible.
- [ ] Repoint Task 5b's oracle tests. Assertions must not change.

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

### Task 9: Pre-deletion gate — *verification only*

**New, and non-negotiable.** Deletion is the one irreversible step; it removes the rollback lever for a
live, unflagged read cutover with 31 documented search gaps still open. Before Task 10 touches anything:

- [ ] `dotnet build All.sln` — 0/0.
- [ ] Every unit and integration suite at the Task R baselines.
- [ ] **E2E at exactly the Task R baseline, matching on failing test names**, not just counts.
- [ ] **A real application start** against a real tenant database, exercising terminology import and package
      load — the two areas whose ports have the least prior coverage. Phase B's ~10 missing tables were
      found by running the app, not by any test.
- [ ] Confirm nothing outside the EF project references `FhirDbContext` or the project. Any hit means an
      earlier task is incomplete: finish it rather than deleting around it.

If any of these fail, **stop**. Task 10 does not start.

---

### Task 10: Delete the EF project

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

---

## What would make this fail

Named plainly, because they are the things worth watching for:

- **Task 5b is skipped or rushed.** It produces no shippable code, which makes it the tempting one to cut.
  It is the only thing standing between a 2,645-line port and no way to know it is right.
- **Task 6's atomicity is assumed rather than mapped.** Nine boundaries, no transaction API, and the failure
  mode is silent partial terminology data rather than an exception.
- **Task 9 is treated as a formality.** It is the last point at which the rollback lever still exists.
- **The follow-ups register is not carried forward** into whatever tracks work after this phase.
