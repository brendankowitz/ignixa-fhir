# Patient/$everything completion — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Patient/{id}/$everything` work end to end on the compiled search path — it currently cannot execute at all — fix a real under-return bug in its expansion, and settle what it returns.

**Architecture:** The traversal is already correct and stays. What is missing is a caller that can reach it, a seed fix, paging, test coverage, and an accurate record of why it differs from the captured legacy SQL.

**Tech Stack:** C# / .NET 10, `Ignixa.Search.Sql` (multi-targets net9.0/net10.0), xUnit + Shouldly, SQL Server 2025 for execution.

## The direction question is settled — keep both directions

An advisory investigated whether the compiler's inbound-compartment + outbound-expansion traversal over-returns relative to the legacy engine's apparently outbound-only capture. **It does not, and the premise was wrong.**

**The spec mandates both.** Identical wording in STU3 v3.0.2, R4 and R5 v5.0.0 (`operation-patient-everything.html`):

> "The server SHOULD return at least all resources that it has that are in the patient compartment for the identified patient(s), **and any resource referenced from those**, including binaries and attachments."

The Patient compartment is defined **inbound** — "any resources where the subject of the resource is the patient." Practitioner, Organization, Location and Medication are **explicitly never in the compartment**; they are master data reached outbound. An `$everything` without the compartment returns a patient record with no clinical content, non-compliant in all four supported versions.

**The legacy engine is not outbound-only.** The captured corpus SQL is unmistakably Microsoft fhir-server's `_include` machinery — `@FilteredData` table variable, `IsMatch`/`IsPartial` columns, `TOP (@p) = 1001` include ceiling — with the seed Patient as the only match and two outbound expansions. That is **phase 1 of a phased operation**: Microsoft documents phase 1 as patient + `generalPractitioner` + `managingOrganization`, phases 2–3 as the patient compartment, phase 4 as devices referencing the patient. The capture never followed the continuation token, so it recorded one phase. SearchParamIds 1012 and 1017 are almost certainly `Patient.general-practitioner` and `Patient.organization`.

**This repo's own legacy EF generator does both directions in one query** — `PatientEverythingQueryGenerator` step 2 is the inbound compartment via `CompartmentSearchQueryGenerator`, step 5 the outbound expansion over four fixed types, step 6 the union. `LowerPatientEverything` is a structural transliteration of it.

**Safety.** Over-return is bounded by construction: the outbound expansion is a **fixed four-type allowlist of patient-agnostic master data**, so it structurally cannot reach another patient's clinical resources. An unrestricted follow-all-references expansion would be a real disclosure surface; this is not. Further, the compiler routes `PatientEverythingExpression` through `AccessConstraintApplier.ApplyToTypes`, so expansion output is per-type constrained — the legacy path had no such enforcement. **The compiler's broader traversal is better guarded than the legacy capture's narrower one.**

**So no traversal change. What changes is the record** (Task 2) and the seed (also Task 2 — the advisory found a real bug).

## Global Constraints

- **Environment:** unset `Platform`, `__DOTNET_PREFERRED_BITNESS`, `__DOTNET_ADD_32BIT` before any `dotnet` command (known CS8034 x86-bitness artifact here, not a real failure).
- `dotnet build All.sln` must be **0 warnings, 0 errors**. Warnings are errors.
- **Baselines:** `Ignixa.Search.Sql.Tests` **802 passed / 0 failed** on both net9.0 and net10.0. A bare `dotnet test All.sln` fails for unrelated environmental reasons (uninitialized submodule content, missing conformance-suites directory, projects needing `TEST_SQL_CONNECTION_STRING`, E2E needing a live environment, a TFM-parallelism file-lock race) — read actual error text before assuming a failure is yours.
- **E2E environment:** `TEST_SQL_CONNECTION_STRING` must name a **brand-new database** — a stale one with schema drift silently produces ~590 bogus failures. Set `SqlServer__AutomaticSchemaDeploymentEnabled=true`. Use the machine's local **SQL Server 2025**, not Docker (the 2022 image cannot deploy a `Sql170` DACPAC). Drop scratch databases afterwards.
- **Corpus: measure, never assume.** All three captured `$everything` entries currently sit as `Divergent`. If a task moves the distribution, update `DivergenceBaseline` and state why; if not, say so. **Never manufacture drift to match an expectation written here** — a previous plan predicted drift that did not occur, twice.
- **Non-vacuity is mandatory** for any test guarding a forwarding or filtering behaviour: write it, delete the code it guards, confirm failure, restore, confirm pass, report both. This caught a genuinely vacuous test in this codebase within the last day.
- Cross-field guards use `NotSupportedException`, matching existing convention.
- No inline comments except non-obvious invariants. Test naming `GivenContext_WhenAction_ThenResult`, AAA with Shouldly, no `#region`.
- `CteRef` is `public readonly record struct CteRef(int Index)`.
- `StartsWith`/`Contains` need an explicit `StringComparison` (CA1310 enforced). An `out` discard cannot appear in an expression tree (CS8198) — use `Count(...).ShouldBe(0)`.

