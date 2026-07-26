# Patient/$everything completion — results

Status of `Patient/{id}/$everything` on the compiled search path (`Ignixa.Search.Sql`) after the
2026-07-25 completion plan (`docs/superpowers/plans/2026-07-25-patient-everything-completion.md`,
tasks 1-6, commits `9c664902`..`a049d7aa`). Written for a reader who was not there.

## What `$everything` returns now, versus before

**Before this plan: nothing.** `PatientEverythingHandler` built a `SearchOptions` with
`ResourceType = null` and the comment `// Multi-resource type search`. `Lower.cs` rejects a null
`ResourceType` outright — `PatientEverythingExpression` could not compile through the compiled search
path at all. This was not a correctness bug in the traversal; the traversal was never reached.

**Now: it compiles, and returns a strictly larger, more correct set.** Task 1
(`src/Application/Ignixa.Application.Operations/Features/PatientEverything/PatientEverythingHandler.cs`,
commit `9c664902`) set `ResourceType = "Patient"` — the field names the *anchor* whose compartment is
expanded, not the set of types returned; the returned types come from the traversal itself
(`StructuralContext.LowerPatientEverything`). With that fixed, `$everything` returns:

```
{patient itself} ∪ {compartment members} ∪ {Practitioner/Organization/Location/Medication referenced
from (patient itself ∪ compartment members)}
```

The third term is new relative to what either Ignixa implementation returned before Task 2 (see below):
previously the outbound expansion was seeded from the compartment alone, so a patient's own
`generalPractitioner`/`managingOrganization` were missed unless some compartment resource happened to
reference the same target independently. That is now fixed — the patient's own references are included
for the first time on both implementations.

## The direction finding, and its spec citations

An earlier advisory questioned whether the compiler's inbound-compartment-plus-outbound-expansion
traversal over-returns relative to the legacy engine's captured SQL, which reads as outbound-only. It
does not, and the premise was wrong.

**The spec mandates both directions**, in identical wording across STU3 v3.0.2, R4, and R5 v5.0.0
(`operation-patient-everything.html`):

> "The server SHOULD return at least all resources that it has that are in the patient compartment for
> the identified patient(s), **and any resource referenced from those**, including binaries and
> attachments."

The Patient compartment is defined **inbound** — resources whose subject is the patient. Practitioner,
Organization, Location, and Medication are **explicitly never in the compartment**; they are reached
only by following outbound references from compartment members. An `$everything` that skipped the
compartment would return a patient record with no clinical content, non-compliant in every supported
FHIR version.

**The captured legacy SQL is not outbound-only by design — it is phase 1 of a phased operation.**
Verified directly against `legacy-sql-corpus.json` (lines ~3072/3135/3198 for the three captured
`$everything` entries): `@FilteredData` table variable, `IsMatch`/`IsPartial` columns, a
`TOP (@p) = 1001` include ceiling, a `Row < @p` window, the seed Patient as the sole `IsMatch = 1` row,
and exactly two outbound expansions on `SearchParamId 1012` and `1017` — almost certainly
`Patient.general-practitioner` and `Patient.organization`. This is Microsoft fhir-server's generic
`_include` machinery, not bespoke `$everything` SQL. Microsoft documents `$everything` as **phased**:
phase 1 is the patient plus `generalPractitioner`/`managingOrganization`, phases 2-3 are the patient
compartment (behind a continuation token), phase 4 is devices referencing the patient. The capture never
followed the continuation token, so it recorded phase 1 only — reading as "outbound only" because phase
1 genuinely is, not because the engine and compiler disagree about which direction to traverse.

This repo's own legacy EF generator (`PatientEverythingQueryGenerator`) already does both directions in
one query — step 2 is the inbound compartment via `CompartmentSearchQueryGenerator`, step 5 the outbound
expansion over the same four fixed types, step 6 the union. The compiler's `LowerPatientEverything` is a
structural transliteration of it, not a divergent design.

