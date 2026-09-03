# Ignixa.DataLayer.SqlServer Phase D: Write-Path Migration — Design

**Builds on:** `worktree-ignixa-datalayer-sqlserver` at Phase C's completion (commit `a83297de`), standalone from `feature/fhir-to-sql-compiler`. Phases A (connection/execution layer), B (SQL Database Projects schema), and C (schema-version compatibility layer) are complete, reviewed, and pushed.

**Scope of this document:** the architecture for Phase D of the six-phase roadmap in `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` §3 — porting `Ignixa.DataLayer.SqlEntityFramework`'s write path off EF Core onto `Ignixa.DataLayer.SqlServer`'s `ISqlExecutionService`. This document corrects that original roadmap entry, which materially understated the work.

---

## 1. Ground truth (research findings, not assumptions)

**The original roadmap's framing of Phase D was wrong.** It read: "Port `SqlMergeRepository`'s TVP-based bulk writes from EF's `Database.ExecuteSqlRawAsync` to `ISqlExecutionService` directly. Lower risk than it sounds — the write path is already raw SQL text execution, not LINQ; this swaps the connection/execution wrapper underneath, not the write logic itself." That is true of `SqlMergeRepository.cs` in isolation — every real write in that file is `_context.Database.ExecuteSqlRawAsync("EXEC dbo.XXX ...", parameters, cancellationToken)`, and `ISqlExecutionService.ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken)` accepts a full `SqlCommand` (output parameters and `SqlDbType.Structured`/TVP parameters both work unchanged), so that specific swap is exactly as low-risk as advertised.

But `SqlMergeRepository` is not what implements `IFhirRepository` — `SqlEntityFrameworkRepository.cs` (1118 lines) does, and it is EF-LINQ-heavy almost everywhere else: 32 `_context.` call sites, 12 LINQ terminal operations (`FirstOrDefaultAsync`, `ToListAsync`, etc.), 6 `SaveChangesAsync` calls, and only 1 pre-existing raw-SQL call site across its 12 public methods (`GetAsync`, `CreateOrUpdateAsync`, `DeleteAsync`, `GetNextTransactionIdAsync`, `BatchWriteAsync`, `CommitTransactionAsync`, `GetStalledTransactionsAsync`, `GetResourceHistoryAsync`, `GetTypeHistoryAsync`, `GetSystemHistoryAsync`, `GetExpiredResourcesAsync`, `HardDeleteResourceAsync`). Phase D is really: **write a new, complete `IFhirRepository` implementation from scratch on raw ADO.NET**, of which the TVP merge mechanism is one well-isolated, genuinely low-risk part.

**Cutover point confirmed by tracing the real caller chain.** `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:266`) — the same lazy-per-tenant factory Phase B/C already wired `SchemaDeployer` into — builds two delegates: `createRepository: Func<FhirDbContext, IFhirRepository>` (:327) and `createSearchService: Func<FhirDbContext, IFhirRepository, ISearchService>` (:347). The search-service delegate receives the already-built repository purely through the `IFhirRepository` interface and never downcasts it — confirmed by reading the full `SqlEntityFrameworkSearchService` construction call (:406-415). This means the repository implementation can be swapped without touching the still-EF-based search path, with no concrete-type coupling risk.

**Both write and read paths currently share one `SearchIndexReferenceDataCache` instance per tenant** (`_multiTenantCache.GetOrCreateCacheForTenant(tenantId, dbContextOptions)` at :323, passed into both delegates). `SearchIndexReferenceDataCache` is genuinely EF-dependent — not just LINQ reads but EF-tracked writes (`_context.Systems.Add(...)` + `SaveChangesAsync()` for on-demand new-system/quantity-code/search-param registration, confirmed at `Indexing/SearchIndexReferenceDataCache.cs:214-215,276-277,695-696`). It is also consumed throughout the EF read path (`ChainedExpressionProcessor`, `CompartmentSearchQueryGenerator`, `CompositeSearchParameterQueryGenerator`, `IncludeProcessor`, `RevIncludeProcessor`, `SearchParameterQueryGenerator`, `SqlEntityFrameworkSearchService`, `SqlEntityFrameworkSymbolResolver` all reference it) — so it cannot simply be relocated; a genuine new ADO.NET port must live alongside the existing EF one, not replace it, until Phase E.