## Out of scope — and where it belongs

**The `_since` / uncommitted-transaction defect stays with branch A.** `SqlServerFhirRepository.CreateOrUpdateAsync` opens a `dbo.Transactions` row and never commits it, so `VisibleDate` stays NULL and `_since` matches nothing on that write path. `Ignixa.DataLayer.SqlServer` does not exist on this branch.

This is **coupled** to `$everything` and must be filed with it: the compiler deliberately kept branch A's `_since` semantics (`Transactions.VisibleDate` rather than a `lastUpdated` surrogate floor) because they match legacy. **Those semantics are only production-correct once that defect is fixed.** Do not "fix" the semantics here to make a test pass.

---

### Task 1: Fix the anchor so `$everything` can compile at all

**Files:**
- Modify: `src/Application/Ignixa.Application.Operations/Features/PatientEverything/PatientEverythingHandler.cs:69`
- Test: `test/Ignixa.Application.Tests/` (locate the existing `$everything` handler tests; create alongside siblings if absent)

**Interfaces:**
- Produces: a `SearchOptions` whose `ResourceType` is the anchor type, so `Lower` dispatches `PatientEverythingExpression` rather than throwing.

This gates every other task. Nothing downstream can be tested until it lands.

The handler sets `ResourceType = null` with the comment `// Multi-resource type search`, and `Lower.cs:253` rejects exactly that. **The guard is right and the handler is wrong:** `SearchOptions.ResourceType` names the *anchor* type whose compartment is expanded, not the set of returned types — the traversal produces those. The comment conflates the two, and the execution gate confirmed `ResourceType = "Patient"` works and returns correct multi-type results.

- [ ] **Step 1: Read `PatientEverythingHandler.cs:69` and `Lower.cs:245-260`.** Satisfy yourself the anchor reading is right. If the code says otherwise, stop and report — the task rests on it.
- [ ] **Step 2: Write the failing test** asserting the built `SearchOptions.ResourceType` is the anchor, not null. Use the handler's real construction shape and this project's handler-test conventions. If no handler test exists, note that absence in your report.
- [ ] **Step 3: Run — expect failure** with `ResourceType` null.
- [ ] **Step 4: Set the anchor and replace the comment.** The comment caused this defect; leaving it invites the fix being reverted. It should say the field names the anchor type whose compartment is expanded, and that many types are returned via the traversal rather than via a null anchor.

Check whether the handler also serves `Group/{id}/$everything`. If so the anchor is the requested resource's type, not a hardcoded `"Patient"` — handle and test that.

- [ ] **Step 5: Run — expect pass. Build `All.sln` — 0/0. Commit.**

---

### Task 2: Fix the expansion seed, and correct the divergence record

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (`LowerPatientEverything`, ~line 443ff)
- Modify: `test/Ignixa.Search.Sql.Tests/Corpus/DivergenceBaseline.cs`

**A real under-return bug, present in both implementations.** `ReferencedTypeExpansionRef(compartmentRef, …)` seeds the outbound expansion **only** from the compartment branch. The seed patient is **not a member of its own compartment** — no reference row points from the patient at itself. So the patient's own `generalPractitioner` and `managingOrganization` are **missed** unless some compartment resource happens to reference them independently.

Those are precisely the two resources the captured legacy phase 1 exists to return. The legacy EF generator has the identical bug (`GetReferencedResourceIdsAsync(compartmentResourceIds)`), so this is not a regression introduced by the unification — it is a shared defect the advisory surfaced.

**Fix:** seed the expansion from `Union(patientItselfRef, filteredCompartmentRef)`.

- [ ] **Step 1: Read `LowerPatientEverything`** and confirm the seed currently excludes `patientItselfRef`.
- [ ] **Step 2: Write the failing test** — a patient with a `generalPractitioner` and `managingOrganization` and **no compartment members referencing them** must still yield those two in the expansion. That isolation is the point: with compartment members referencing them, the bug is invisible.
- [ ] **Step 3: Run — expect failure** (the expansion is empty or missing those two).
- [ ] **Step 4: Change the seed to the union. Run — expect pass.**
- [ ] **Step 5: Correct `DivergenceBaseline`'s account — its second correction.**

