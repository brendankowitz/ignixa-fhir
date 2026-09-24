# Phase F — retire `Ignixa.DataLayer.SqlEntityFramework` — design

**Status:** ready for review
**Date:** 2026-07-26
**Branch:** `worktree-ignixa-datalayer-sqlserver`, tip `f07d4c1a`
**Roadmap:** Phase F of `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` — "Retire
`Ignixa.DataLayer.SqlEntityFramework` once A–E are all live and verified."

## Where this starts

Phases A–E are complete. Both the read and write paths already run on `Ignixa.DataLayer.SqlServer`,
unconditionally and without a feature flag — `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory`
constructs `SqlServerRepositoryFactory.CreateRepository` and `.CreateSearchService`, and both delegates
take a `FhirDbContext` and discard it. That discarded parameter is the shape of a finished cutover whose
scaffolding has not been removed.

What still runs on EF is not search and not CRUD. It is four feature areas — terminology, package
management, background jobs, and the source event store — plus the composition root itself, which lives in
the EF project and still builds `FhirDbContext` options.

## Goal

Delete `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework` outright, leaving exactly one data layer.

## Decisions already taken

Three, recorded here because they shape everything below and were made deliberately against stated
alternatives:

1. **Full deletion in this phase**, not a staged retirement that leaves the project inert on disk. The
   motivation is that maintaining two data layers means every future fix is reasoned about twice; one
   target is worth more than one rollback lever.
2. **The 50 differential tests are deleted with the engine**, not converted to golden snapshots. This
   drops the integration suite from 135 to 85 and removes the harness that found the untyped-reference
   collision bug and four pre-existing write-path bugs. See "The ordering constraint" — it is the direct
   consequence and the main risk this design manages.
3. **The storage type is renamed** from `SqlEntityFramework` to `SqlServer`, with the old string accepted
   as a deprecated synonym so deployed tenant configuration keeps working unchanged.

Two consequences follow that are worth stating plainly rather than discovering later. Deleting the legacy
search generators removes the rollback lever for a live, unflagged read cutover that shipped with 31
documented E2E gaps still open. And the compiler's own design gated that cutover on full parity with
"no fallback dispatch" — a gate that was consciously not met. Neither changes the decision; both are
reasons the acceptance criteria below are stricter than a pure deletion would otherwise need.

## The design's central fact: the port is interface-preserving

Every EF implementation being retired already sits behind an interface owned by a higher layer:

| EF implementation | Interface | Interface lives in |
|---|---|---|
| `SqlCodeSystemImporter` | `ITerminologyImporter` | `Ignixa.Domain` |
| `SqlTerminologyService` | `ITerminologyService` | `Ignixa.Validation` |
| `SqlSystemRepository` | `ISystemRepository` | `Ignixa.Domain` |
| `SqlPackageResourceRepository` | `IPackageResourceRepository` | `Ignixa.Domain` |
| `SqlBackgroundJobRepository<T>` | `IBackgroundJobRepository<T>` | `Ignixa.Domain` |
| `SqlSourceEventStore` | `ISourceEventStore` | `Ignixa.Conformance.Events` |

So no consumer changes. Each port is: implement the same interface in `Ignixa.DataLayer.SqlServer` over
`ISqlExecutionService`, swap one registration, delete the EF type. Consumers cannot tell the difference,
which is also what makes the ports testable against the existing suites rather than needing new ones.

`HybridTerminologyService` is the exception — it is a composition over `ITerminologyService`, not a data
access type, and moves unchanged apart from its namespace.

## This is not Phase D's shape

Phase D's design argued the write-path port was "lower risk than it sounds — the write path is already raw
SQL text execution, not LINQ; this swaps the connection/execution wrapper underneath, not the write logic
itself." **That reasoning does not transfer to Phase F**, and assuming it does is the most likely way this
phase goes wrong:

| Port target | Lines | LINQ constructs | Raw-SQL calls |
|---|---:|---:|---:|
| `SqlCodeSystemImporter` | 1,874 | 86 | 3 |
| `SqlPackageResourceRepository` | 943 | 58 | 0 |
| `SqlTerminologyService` | 771 | 46 | 0 |
| `SqlBackgroundJobRepository` | 250 | 6 | 0 |
| `SqlSystemRepository` | 126 | — | 0 |
| `SqlSourceEventStore` | 157 | 4 | 0 |

Terminology and package management together are roughly 3,700 lines of genuine LINQ-to-SQL rewrite, not a
wrapper swap. `SqlCodeSystemImporter` alone is the largest single file in the phase and carries bulk-import
semantics that EF currently hides: a `BulkInsertThreshold` of 1,000 concepts above which it already calls a
hand-written bulk path, and `AddRange` + `SaveChangesAsync` below it. The raw-ADO.NET port must make the
small-set path's batching explicit rather than inheriting EF's change-tracker behaviour.

## The ordering constraint

Because decision 2 deletes the differential harness, **the only window in which a reference implementation
exists is before the deletion commit.** This is a hard sequencing rule, not a preference:

1. Port each area while EF is still present and registered.
2. Verify each port against live EF behaviour in that window.
3. Delete only after every port is verified.

A port merged after the deletion has nothing to be checked against. Every task below that ports code
therefore carries its own verification step, and the deletion is the last task in the phase rather than the
first.