**Safety of the broader traversal**: the outbound expansion is a fixed four-type allowlist of
patient-agnostic master data, so it cannot structurally reach another patient's clinical resources.
`PatientEverythingExpression` also routes through `AccessConstraintApplier.ApplyToTypes` (`Lower.cs`),
constraining every arm of the union including the expansion — enforcement the legacy engine's capture
had no equivalent for.

No traversal change was made as a result of this finding. What changed is the record
(`DivergenceBaseline`, see below) and the seed (Task 2).

## The seed bug, and its scope

`StructuralContext.LowerPatientEverything` seeded the outbound expansion
(`ReferencedTypeExpansionRef`) from the compartment branch only:

```csharp
unionParts.Add(ReferencedTypeExpansionRef(compartmentRef, ResolveReferencedTypeIds()));
```

`ReferencedTypeExpansionRef` joins `dbo.ReferenceSearchParam` outward from the seed's own rows. The
seed patient is **not a member of its own compartment** — compartment membership is established by rows
that point *at* the patient, never by a self-referencing row. So the patient's own
`generalPractitioner`/`managingOrganization` were structurally unreachable from that seed unless some
compartment member happened to reference the same target independently.

**This bug existed in both Ignixa implementations.** The legacy EF generator's
`GetReferencedResourceIdsAsync(compartmentResourceIds)` has the identical shape — seeded from compartment
membership only. It is not a regression introduced by the compiler; it is a shared defect the advisory
surfaced, present since before this plan.

**Fix** (Task 2, commit `5df9e873`): seed from `Union([patientItselfRef, compartmentRef])` instead. A
regression test (`GivenAPatientEverythingSearchIncludingReferencedResources_...` in
`test/Ignixa.Search.Sql.Tests/Lowering/PatientEverythingLoweringTests.cs`) isolates the case a
compartment-seeded fix would still pass: a symbol table whose only compartment-member type
(Observation) never carries a `generalPractitioner`/`managingOrganization` reference, so the expansion's
seed must reach the patient-itself CTE directly, not through the compartment.

**Say this plainly rather than let it read as an accident**: fixing this in the compiler and not in the
legacy EF path means the two now differ in behaviour — correctly, in the compiler's favour. The compiler
returns strictly more (and correct) resources for this case than the legacy path does today.

## The five recorded decisions (Task 5), and where each lives in code

All five are recorded as decisions in the code at the site where the decision is made, not left as
emergent or silent behaviour:

1. **`search.mode` on expansion rows** — decided to keep them as `match`, not `include`.
   `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, `LowerPatientEverything`'s `<remarks>`,
   under *"Decision -- expansion rows are matches, not includes."* Reasoning: `$everything` carries no
   `_include`, so `match` is the spec-accurate code; this repo's own legacy EF generator already hydrates
   every row as `match` (the `IsMatch = 0` behaviour belongs to Microsoft fhir-server's captured SQL, not
   this repo's legacy path — a correction to the original brief); and the include-stage machinery
   truncates at `Limit`, which would be a worse failure mode for an operation named "everything." The
   migration path to `include` mode is recorded alongside it (a null-`ReferenceSearchParamId`
   `IncludeStage` with `SeedFromMatch = true`) for whenever a compiled search service exists to consume
   it.

2. **`_type` × expansion** — implemented, as an intersection. `StructuralContext.ResolveReferencedTypeIds`
   intersects the expansion's fixed four-type output with `expression.FilteredResourceTypes`, dropping
   the expansion arm entirely when the intersection is empty. `PatientEverythingHandler.cs`'s coarser
   guard (any `_type` at all suppresses the expansion) is kept alongside it, for the legacy EF path, with
   the reason now recorded at the assignment: the legacy expansion applies no type filter of its own, so
   the handler's guard is the only thing standing between `$everything?_type=Encounter` and a bundle full
   of Practitioners on that path. The two engines therefore now encode deliberately different `_type`
   rules (see "what remains open" below).

3. **Device** — verified, not assumed, and recorded as a known gap. All five generated compartment
   definitions (`src/Core/Ignixa.Search/Generated/`, STU3/R4/R4B/R5/R6) list `Device` in the Patient
   compartment with an **empty** parameter list, so no compartment traversal can ever return one; R5/R6
   reach devices only indirectly through `DeviceAssociation`/`DeviceUsage`, not the Device itself. This is
   a gap in the spec's own `CompartmentDefinition` (which is why Microsoft's engine patches it with a
   bespoke phase 4), not something closable in `LowerPatientEverything` — closing it needs a
   version-conditional `Device.patient` symbol (present STU3/R4/R4B, absent R5+) that
   `SymbolCollectingVisitor` must request and tolerate the absence of. Recorded at
   `LowerPatientEverything`'s `<remarks>` as a known gap, scoped as follow-up.

4. **Patient `link`** — explicit non-goal. Recorded in `PatientEverythingHandler.cs`'s `<remarks>`.
   Neither following `seealso` one layer deep nor answering `replaced-by` with a 301/OperationOutcome
   under `Prefer: handling=strict` is implemented. Reasoning: `seealso` following turns a single-patient
   operation into a data-bounded (not cardinality-bounded) graph walk, and the `replaced-by` redirect is
   an HTTP-status decision that belongs at the endpoint, not the handler.

5. **`_since` scope** — a stated decision, not emergent behaviour. Recorded at
   `LowerPatientEverything`'s `<remarks>` under *"Decision -- `_since` narrows the seed, not the expansion
   output."* `_since` is intersected into the compartment set before it seeds the expansion; the
   expansion's own output carries no visibility bound. Consequence stated explicitly: a Practitioner whose
   only referencing compartment rows all predate the cutoff disappears from an incremental pull even if
   the Practitioner itself changed after it. This matches the legacy EF generator exactly. Kept because
   the alternative (expand from the unfiltered compartment, then filter) would make an incremental pull
   traverse the whole compartment — the cost `_since` exists to avoid.

A sixth item, R5 Provenance/AuditTrail, is also an explicit non-goal, recorded in
`PatientEverythingHandler.cs`'s `<remarks>`: both types are target-referencing, so satisfying the R5
SHOULD is a reverse traversal over the whole result set, a different query shape rather than another
member type.

## Verification measured on this run

- `dotnet build All.sln`: **0 Warning(s), 0 Error(s)**.
- `Ignixa.Search.Sql.Tests`: **812 passed, 0 failed** on both net9.0 and net10.0.
- `Ignixa.Application.Tests` (net10.0, where the handler lives): **1052 passed, 0 failed, 1 skipped**
  (the skip is a pre-existing, unrelated timeout test).

## Corpus verdict distribution — final state

Measured from a fresh `Ignixa.Search.Sql.Tests` run (`legacy-sql-differential-report.md`), 185 captured
searches:

| Verdict | Count |
|---|---:|
| NotCompiled | 0 |
| Match | 75 |
| CompilerDoesLess | 37 |
| CompilerDoesMore | 14 |
| Divergent | 59 |

Unchanged from the plan's baseline across every task in this plan (Tasks 2, 3, 4, 5 each measured and
reported no drift; this run reconfirms it). `DivergenceBaseline`'s four constants
(`test/Ignixa.Search.Sql.Tests/Corpus/DivergenceBaseline.cs`) match exactly.

### The three `$everything` entries still `Divergent`, and why

All three captured `$everything` searches remain `Divergent`. Per-entry, measured from this run's
report:

**`/Patient/ignixa-evx-pat/$everything?_count=100&foo=bar`** and
**`/Patient/ignixa-evx-pat/$everything?_since=3000`** (same shape): legacy-only holds `table Resource`,
`filter IsDeleted`, `filter IsHistory`, and `filter Row < <v>` (x2) — its phased include-window paging.
Compiler-only holds `table ReferenceSearchParam` (x74) plus its associated filters — the Patient
compartment's membership-parameter reads. This asymmetry is Task 4's finding, not a shape mismatch: the
capture covers phase 1 of Microsoft's phased operation only (patient + `generalPractitioner` +
`managingOrganization`); phases 2-3, which *are* the compartment traversal, were never captured because
the capture never followed the continuation token. No change on this branch can close this — it requires
capturing phases 2-4, not a compiler fix. `ShapeComparison.Decide` verdicts on the tables/filters
multiset alone; paging operators (`TOP`, `OFFSET/FETCH`, `ORDER BY`) are reported but never decisive, and
the only paging artefact that reaches the verdict is legacy's `Row < <v>` window, which no compiler-side
keyset seek can cancel (the seek predicate canonicalizes as a different filter, so implementing paging
*adds* to the compiler-only side rather than closing the gap).

**`/Patient/ignixa-evx-pat/$everything?_type=foo`**: legacy-only holds `table ReferenceSearchParam` (x2),
`table Resource`, `filter IsDeleted`, `filter IsHistory`, `filter Row < <v>` (x2), and
`filter SearchParamId` (x2) — legacy still expands its two fixed outbound reference parameters as two
reads regardless of `_type`, because its expansion applies no type filter of its own. Compiler-only holds
just `filter <v> = <v>` (the compartment's unsatisfiable-predicate collapse for an unrecognized type) and
`filter ResourceTypeId`. This is Task 5's `_type`-intersection fix working as intended — since
`ResolveReferencedTypeIds` now intersects the expansion's four fixed types against `_type=foo` (which
resolves to nothing), the compiler emits **zero** `ReferenceSearchParam` reads for this entry, down from
one before that fix. The verdict stays `Divergent` because legacy's un-narrowed reads remain on its own
side; the diff widened, but in the compiler's favour (a caller who excluded all four types no longer
receives them from the compiler, where before this plan's Task 5 it still did) — `DivergenceBaseline`
records this explicitly as "the compiler doing less, correctly," not a regression.

## What remains open

- **Device** is present-but-inert in all five compartment definitions (STU3 through R6) — listed as a
  compartment member type with zero linking parameters, so no traversal can ever return one. Needs a
  version-conditional `Device.patient` symbol (STU3/R4/R4B only) at the resolve stage. Scoped as
  follow-up, not implemented here.
- **`search.mode` deferral has a consequence that must be actively closed later, not just noted.** The
  moment a compiled search service exists and serves `$everything`, the compiler's bundle will have
  different `search.mode` values and a different page partition than Microsoft fhir-server's phased
  output for the same operation. The decision comment names the trigger (a compiled search service
  existing); nothing currently acts on it automatically.
- **The handler and the compiler now encode deliberately different `_type` rules.** The legacy-path
  handler's guard is coarse (any `_type` suppresses the whole expansion, so `_type=Practitioner` returns
  no referenced practitioners even though one was explicitly asked for); the compiler's intersection is
  precise (it returns exactly the requested referenced type). This is correct on the compiler side and
  intentional, but it means `$everything?_type=Practitioner` returns a different bundle depending on
  which engine serves it — a divergence to expect in row-level comparison, not a bug to chase.
- **`IpsGeneratorService` currently relies on the pre-fix expansion behaviour.** It constructs
  `PatientEverythingExpression` with a `_type` set and `includeReferencedResources = true` unconditionally
  (bypassing the handler's guard), so before Task 5's fix it received
  Practitioner/Organization/Location/Medication rows outside its requested IPS section types on any path
  that honored the intersection. It runs on the legacy EF path today, where `GetReferencedResourceIdsAsync`
  also ignores `_type`, so its behaviour is unchanged for now — but on the compiled path it will get only
  what it asked for, and if any IPS section silently depended on those extra rows, that surfaces at
  migration. Not investigated further; flagged rather than left silent.
- **`_since` is coupled to branch A's uncommitted-transaction defect.** `SqlServerFhirRepository.CreateOrUpdateAsync`
  (on branch A only — `Ignixa.DataLayer.SqlServer` does not exist on this branch) opens a
  `dbo.Transactions` row and never commits it, leaving `VisibleDate` NULL, so `_since` matches nothing on
  that write path today. The compiler deliberately kept legacy's `_since` semantics
  (`Transactions.VisibleDate`, not a `lastUpdated` surrogate floor) because they match legacy — those
  semantics are only production-correct once that defect is fixed. Not changed here to make anything pass;
  filed with branch A in the handoff document (below).

## What is genuinely unproven here

**This branch has no compiled search service.** `SqlServerCompiledSearchService` exists only on branch A
(`worktree-ignixa-datalayer-sqlserver`); `src/DataLayer/` on this branch holds only BlobStorage,
FileSystem, InMemoryIndex, and SqlEntityFramework. Production `ISearchService` here resolves to the
legacy EF path; `Ignixa.Search.Sql` is reachable only through `SearchCompiler` and its own test suite.

**Nothing in this plan has executed against a database.** Every claim above — the anchor fix, the seed
fix, the `_type` intersection, the paging model, the five decisions — is verified at the compiler level:
lowering shape, emitted SQL text, and the corpus differential comparison against captured legacy SQL. No
`$everything` request has been run end-to-end, and per this task's explicit constraint the E2E suite was
not run in this task — running it here would exercise the legacy EF path only and prove nothing about the
compiled traversal these six tasks changed.

The branch-A handoff document
(`docs/superpowers/specs/2026-07-25-patient-everything-branch-a-handoff.md`, written in Task 3) names
precisely what must run once branch A rebases onto this branch: a new `$everything` E2E test seeded to
exercise Task 2's seed-union fix specifically (compartment members that do *not* independently reference
the patient's `generalPractitioner`/`managingOrganization`, so the fix stays falsifiable rather than
accidentally masked the same way the original bug was); a new `_since` E2E test run through the real
write path with no manual patching, expected to **fail** against branch A's uncommitted-transaction
defect (a passing result there would itself be evidence something else is wrong); and the three
`.cs.txt` gate-test files (six `[Fact]`s total) preserved verbatim at
`docs/superpowers/specs/2026-07-25-unified-execution-gate-tests/`, all of which target types that live
only in `Ignixa.DataLayer.SqlServer` and so cannot compile or run here.

Do not read the compiler-level verification above as end-to-end validation. It is not.

## Addendum, 2026-07-26: what execution found

Branch A rebased onto this work and ran it against a database. The caveat above held, and it mattered:
the first execution found a defect that every layer of verification recorded in this document had missed.

**`$everything?_type=X` threw `RequestNotValidException: SymbolTable has no ResourceTypeId for
'Practitioner'`.** `SymbolCollectingVisitor.VisitPatientEverything` collects the four expansion types only
when `IncludeReferencedResources` is set, and `LowerPatientEverything` originally resolved them under the
same condition. The `_type` intersection recorded above as Task 5's fix hoisted that resolve *out* of the
guard, so it could use `expansionTypeIds.Count` in the condition — which left the lowerer resolving
symbols the collector deliberately never gathered. The handler sets the flag false exactly when `_type` is
present (`includeReferencedResources = request.Types is null or { Count: 0 }`), so the broken shape is the
one a caller reaches by filtering. Fixed by moving the resolve back inside the guard, keeping the
intersection.

**Why nothing here caught it.** `PatientEverythingLoweringTests.BuildSymbols` registers all four expansion
types unconditionally, which made the missing-symbol case unreachable from any lowering test; and the
corpus verdicts key off the tables/filters multiset, not symbol resolution. Both facts are visible in this
document's own verification section, and neither was recognised as a coverage hole until execution
produced the exception. A green suite and zero corpus drift were consistent with a broken operation.

**What execution confirmed.** The seed fix (Task 2) is proven at row level: a Practitioner and an
Organization referenced by the patient row and by nothing else are returned, which the pre-fix traversal —
seeded from the compartment alone — could not have done. `_since` over the ordinary write path returns no
compartment member, confirming the `CreateOrUpdateAsync` transaction-commit defect is real and executable
rather than inferred from reading.

`Ignixa.DataLayer.SqlServer.IntegrationTests`: **135 passed, 0 failed** (126 pre-existing, the 6 gate
tests adopted, 3 added for the paths above).

**Two numbers in this document are wrong.** `Ignixa.Search.Sql.Tests` is reported above as 812; main's
baseline after the post-merge fixes landed with the #365 squash is **817**, and 818 with the regression
test added by the fix. `Ignixa.Application.Tests` is reported as 1052; it measures **1125** on branch A
after the merge. The corpus distribution (75/37/14/59) is unchanged and still accurate.