Its doc comment currently says the divergence is "opposite graph directions." That is wrong. The captured SQL is **phase 1 of Microsoft's phased `$everything`** (patient + `generalPractitioner` + `managingOrganization`; compartment arrives in phases 2–3 behind a continuation token the capture never followed). Evidence: `@FilteredData` table variable, `IsMatch`/`IsPartial` columns, `TOP (@p) = 1001` include ceiling, seed Patient as sole match, two outbound expansions on SearchParamIds 1012/1017 — almost certainly `Patient.general-practitioner` and `Patient.organization`.

Follow the file's own stated convention: record the changed reason rather than adjusting the count. **The remaining divergence is the paging model** — phased continuation versus single windowed query — which is Task 4, not a semantics gap.

- [ ] **Step 6: Measure corpus drift**, report either way. Build, commit.

---

### Task 3: Establish the coverage this branch can actually carry

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/` — compiler-level coverage
- Create: `docs/superpowers/specs/2026-07-25-patient-everything-branch-a-handoff.md`

**Interfaces:**
- Consumes: Task 1's anchor fix and Task 2's seed fix.

**Read this before writing anything — the plan was wrong about what is testable here.**

Task 1 established that **this branch has no compiled search service at all.** `SqlServerCompiledSearchService` exists only on branch A (`worktree-ignixa-datalayer-sqlserver`); `src/DataLayer/` here holds BlobStorage, FileSystem, InMemoryIndex and SqlEntityFramework only. Production `ISearchService` on this branch resolves to the **legacy EF** implementation, and `Ignixa.Search.Sql` is reachable only through `SearchCompiler` and tests.

So an `$everything` E2E test written here would exercise the **legacy EF path**, not the compiled traversal Tasks 1 and 2 fixed. That is worse than no coverage — it would look like validation and prove nothing about the code under change. The earlier draft of this task called for exactly that; it was wrong.

**The honest ceiling on this branch is compiler-level.** Real end-to-end validation belongs to branch A after it rebases, because A is where the compiled service is registered.

- [ ] **Step 1: Write compiler-level `$everything` coverage** in `Ignixa.Search.Sql.Tests` — lowering shape and emitted SQL. This is genuinely valuable and currently thin: assert the compartment traversal, the outbound expansion (including Task 2's union seed), the conditional date filter, `_since`, and that `AccessConstraint` reaches every row-producing stage. Pin the `Explain()` output so a future change to the traversal is visible in a diff.

- [ ] **Step 2: Inventory the six preserved gate tests** at `docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/`. They sit as `.cs.txt` files compiled by nothing because they target `Ignixa.DataLayer.SqlServer`. Determine which are adoptable here (if any) and which are branch A's at rebase. Record the split; do not force one into this branch by stubbing out its data layer.

- [ ] **Step 3: Write the branch-A handoff document.** It must state precisely what A should run once it rebases onto this branch, and why each item cannot run here:
  - `$everything` E2E: seed a patient with compartment members of several types **plus** a `generalPractitioner` and `managingOrganization` not otherwise referenced (Task 2's isolation case — with a compartment member referencing them, the seed bug stays invisible). Assert the bundle equals `{patient} ∪ compartment ∪ {referenced Practitioner/Organization/Location/Medication}`.
  - `_since` E2E: **expected to fail** against A's uncommitted-transaction defect, which leaves `VisibleDate` NULL. That failure is evidence for the follow-up, not something to work around — and a *passing* `_since` test would mean something else is wrong.
  - The six gate tests from Step 2 that belong to A.

- [ ] **Step 4: Run the compiler suite**, both TFMs. Measure corpus drift and report either way.

- [ ] **Step 5: Commit.**

---

### Task 4: Paging model

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, `src/Core/Ignixa.Search.Sql/Builders/SqlBuilder.cs`

The one gap PR #365 listed for its own `$everything` that the unification did not close, and — after Task 2's correction — **the sole remaining reason the three corpus entries diverge.**

The legacy engine pages *phased*: distinct query shapes per phase behind a continuation token. The compiler emits a single unwindowed query. These are different models, not different window sizes.

- [ ] **Step 1: Read the captured legacy paging shape** in the corpus and `DivergenceBaseline`'s corrected account.
- [ ] **Step 2: Establish whether existing machinery suffices.** The compiler has keyset `PageSpec` and `OffsetSpec` with OFFSET/FETCH emission. **Determine whether `$everything` can page through those before designing anything** — this may be wiring rather than new capability.
- [ ] **Step 3: The model is decided — single windowed query.** Not phased.

The phases are an implementation detail of how Microsoft assembles the result set, not a difference in what the operation returns: per Task 2's spec analysis, phase 1 plus phases 2-3 plus phase 4 is the same set a single union produces. A single windowed query over that union is one round trip instead of four and needs no phase concept, which the compiler does not have.

**Record this decision and its reasoning in the code at `LowerPatientEverything`**, including what was rejected and why — a future reader comparing against Microsoft's documented phasing will otherwise assume the difference is an oversight.

Trade-off accepted knowingly: phased paging bounds memory per phase for very large compartments. A single windowed query relies on the window to do that instead. If that proves insufficient in practice, phasing remains available later — this decision is reversible.
- [ ] **Step 4: Write failing tests. Implement.**
- [ ] **Step 5: Measure corpus drift** — this task is the genuine candidate to move the distribution. Report what you measure.
- [ ] **Step 6: Build, commit.**

---

### Task 5: Scope decisions and stated non-goals

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs` (doc comments at `LowerPatientEverything`)
- Modify: `src/Application/Ignixa.Application.Operations/Features/PatientEverything/PatientEverythingHandler.cs`