**`GzipResourceCompressor` and the 16 `RowGenerators` are pure logic with zero DB/EF dependency** (confirmed via a repo-wide grep for `FhirDbContext|_context\.|DbContext|EntityFrameworkCore` across the `RowGenerators/` folder — zero matches). `GzipResourceCompressor` is also shared with the EF read path (`IncludeProcessor`, `RevIncludeProcessor`, `SqlEntityFrameworkSearchService` all take it as a constructor parameter), so it has the same relocate-vs-duplicate constraint as the cache. The `RowGenerators` are consumed only by `SqlMergeRepository` today, with no read-path conflict.

**`PostMergeExtensionUpdater` is already portable** — both `UpdateTokenSearchParamExtensionsAsync`/`UpdateUriSearchParamExtensionsAsync` build batched parameterized SQL and call `_context.Database.ExecuteSqlRawAsync(sql, parameters, ct)` (`PostMergeExtensionUpdater.cs:78-81,130-133`), matching CLAUDE.md's documented description. Mechanical retarget to `ISqlExecutionService`, not a real port.

**Phase B unified schema provisioning for both paths.** `DatabaseInitializer`/`Migrations/97.sql` were deleted in Phase B; `SchemaDeployer` (the same SSDT-dacpac-based deployer both `IFhirRepositoryFactory` and the new `Ignixa.DataLayer.SqlServer` project already use) is now the only schema-bootstrap path, for both the EF-based and the future ADO.NET-based repository. This makes differential testing between the two implementations straightforward: both are provisioned identically, so any behavioral difference is a real port bug, not a schema-provisioning artifact.

**Zero differential-test scaffolding exists today** comparing `Ignixa.DataLayer.SqlServer` against `Ignixa.DataLayer.SqlEntityFramework` — greenfield for this phase, though flagged as needed since the Phase 9 `fhir-to-sql-compiler` roadmap retrospective (design doc §8).

## 2. Scope decision

**One phase, sized like Phase B (multiple tasks, not split into sub-phases).** All 12 `IFhirRepository` methods must be ported before cutover regardless, since the factory swaps the whole class at once — splitting into independently-brainstormed/planned sub-phases would just insert an artificial pause mid-interface with no independent production value at each stop.

**In scope:**
- A complete new `IFhirRepository` implementation in `Ignixa.DataLayer.SqlServer`, covering all 12 methods currently on `SqlEntityFrameworkRepository`.
- The TVP merge/transaction mechanism (`BeginTransactionAsync`/`MergeResourcesAsync`/`CommitTransactionAsync`/`PutTransactionHeartbeatAsync`), the roadmap's original narrow scope — genuinely low-risk, mechanical retarget.
- A new ADO.NET-backed reference-data cache, replicating `SearchIndexReferenceDataCache`'s read-and-on-demand-write semantics.
- A new `PostMergeExtensionUpdater` retargeted to `ISqlExecutionService` (mechanical).
- Copies of `GzipResourceCompressor` and the 16 `RowGenerators` into `Ignixa.DataLayer.SqlServer`.
- The differential-test harness (§4) and the cutover itself (§5).

**Out of scope** (§6).

## 3. Component boundary

**Nothing moves out of `Ignixa.DataLayer.SqlEntityFramework`.** Every existing class (`SqlEntityFrameworkRepository`, `SqlMergeRepository`, `SearchIndexReferenceDataCache`, `GzipResourceCompressor`, the `RowGenerators`, `PostMergeExtensionUpdater`) stays exactly where it is, untouched — still load-bearing for `SqlEntityFrameworkSearchService`'s reads until Phase E, and not deleted until Phase F retires the whole project.

**`Ignixa.DataLayer.SqlServer` gains a full new `IFhirRepository` implementation**, built from:
- A genuine port of the 12 `IFhirRepository` methods, hand-written parameterized SQL replacing LINQ, using `ISqlExecutionService`.
- A genuine port of the TVP merge mechanism (mechanical — same SQL text, same TVP shapes, just `ISqlExecutionService.ExecuteNonQueryAsync` instead of `_context.Database.ExecuteSqlRawAsync`).
- A genuine port of `SearchIndexReferenceDataCache`'s semantics — real work, since it has both reads and EF-tracked on-demand writes to replicate as raw SQL.
- A mechanical retarget of `PostMergeExtensionUpdater`'s SQL execution calls.
- **Copies, not moves**, of `GzipResourceCompressor` and the `RowGenerators` — both are pure logic, both are still needed by the EF project's read path, so relocating them would break `SqlEntityFrameworkSearchService`/`IncludeProcessor`/`RevIncludeProcessor` before Phase E. The EF copies are deleted in Phase F; duplication is a deliberate, temporary, accepted state, not an oversight.

