# Ignixa.DataLayer.SqlServer — Phase B Design: SQL Database Projects Adoption

**Status:** Approved, ready for implementation planning.

**Parent design:** `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` (§3, §5) — read that first for the full 6-phase roadmap (A–F) and overall motivation. This document covers Phase B only.

**Branch:** continues directly on `worktree-ignixa-datalayer-sqlserver` (Phase A's branch — not merged anywhere, pushed standalone to origin per explicit user decision). Phase B does not get its own branch.

## 1. Scope

Phase A (complete, pushed) built the project skeleton and a tenant-scoped raw-ADO.NET connection/execution layer (`ISqlExecutionService`). Phase B replaces how Ignixa's SQL schema is *authored and deployed*, without changing what the schema actually *is*:

- Decompose the existing `97.sql` baseline (6,349 lines, single file) into a proper SDK-style SQL Database Project (`.sqlproj`), one file per object, verified byte-for-byte identical via a zero-diff `SqlPackage /Action:DeployReport` check — not incremental, not hand-verified.
- Fold the 6 existing EF Core migrations into that same decomposed baseline.
- Retire EF Core Migrations as the schema-authoring/bootstrap mechanism entirely.
- Switch new-tenant database bootstrap from "execute raw `97.sql` text + run EF migrations" to "deploy the SSDT-built `.dacpac`" via the DacFx (`Microsoft.SqlServer.Dac`) .NET API, in-process — no `sqlpackage` CLI dependency at runtime.
- Give `Ignixa.Search.Sql.Generators`' `SqlCatalogGenerator` a new input-source strategy (many files instead of one).
- Add a CI step that builds the `.dacpac` and produces a reviewable `DeployReport` diff artifact — `/Action:Publish` never runs unattended in CI.

**Ground truth established before this design** (see the parent design doc and this session's research): fhir-server's own SSDT/generator tooling is not publicly accessible — this is original Ignixa tooling informed by the visible script-pairing shape (full + diff `.sql` per version, an "Auto-Generated from Sql build task" header already inherited verbatim in `97.sql`), not a port. fhir-server's schema versions 98–113 (16 versions ahead of Ignixa's v97 baseline) contain zero table/index/column DDL changes — confirmed by exhaustive grep — so Phase B has no real schema content to "catch up" on; it is purely a tooling/process shift for Ignixa's *existing* v97 shape.

**Ground truth established during this design's brainstorm** (direct source reads, not assumption):
- `FhirDbContext.OnModelCreating` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/FhirDbContext.cs`) is pure Fluent API, hand-authored to mirror `97.sql` — comments literally say "matches 97.sql legacy schema." It is **not** auto-derived from migrations; the 6 EF migrations only account for post-`97.sql` deltas (background jobs, package/terminology indexes, terminology import tracking, resource TTL, source events, search-param extension columns).
- `DatabaseInitializer.InitializeAsync` (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/DatabaseInitializer.cs`) checks emptiness via `sys.tables` presence of the `Resource` table, executes embedded `97.sql` text if empty, then always runs `Database.GetPendingMigrationsAsync`/`MigrateAsync`. No dedicated `SchemaVersion` table or startup schema-mismatch gate exists today.
- Reads/search (`Search/*.cs`) are typed EF LINQ against `DbSet<T>` — depend on the Fluent API model matching real DDL. Writes (`SqlMergeRepository.cs`) are raw `ExecuteSqlRawAsync` calls to stored procedures with TVPs — independent of the EF model, resilient to DDL-authoring-tool changes as long as object names/shapes match (`TvpSchemaProvider.cs` introspects TVP columns from SQL Server at runtime).
- `Ignixa.Search.Sql.csproj` currently has one `<AdditionalFiles Include="...\97.sql" />`, consumed by `SqlCatalogGenerator` (a Roslyn incremental generator) to emit `SqlCatalog.Default`'s facts.
- This repo already has an environment-specific config precedent (`Ignixa.Web/appsettings.Development.json` overriding `Ignixa.Web/appsettings.json`) to build the auto-upgrade toggle on.