**Verification method per area**, given the interface-preserving design: the existing consumer-level tests
(terminology, package management, background jobs, conformance events) exercise these interfaces already.
Each port runs those suites against the new implementation and must match. Where an area's existing
coverage is thin, the port task writes the missing test **against the EF implementation first** — proving
the test passes on the old code — then repoints it. A test written only against the new implementation
proves nothing about equivalence.

## Scope

**Ported (≈3,900 lines rewritten):** the six implementations above, into
`src/DataLayer/Ignixa.DataLayer.SqlServer/Features/{Terminology,PackageManagement,BackgroundJobs}` and
`/EventStore`, mirroring the existing project layout.

**Relocated:** the composition root. `SqlEntityFrameworkRepositoryFactory` (497 lines) is replaced by a
SqlServer-owned factory. Three services currently demand it by concrete type and must be repointed:
`SqlReferenceDataPreloadService`, `TerminologyImportBootstrapService`, and
`ImportTerminologyResourceActivity` (which also takes a `FhirDbContext` parameter directly). The
`IDbContextFactory<FhirDbContext>` registration in `DataLayerRegistration.cs` goes with it.

**Renamed:** storage type `SqlEntityFramework` → `SqlServer`, old value accepted as a deprecated synonym.
Note `SqlExecutionService` already had a bug where it rejected the `SqlEntityFramework` synonym that every
real tenant config uses — the synonym path is load-bearing, not cosmetic.

**Deleted (≈10,600 lines):** the EF project entire — `Entities/` (1,748), `Search/` and its query
generators (6,614), `RowGenerators/` (2,260), `FhirDbContext`, `TvpSchemaProvider`, the EF
`PostMergeExtensionUpdater`, and `Compression/GzipResourceCompressor` (already duplicated in the SqlServer
project). Plus: the 50 differential facts in
`test/Ignixa.DataLayer.SqlServer.IntegrationTests/Differential/`, the two EF test projects
(`Ignixa.DataLayer.SqlEntityFramework.IntegrationTests`, and `Ignixa.DataLayer.LegacySqlEF.Tests` — the
latter already fails to compile and is not in `All.sln`), and the EF `ProjectReference` from
`Ignixa.Api.E2ETests`.

**Out of scope:** the 31 remaining E2E search gaps
(`docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md`, five groups / 29 failures plus two
architectural guards). Phase F must not change search behaviour at all — see acceptance.

## Ordering

Easiest first, so the raw-ADO.NET porting pattern is established on low-risk code before the large
rewrites, and every step leaves the branch green:

1. `SqlSourceEventStore` (157 lines, 4 LINQ) — smallest, establishes the pattern.
2. `SqlBackgroundJobRepository` (250, 6) — generic type, still simple.
3. `SqlSystemRepository` (126) — terminology's leaf dependency, needed before the two big terminology files.
4. `SqlPackageResourceRepository` (943, 58) — first genuine rewrite.
5. `SqlTerminologyService` (771, 46) — seven public operations, each independently verifiable.
6. `SqlCodeSystemImporter` (1,874, 86) — largest; bulk-import batching made explicit. Likely warrants
   splitting across more than one task at plan time.
7. `HybridTerminologyService` — namespace move only.
8. Composition-root relocation + the three services + storage-type rename.
9. **Deletion**, last: EF project, differential tests, EF test projects, E2E project reference.

## Acceptance

Phase F changes no behaviour. The bar is therefore equality, not improvement:

- `dotnet build All.sln` — 0 warnings, 0 errors.
- `Ignixa.Search.Sql.Tests` — **839/839** both TFMs, unchanged (Phase F touches no compiler code).
- `Ignixa.DataLayer.SqlServer.IntegrationTests` — **135/135 before** the deletion task, **85/85 after**,
  with the drop accounted for entirely by the 50 deleted differential facts and no other test lost.
- `Ignixa.Application.Tests` — 1125/0/1 skip, unchanged.
- **E2E — 620 total / 569 passed / 31 failed / 20 skipped, exactly.** Same counts *and the same failing
  test names* as
  `docs/superpowers/specs/2026-07-25-search-sql-gap-closure-design.md`'s recorded set. A different count in
  either direction is a Phase F regression until proven otherwise; a *lower* failure count is equally
  suspect, because nothing in this phase should fix a search gap.
- A live application start against a real tenant database, exercising terminology import and package load —
  the two areas whose ports have the least existing coverage. Phase B's history is the argument for this:
  its ~10 missing tables were found by running the real app, not by any test.

## Risks

- **`SqlCodeSystemImporter`'s bulk-import path.** 1,874 lines with EF change-tracking and two divergent
  insert strategies. The most likely place for a silent behavioural difference, and the least covered by
  existing tests.
- **The deletion is irreversible in effect.** After it, no reference implementation exists for any future
  question of "what did legacy do here?" — including for the 31 open search gaps, whose design document
  assumes the legacy engine is available for comparison. That document should be re-read and amended as
  part of the deletion task if it depends on the harness.
- **Terminology has the thinnest existing coverage** of any area being ported, and the largest surface.
  Expect the port tasks to write tests against EF first, per the verification method above.
- **`ImportTerminologyResourceActivity` takes `FhirDbContext` directly**, so it is the one consumer that
  does need a signature change rather than a registration swap.