Exact class names (the new repository's name, the merge mechanism's name, the cache port's name) are finalized during the implementation plan, matching how every prior phase in this initiative has handled naming.

**Cutover mechanism:** `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory`'s `createRepository` delegate (currently `SqlEntityFrameworkRepositoryFactory.cs:327`) is changed to construct the new `Ignixa.DataLayer.SqlServer`-based implementation instead of `SqlEntityFrameworkRepository`. The `createSearchService` delegate is unchanged — it continues receiving whatever `IFhirRepository` it's handed, purely through the interface, with no code change required on the read side.

**Known transitional risk, accepted not solved:** during the Phase D→E window, the write path (new ADO.NET-backed cache) and the read path (existing EF-backed cache) hold two independent in-memory caches over the same reference-data tables (`dbo.System`, `dbo.SearchParam`, `dbo.QuantityCode`, etc.) for the same tenant. They cannot corrupt data — the underlying tables' uniqueness constraints are the real guard against a duplicate insert race, and a cache miss on either side just costs an extra DB round-trip, not wrong data — but a write through the new path could register a new System that the read-side EF cache doesn't know about until its own next reload/miss. Stated explicitly here as an accepted transitional gap; not mitigated further in this phase.

## 4. Testing

**A genuine differential-test suite, built from scratch.** Deploy two fresh tenant databases via `SchemaDeployer` (identical provisioning on both sides, per §1). Perform an equivalent operation — create, update, delete, batch write, hard-delete, TTL upsert, and any other state-mutating method — through each `IFhirRepository` implementation against its own database. Assert the resulting **row-level state** is identical between the two databases, not just return values, since side effects in the database are the actual thing under test. This requires a real SQL-level comparison harness (dump the relevant tables from both databases and diff them) that does not exist yet and is itself part of this phase's scope.

**Golden-shape unit tests** for each new component in isolation (the merge/TVP mechanism, the reference-data cache port, the extension updater) — exact assertions, never loose non-null checks, matching this initiative's standing discipline from Phases A-C.

**Error-handling parity is a first-class testing requirement, not an afterthought.** The existing implementation has special-cased SQL error handling that must be replicated exactly — e.g. `SqlMergeRepository.MergeResourcesAsync` catches `SqlException` with `Number == 50409` and rethrows as `PreconditionFailedException` (FHIR version-conflict semantics). Every such special case across the 12 ported methods must be found (not assumed absent) and covered by a differential or golden-shape test, not discovered later in production.

## 5. Rollout

**Straight swap once tests pass — no feature flag, no dual-write.** Once the differential-test suite and golden-shape tests establish behavioral equivalence, `CreateServiceFactory`'s `createRepository` delegate always returns the new implementation for every SqlServer-storage tenant. Correctness is established before merge via tests, not after deploy via a runtime toggle. Revert path, if something is wrong post-deploy, is redeploying the prior commit — matching how `SchemaDeployer.UpgradeIfNeededAsync` (Phase C) and every other safety-critical mechanism in this initiative has shipped: proven correct before merge, not gated behind a kill switch.

## 6. Explicitly out of scope

Named here so it is not silently assumed or silently dropped later:

- **Any read/search-path change** — `SqlEntityFrameworkSearchService` and everything it depends on stays exactly as-is until Phase E.
- **A feature-flag or dual-write/shadow-write mechanism** — explicitly rejected in favor of straight-swap (§5).
- **Removing `FhirDbContext` or any EF Core package reference from the solution** — Phase F's job, once both write and read paths are cut over.
- **Exact class names/file structure for the new components** — finalized during the implementation plan.
- **Resolving the transitional dual-cache risk (§3)** — accepted as a known, low-severity gap for the Phase D→E window, not mitigated in this phase.

## 7. Process note

Given the write-path blast radius (a live production data path for a healthcare system), the implementation plan itself gets a Fable-model review before subagent-driven execution begins — in addition to the task-scoped reviews after each task and the final whole-branch review at the end, which is this initiative's standing process for every phase.