Four behaviours the advisory identified that neither implementation handles. Each needs a **recorded decision** — implement or explicit non-goal — not silence. Silence is what let the anchor defect survive.

- [ ] **Step 1: `_type` × expansion.** `$everything?_type=Encounter` should return only Encounters, but the expansion still emits its four fixed types whenever referenced-resource inclusion is on. Decide: the handler clears the flag when `_type` excludes those types, or the expansion output intersects `FilteredResourceTypes`. Implement and test.
- [ ] **Step 2: Device.** Microsoft has a dedicated phase 4 for devices referencing the patient, because Device is **not** in the R4 patient compartment. **Verify whether Ignixa's per-version compartment definitions include Device.** If not, `$everything` silently drops a clinically significant type — implement or record as a known gap with rationale.
- [ ] **Step 3: Patient `link`.** Microsoft follows `seealso` one layer deep and returns an `OperationOutcome`/301 for `replaced-by` under `Prefer: handling=strict`. Neither Ignixa path does any of it. Record as an explicit non-goal unless you implement it.
- [ ] **Step 4: `_since` scope.** Referenced resources are never `_since`-filtered, but their *seed* is — so a Practitioner disappears from an incremental pull when all its referencing compartment rows predate `_since`. This matches legacy EF. Make it a **stated** decision in the code rather than emergent behaviour.
- [ ] **Step 5: R5 Provenance/AuditTrail.** R5 adds "servers should consider returning appropriate Provenance and AuditTrail." Record as a non-goal.
- [ ] **Step 6: Build, run, commit.**

---

### Task 6: Verification and re-baseline

**Files:**
- Create: `docs/superpowers/specs/2026-07-25-patient-everything-results.md`

- [ ] **Step 1: Full compiler suite**, both TFMs.
- [ ] **Step 2: Corpus verdict distribution** — final state, and for each `$everything` entry still `Divergent`, the remaining reason.

**No E2E run in this task.** Per Task 3, this branch has no compiled search service — an E2E run here exercises the legacy EF path and says nothing about the compiled `$everything`. End-to-end validation is branch A's, against the handoff document Task 3 produces. Recording that as a deliberate boundary rather than an omission.
- [ ] **Step 4: Write the results document** — what `$everything` now returns versus before, the direction finding and its spec citations, the seed bug and its scope (both implementations), coverage added, decisions recorded in Task 5, and what remains open.
- [ ] **Step 5: Commit.**

---

## Notes for whoever executes this

- **Task 1 gates everything.** Nothing else is testable until `$everything` compiles.
- **Task 2's seed bug exists in the legacy EF generator too.** Fixing it here means the compiler and legacy diverge in behaviour — correctly, in the compiler's favour. Say so when it lands; do not let it read as an accidental difference.
- **The `_since` semantics here are deliberate** (legacy's `Transactions.VisibleDate`, not a `lastUpdated` floor) and are only production-correct once branch A's uncommitted-transaction defect is fixed. Do not change them to make a test pass.
- **A reviewer verifying this later** should seed a database with a patient, compartment resources and referenced master data, then assert the bundle equals `{patient} ∪ {GP, managingOrg} ∪ compartment ∪ {referenced master data}` — or diff against a dockerized Microsoft FHIR Server on identical data, comparing resource-id sets across all continuation pages while ignoring order and phasing.