## 2. New SSDT project

New project: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj` (SDK-style, `Microsoft.Build.Sql`). Lives alongside `Ignixa.DataLayer.SqlServer` (Phase A) as a sibling — a schema-only project, not a C# code project.

Folder convention, one file per object:
```
Tables/*.sql              -- one CREATE TABLE per file
Views/*.sql
StoredProcedures/*.sql
Types/*.sql                -- TVP type definitions
Functions/*.sql            -- if any exist in 97.sql
Security/*.sql             -- schemas/roles, if any
```
Files named after the object (`Tables/Resource.sql`, `StoredProcedures/MergeResources.sql`, etc.), matching SSDT convention and the fhir-server script-pairing shape this baseline already inherited.

**Verification (the actual pass/fail gate for the decomposition task):**
```
dotnet build Ignixa.DataLayer.SqlServer.Database.sqlproj   →  .dacpac
sqlpackage /Action:DeployReport
  /SourceFile:<dacpac>
  /TargetConnectionString:<a DB bootstrapped the OLD way, via 97.sql + the 6 EF migrations>
```
Must report **zero** table/column/index/constraint/procedure/type differences. This replaces hand-verification entirely, per the user's explicit choice.

## 3. `SqlCatalogGenerator` input-source change

`Ignixa.Search.Sql.csproj`'s single-file `<AdditionalFiles>` entry is replaced with a glob over the new SSDT project's relevant object folders:
```xml
<AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlServer.Database\Tables\*.sql" />
<AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlServer.Database\Views\*.sql" />
<AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlServer.Database\Types\*.sql" />
```
(Scoped to only the object kinds `SqlCatalogGenerator` actually extracts facts from today — the implementation plan must confirm exactly which kinds by reading the generator's current parsing logic, not assume.)

`SqlCatalogGenerator` changes internally from "read one `AdditionalText`" to "collect all matching `AdditionalTexts`, parse each independently (parsing logic itself is unchanged — still per-statement T-SQL text), merge into one `SqlCatalog.Default` model." Since each object now lives in exactly one file under the decomposed layout (no cross-file redefinition), the merge is a straight union — no precedence/conflict logic needed.

The old `97.sql` `<AdditionalFiles>` reference is removed once this is wired up.

## 4. Bootstrap/deploy ownership

**Ownership split:**
- `Ignixa.DataLayer.SqlServer.Database` (the `.sqlproj`) owns the schema *definition* only. Its sole build output is the `.dacpac`, embedded as a resource for downstream consumption.
- A new class, `SchemaDeployer`, lives in `Ignixa.DataLayer.SqlServer` (Phase A's raw-ADO.NET project — the right long-term home, since Phase D/E eventually move everything else there too). References `Microsoft.SqlServer.Dac` and the `.Database` project. This is the class that actually calls `DacServices.Deploy(...)`.
- `DatabaseInitializer` in `Ignixa.DataLayer.SqlEntityFramework` is removed; app startup wiring points at `SchemaDeployer` instead. This is a real production-wiring change — unlike Phase A's "zero production-facing change" constraint, Phase B is explicitly allowed to touch startup wiring, per the user's own choice to do the full bootstrap cutover now rather than defer it.

**New-tenant bootstrap flow:**
1. Check connectivity (unchanged from today).
2. Check emptiness via `sys.tables` (unchanged signal, same query as today).
3. If empty **and** the auto-upgrade toggle (below) is enabled: `DacServices.Deploy(dacpacPackage, databaseName, upgradeExisting: false, connection)`. `upgradeExisting: false` is a deliberate assertion, not just a default — if a bug elsewhere ever routes a non-empty database into this call, DacFx throws rather than silently attempting an unreviewed upgrade against existing schema. This is the concrete mechanism enforcing "empty-DB bootstrap is exempt from the never-auto-publish rule; existing DBs are never auto-touched."
4. If not empty: **no action.** Phase B assumes any existing tenant database is already schema-identical to the dacpac (true by construction, since decomposition is zero-diff-verified). No pending-migration check, no `MigrateAsync` — that whole mechanism is retired, not merely unused.
5. If empty and the toggle is disabled: fail fast at startup with a clear, actionable error ("database not initialized; run schema deployment manually") — never silently block, never proceed half-initialized.

**Auto-upgrade toggle** (config flag, borrowed directly from fhir-server's `SqlServerSchemaOptions.AutomaticUpdatesEnabled` pattern, per explicit user direction): `SqlServer:AutomaticSchemaDeploymentEnabled`.
- `true` in `appsettings.Development.json` (matching the user's "we always set it to enabled in dev environments to keep things easy").
- `false` in the base `appsettings.json` — Production requires an explicit opt-in.
- This flag is intentionally general ("does this app apply schema changes unattended") rather than Phase-B-specific — Phase C's future existing-database upgrade logic reuses the same flag rather than inventing a second one.

## 5. EF Migrations retirement

Once `SchemaDeployer` replaces `DatabaseInitializer`, nothing calls `Database.GetPendingMigrationsAsync`/`MigrateAsync`, and nothing executes raw `97.sql` text. Per this repo's own "delete confirmed-unused code" convention, Phase B deletes:
- `Ignixa.DataLayer.SqlEntityFramework/Migrations/` — all 6 migration + `.Designer.cs` files, plus `FhirDbContextModelSnapshot.cs`.
- `Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql` and its `EmbeddedResource`/`None Remove` csproj entries.
- `DatabaseInitializer.cs` and its tests.
- The `OnConfiguring` override that ignores `PendingModelChangesWarning` (no longer relevant — nothing calls `Migrate*` to trigger that warning).

`FhirDbContext.OnModelCreating`'s Fluent API model is **untouched** — still required for LINQ query translation against the (now SSDT-authored, shape-identical) tables. Only the migration/bootstrap machinery is retired, not the query-mapping model. Git history preserves the deleted files if ever needed for reference.

## 6. CI pipeline change

CI gains: `dotnet build Ignixa.DataLayer.SqlServer.Database.sqlproj` → `.dacpac` → `sqlpackage /Action:DeployReport` against the same SQL Server container CI already runs for E2E tests → publish the diff report as a build artifact for human review. `/Action:Publish` never runs in CI — matches the standing "always generate-then-review, never auto-publish" rule, which continues to apply to any deploy against an existing/populated target. The only exempted case is the empty-DB in-process bootstrap path from §4, which is a different mechanism (DacFx API, not the CI pipeline) applied to a target that is, by construction, empty.

## 7. Testing

- `SchemaDeployer` integration tests: deploy against a genuinely empty database succeeds and produces the expected tables (golden-shape assertions on actual `sys.tables`/`sys.columns` contents, not loose non-null checks — matching this branch's established discipline); deploy attempted against a non-empty database throws, proving the `upgradeExisting: false` guard actually guards; toggle-disabled-and-empty fails fast with the expected error, not a hang or silent no-op.
- The zero-diff `DeployReport` check (§2) is the primary proof the decomposition didn't silently change anything — a build-time/CI gate, not a unit test, but must run on every PR touching the `.sqlproj`.
- Old `DatabaseInitializer`-focused tests are deleted alongside the class they test.

## 8. Explicitly out of scope for Phase B

- Existing-tenant schema upgrades (applying a *changed* dacpac to an already-populated database) — Phase C's schema-version compatibility layer.
- Any actual schema *changes* — Phase B is a pure re-authoring of the current v97 shape, zero-diff verified. The first real schema change authored via SSDT happens in a future phase.
- Changes to `SqlCatalogGenerator`'s fact-extraction logic to understand new T-SQL constructs — only its input-gathering (§3) changes in Phase B.
- Read-path or write-path wiring into `ISqlExecutionService` — that's Phase D (writes) and Phase E (reads), gated as described in the parent design doc.
