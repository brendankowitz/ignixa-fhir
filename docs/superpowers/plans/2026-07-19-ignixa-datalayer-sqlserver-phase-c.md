# Ignixa.DataLayer.SqlServer Phase C: Schema-Version Compatibility Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give every tenant database a tracked schema version, an in-process destructive-operation classifier that decides whether a pending upgrade is safe to auto-apply, an on-connect auto-upgrade path restricted to provably-safe (expand-only) diffs, and an explicit operator-triggered path for everything else — closing the "existing, already-populated database" case `SchemaDeployer` (Phase B) deliberately left unhandled.

**Architecture:** `SchemaVersion` is a simple, manually-bumped integer (no per-version stored snapshots) stamped into a new per-tenant `SchemaVersion` table. On tenant connect, `SchemaDeployer` gains a new upgrade path: if the tenant is behind, generate one in-process `DacServices.GenerateDeployReport` diff against the current dacpac, classify it via an allow-list seeded directly from Phase B's own proven-benign findings (Categories B/C/D/E), and either auto-apply + stamp, or refuse and point at a new CLI tool for explicit, reviewed operator application. A version-gating primitive (`ISchemaVersionResolver` + `SchemaVersionConstants`) ships alongside this, with no real caller yet — Phase D/E's job.

**Tech Stack:** Same as Phase B — `Microsoft.SqlServer.DacFx` (in-process `DacServices.GenerateDeployReport`, confirmed as a real instance method returning XML directly, no CLI shell-out needed), `System.CommandLine` (matching this repo's existing `tools/*.Cli` convention) for the new operator tool, `System.Xml.Linq` for parsing `DeployReport` XML.

**Full design:** `docs/superpowers/specs/2026-07-19-ignixa-datalayer-sqlserver-phase-c-design.md` — read this first for the *why* and every locked-in decision. Parent design: `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` §6. Phase D+ (write-path migration, read cutover, retiring `SqlEntityFramework`, and any real version-*gated* read/write behavior) are explicitly out of scope for this plan.

## Global Constraints

- This plan runs directly in the git worktree already in use for this initiative: `C:\src\ignixa-fhir\.claude\worktrees\ignixa-datalayer-sqlserver` (branch `worktree-ignixa-datalayer-sqlserver`). No new worktree, no new branch — continues Phase B's branch directly. This branch does **not** merge into `feature/fhir-to-sql-compiler`; it stays standalone and gets pushed to origin directly, matching Phase A and Phase B.
- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures, known and out of scope per every prior increment.
- **The real, current `DeployReport` XML schema** (confirmed by generating one directly against the current dacpac — not assumed, not transcribed from an earlier report's condensed paraphrase): root element `<DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02">`, containing `<Alerts><Alert Name="...">​<Issue Value="..." /></Alert></Alerts>` and `<Operations><Operation Name="Drop|Create|Alter|UnbindTable|TableRebuild|Refresh|...">​<Item Value="..." Type="..." /></Operation></Operations>`. A real, verified example (self-consistency comparison against the current dacpac, no real schema drift):
  ```xml
  <?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Alerts><Alert Name="DataMotion"><Issue Value="[dbo].[ResourceChangeData]" /></Alert></Alerts><Operations><Operation Name="Drop"><Item Value="unnamed constraint on [dbo].[SchemaMigrationProgress]" Type="SqlDefaultConstraint" /><Item Value="[dbo].[CH_Resource_RawResource_Length]" Type="SqlCheckConstraint" /><Item Value="[PartitionScheme_ResourceChangeData_Timestamp]" Type="SqlPartitionScheme" /><Item Value="[PartitionFunction_ResourceChangeData_Timestamp]" Type="SqlPartitionFunction" /><Item Value="[dbo].[DF_ResourceChangeData_Timestamp]" Type="SqlDefaultConstraint" /></Operation><Operation Name="Create"><Item Value="[PartitionFunction_ResourceChangeData_Timestamp]" Type="SqlPartitionFunction" /><Item Value="[PartitionScheme_ResourceChangeData_Timestamp]" Type="SqlPartitionScheme" /><Item Value="Default Constraint: unnamed constraint on [dbo].[SchemaMigrationProgress]" Type="SqlDefaultConstraint" /><Item Value="[dbo].[CH_Resource_RawResource_Length]" Type="SqlCheckConstraint" /></Operation><Operation Name="UnbindTable"><Item Value="[dbo].[ResourceChangeData]" Type="SqlTable" /></Operation><Operation Name="TableRebuild"><Item Value="[dbo].[ResourceChangeData]" Type="SqlTable" /></Operation><Operation Name="Refresh"><Item Value="[dbo].[CaptureResourceIdsForChanges]" Type="SqlProcedure" /><Item Value="[dbo].[FetchResourceChanges_3]" Type="SqlProcedure" /><Item Value="[dbo].[CaptureResourceChanges]" Type="SqlProcedure" /><Item Value="[dbo].[MergeResources]" Type="SqlProcedure" /></Operations></DeploymentReport>
  ```
  Note Category E (see below) does **not** appear here — it only manifests when comparing against a database whose catalog was populated by the old EF migrations' `CAST(...)`-emitting default-value code path, which no `SchemaDeployer`-bootstrapped tenant will ever have. Include it in the allow-list defensively anyway (a tenant could theoretically predate `SchemaDeployer`), but it is not expected to appear in practice.
- **The destructive-operation classifier's design principle** (re-read `docs/superpowers/specs/2026-07-19-ignixa-datalayer-sqlserver-phase-c-design.md` §3 for the full rationale): `Create` and `Refresh` operations are **never** classified as destructive — `Create` fails loudly at deploy time if invalid (e.g. a new unique constraint against duplicate data) rather than silently corrupting, and `Refresh` only recompiles a procedure's schema binding, it never changes shape or loses data. Only `Drop`, `Alter`, `TableRebuild`, and `UnbindTable` operations need allow-list matching per-item; anything of those kinds whose `Item` doesn't match a known-benign pattern marks the whole diff unsafe. This resolves Category D's Refresh-list variability (documented below) without needing to hardcode procedure names or build transitive reference tracking.
- **The allow-list, seeded from Phase B's own proven findings** (re-derived directly from `.superpowers/sdd/task-2-report.md` and `.superpowers/sdd/task-9-report.md`, not the design doc's paraphrase — re-read those reports if still present in this gitignored, worktree-local directory; if gone, this list is still complete and authoritative on its own):
  - **Category B/E** — any `Drop`/`Create` `Item` with `Type="SqlDefaultConstraint"` (any object name) — default-value canonicalization noise (`CURRENT_TIMESTAMP` vs. `getdate()`, and `CAST(...)`-emitted vs. plain-literal typed defaults) is never destructive to existing rows, only affects future inserts.
  - **Category C** — any `Drop`/`Create` `Item` with `Type="SqlCheckConstraint"` and `Value` containing `CH_Resource_RawResource_Length` — narrow, name-matched (hex-literal canonicalization proven specific to this one constraint; do not generalize to all check constraints without new evidence).
  - **Category D** — any `Drop`/`Create` `Item` with `Type="SqlPartitionScheme"` or `Type="SqlPartitionFunction"` and `Value` containing `PartitionScheme_ResourceChangeData_Timestamp` or `PartitionFunction_ResourceChangeData_Timestamp`; any `UnbindTable`/`TableRebuild` `Item` with `Type="SqlTable"` and `Value="[dbo].[ResourceChangeData]"`. (The dependent-procedure `Refresh` operations Category D also produces need no allow-list entry at all, since `Refresh` is categorically non-destructive per the principle above — this was empirically necessary: Task 2's original report showed 7 refreshed procedures, Task 9's showed 4, because the set is transitive on *other*, unrelated diffs elsewhere in the same report, not a fixed list.)
- **`SchemaDeployer`'s current exact shape** (`src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs`, 162 lines, already reviewed and approved in Phase B — read it in full before Task 1): constructor `(ITenantConfigurationStore, IHostEnvironment, IOptions<SqlServerOptions>, ILogger<SchemaDeployer>)`. `DeployIfEmptyAsync(int tenantId, CancellationToken)` — resolves connection string via `SqlExecutionService.ResolveConnectionStringAsync` (static, internal, already shared) → dev-mode `CreateEmptyDatabaseAsync`+`WaitUntilConnectableAsync` if unreachable → `IsDatabaseEmptyAsync` (no-op if not empty) → `AutomaticSchemaDeploymentEnabled` toggle check → load embedded dacpac → `DacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken:)`. This plan does not alter this method's existing safety logic (connectivity/emptiness/toggle checks, the `upgradeExisting: true` deploy call) — the one narrow, additive exception is Task 1's version-stamp insert after a successful deploy (necessary bookkeeping, not a behavior change; explicitly justified in Task 1).
- **`SqlExecutionService.ResolveConnectionStringAsync`** (`SqlExecutionService.cs:33-82`, already shared/reused by `SchemaDeployer`): `internal static async Task<string> ResolveConnectionStringAsync(ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)`. The new `ISchemaVersionResolver` (Task 4) and the CLI tool (Task 6) must call this exact method, not duplicate its tenant/storage-type/system-partition-inheritance validation logic.
- **`Tables/*.sql` DDL convention** (established across Phase B Tasks 1/5/8/9): `CREATE TABLE dbo.<Name> (` unbracketed identifiers, one column per line, uppercase types with a space before parens (`SMALLINT`, `VARCHAR (64)`), inline `CONSTRAINT PK_<Table> PRIMARY KEY (...)` for the PK, `IDENTITY (1, 1)` with spaces, `GO`-batched index statements after the table. No FK/index precedent needed for `SchemaVersion` (it's a simple standalone table, no relationships).
- **`Microsoft.SqlServer.DacFx`'s in-process `DeployReport` generation** (confirmed directly against Microsoft's own API reference): `public string DacServices.GenerateDeployReport(DacPackage package, string targetDatabaseName, DacDeployOptions options = default, CancellationToken? cancellationToken = default)` — an **instance** method (same `DacServices` instance already used for `Deploy`), returns the report XML directly as a string. No `sqlpackage` CLI shell-out needed at runtime, matching `SchemaDeployer`'s existing in-process-only design.
- **No existing CLI tool in this repo is tenant/database-aware** (checked `tools/Ignixa.SqlOnFhir.Cli`, `tools/Ignixa.DeId.Cli` — both are standalone file-processing tools using `System.CommandLine` + `PackAsTool=true`, `net10.0`; neither references `ITenantConfigurationStore`/`appsettings.json`, neither has a confirmation-prompt pattern). Task 6's CLI is a first-of-its-kind pattern in this repo — it reuses the `System.CommandLine`/`PackAsTool` project shape from precedent, but must reference `Ignixa.DataLayer.SqlServer` and load `appsettings.json`/`appsettings.{ASPNETCORE_ENVIRONMENT}.json` via `Microsoft.Extensions.Configuration` directly (a minimal, standalone config load — not the full app's DI/hosting stack) to resolve tenant connection strings through the same `ITenantConfigurationStore`-based mechanism the running app uses, rather than accepting a raw connection string from the operator (which would bypass system-partition inheritance and be inconsistent with how tenant resolution works everywhere else).
- **Environment**: Docker is unavailable in this sandboxed session; this machine's `(localdb)\MSSQLLocalDB` is SQL Server 2019, incompatible with the `.sqlproj`'s `Sql170DatabaseSchemaProvider` (SQL Server 2025) target. Every task needing a real SQL Server instance uses the local SQL Server 2025 engine at `Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` instead (the established substitution from Phase B Tasks 4/5/6/8/9).
- **Testing discipline**: exact assertions (real classifier verdicts against real captured XML, real `sys.tables` contents, real error messages), never loose non-null checks — matching every prior phase.

---

### Task 1: `SchemaVersion` table, version constants, and stamping on empty-database deploy

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/SchemaVersion.sql`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs` (stamp the version after a successful empty-database deploy — the one narrow, additive exception noted in Global Constraints)
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerDeploymentTests.cs` (extend — confirm the stamped row after a fresh deploy)

**Interfaces:**
- Consumes: nothing new.
- Produces: `SchemaVersionConstants.CurrentVersion` (`int`), `SchemaVersionConstants.MinSupportedReadVersion` (`int`) — consumed by Task 4 (`ISchemaVersionResolver`) and Task 5 (`SchemaDeployer`'s new upgrade path). The `dbo.SchemaVersion` table (columns: `Version INT NOT NULL`, `AppliedAt DATETIMEOFFSET NOT NULL`) — consumed by Task 4/5.

- [ ] **Step 1: Write `Tables/SchemaVersion.sql`**

```sql
CREATE TABLE dbo.SchemaVersion (
    Version   INT             NOT NULL,
    AppliedAt DATETIMEOFFSET  NOT NULL DEFAULT sysutcdatetime(),
    CONSTRAINT PK_SchemaVersion PRIMARY KEY (Version)
);
```
(`Version` as the primary key, not an identity/single-row design, deliberately — this keeps a full history of every version this tenant has ever been stamped at, in case Phase D+ ever needs to know "when did this tenant cross version N," not just its current state. The *current* version is `SELECT MAX(Version) FROM dbo.SchemaVersion`.)

- [ ] **Step 2: Confirm the SSDT project still builds**

```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
```
Expected: 0 errors, warning count/categories unchanged from Phase B's established 325-warning baseline (no new warnings referencing `SchemaVersion`).

- [ ] **Step 3: Write `SchemaVersionConstants.cs`**

```csharp
namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// This project's compiled-in schema-version window. Bumped by whoever authors a real
/// schema change, alongside an expand/contract classification recorded in the changelog
/// below -- mirrors fhir-server's SchemaVersionConstants pattern, adapted for Ignixa's
/// per-tenant (not single-shared-database) versioning model.
/// </summary>
public static class SchemaVersionConstants
{
    /// <summary>The schema version this build's dacpac represents.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The oldest tenant schema version this build still tolerates reading an
    /// un-upgraded tenant against. No version-gated read/write behavior exists yet
    /// (Phase D/E's job) -- this constant is the primitive future code will check.
    /// </summary>
    public const int MinSupportedReadVersion = 1;

    // Changelog (append, never edit history):
    // Version 1 (expand) -- introduces the SchemaVersion table itself. Every tenant
    // database, new or upgraded, starts here.
}
```

- [ ] **Step 4: Stamp the version after a successful empty-database deploy**

In `SchemaDeployer.cs`, after the existing `dacServices.Deploy(...)` call succeeds (the line already ending with `_logger.LogInformation("Deployed schema to tenant {TenantId}'s new database...")`), add:
```csharp
        await StampSchemaVersionAsync(connectionString, SchemaVersionConstants.CurrentVersion, cancellationToken);
```
Add the new private static helper, matching the file's existing style (`await using`, parameterized command):
```csharp
    private static async Task StampSchemaVersionAsync(string connectionString, int version, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT dbo.SchemaVersion (Version) VALUES (@version)";
        command.Parameters.AddWithValue("@version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
```
This is the one narrow, additive change to `DeployIfEmptyAsync` this plan makes — it does not alter the method's existing connectivity/emptiness/toggle-check/`Deploy`-call logic, only adds a bookkeeping insert after a deploy that already succeeded. Without it, every freshly-deployed tenant would show as unversioned and look permanently "behind" on its very next connection.

- [ ] **Step 5: Extend the existing empty-database integration test**

In `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerDeploymentTests.cs`, extend `GivenAnEmptyDatabase_WhenDeployIfEmptyAsyncCalled_ThenCreatesTheExpectedTables` (or add a new fact if that one is already scoped tightly) with an exact assertion:
```csharp
        // ... after the existing table-existence assertions ...
        await using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(Version) FROM dbo.SchemaVersion";
        var stampedVersion = (int)(await versionCommand.ExecuteScalarAsync(CancellationToken.None))!;
        stampedVersion.ShouldBe(SchemaVersionConstants.CurrentVersion);
```

- [ ] **Step 6: Run the tests**

```
dotnet build All.sln
```
Set `TEST_SQL_CONNECTION_STRING=Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` and:
```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SchemaDeployerDeploymentTests
```
Expected: 0 warnings/errors on build; the extended/new test passes, confirming a fresh empty-database deploy now stamps `SchemaVersion`.

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/SchemaVersion.sql src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionConstants.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerDeploymentTests.cs
git commit -m "feat(datalayer-sqlserver): add SchemaVersion table and stamp it on empty-database deploy"
```

---

### Task 2: Post-deployment script idempotency

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Scripts/Script.PostDeployment.sql`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/PostDeploymentScriptIdempotencyTests.cs` (new)

**Interfaces:**
- Consumes: nothing new.
- Produces: a `Script.PostDeployment.sql` safe to run more than once against the same database — a prerequisite for Task 5's upgrade path, which will run `Deploy` (and therefore this script) against already-populated tenant databases.

**Why**: Phase B's Task 2/9 testing found that re-running `sqlpackage /Action:Publish` against an already-deployed database re-executes this script's `ResourceChangeType` seed `INSERT`s unconditionally, failing loudly with `SQL72014` (duplicate key on `PK_ResourceChangeType`) on the second run. The partition-splitting loop also redundantly re-runs its full 770-iteration split every time, wasted work (not a correctness bug — Category D's rebuild is already proven benign) but worth avoiding on a live database.

The current file (`src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Scripts/Script.PostDeployment.sql`):
```sql
DECLARE @numberOfHistoryPartitions AS INT = 48;
DECLARE @numberOfFuturePartitions AS INT = 720;
DECLARE @rightPartitionBoundary AS DATETIME2 (7);
DECLARE @currentDateTime AS DATETIME2 (7) = sysutcdatetime();
WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
    BEGIN
        SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);
        ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
        ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
            SPLIT RANGE (@rightPartitionBoundary);
        SET @numberOfHistoryPartitions -= 1;
    END

INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (0, N'Creation');

INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (1, N'Update');

INSERT  dbo.ResourceChangeType (ResourceChangeTypeId, Name)
VALUES                        (2, N'Deletion');
```

- [ ] **Step 1: Guard the partition-splitting loop with a boundary-count check**

Replace the file's opening with:
```sql
IF (SELECT COUNT(*) FROM sys.partition_range_values prv
    JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
    WHERE pf.name = 'PartitionFunction_ResourceChangeData_Timestamp') <= 1
BEGIN
    DECLARE @numberOfHistoryPartitions AS INT = 48;
    DECLARE @numberOfFuturePartitions AS INT = 720;
    DECLARE @rightPartitionBoundary AS DATETIME2 (7);
    DECLARE @currentDateTime AS DATETIME2 (7) = sysutcdatetime();
    WHILE @numberOfHistoryPartitions >= -@numberOfFuturePartitions
        BEGIN
            SET @rightPartitionBoundary = DATEADD(hour, DATEDIFF(hour, 0, @currentDateTime) - @numberOfHistoryPartitions, 0);
            ALTER PARTITION SCHEME PartitionScheme_ResourceChangeData_Timestamp NEXT USED [Primary];
            ALTER PARTITION FUNCTION PartitionFunction_ResourceChangeData_Timestamp( )
                SPLIT RANGE (@rightPartitionBoundary);
            SET @numberOfHistoryPartitions -= 1;
        END
END
```
(A freshly-created partition function has exactly 1 boundary value — the `1970-01-01` starting point declared in `Storage/PartitionFunction_ResourceChangeData_Timestamp.sql`. `<= 1` means "never split" — run the loop. Anything greater means it's already been split at least once; skip.)

- [ ] **Step 2: Guard the seed `INSERT`s with `IF NOT EXISTS`**

Replace the 3 `INSERT` statements with:
```sql
IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 0)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (0, N'Creation');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 1)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (1, N'Update');

IF NOT EXISTS (SELECT 1 FROM dbo.ResourceChangeType WHERE ResourceChangeTypeId = 2)
    INSERT dbo.ResourceChangeType (ResourceChangeTypeId, Name) VALUES (2, N'Deletion');
```

- [ ] **Step 3: Confirm the SSDT project still builds**

```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
```
Expected: 0 errors, same warning baseline.

- [ ] **Step 4: Write the idempotency integration test**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/PostDeploymentScriptIdempotencyTests.cs`:
```csharp
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Dac;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class PostDeploymentScriptIdempotencyTests
{
    [Fact]
    public async Task GivenAnAlreadyDeployedDatabase_WhenPublishedAgain_ThenSucceedsWithoutError()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING is not set. Run the docker-compose.test.yml SQL Server " +
                "container and set this environment variable before running integration tests.");

        var databaseName = $"IgnixaPostDeployIdempotency_{Guid.NewGuid():N}";
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = databaseName };

        await using (var masterConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await masterConnection.OpenAsync();
            await using var createCommand = masterConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE [{databaseName}]";
            await createCommand.ExecuteNonQueryAsync();
        }

        try
        {
            var dacpacPath = TestDacpacLocator.FindDacpacPath();
            using var package = DacPackage.Load(dacpacPath);
            var dacServices = new DacServices(builder.ConnectionString);

            // First publish -- establishes the schema, including the post-deployment script's
            // partition split (770 boundaries) and 3 ResourceChangeType seed rows.
            dacServices.Deploy(package, databaseName, upgradeExisting: true);

            // Second publish against the SAME now-populated database -- this is exactly the
            // scenario that failed with SQL72014 before this task's fix.
            Should.NotThrow(() => dacServices.Deploy(package, databaseName, upgradeExisting: true));

            await using var verifyConnection = new SqlConnection(builder.ConnectionString);
            await verifyConnection.OpenAsync();
            await using var countCommand = verifyConnection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(*) FROM dbo.ResourceChangeType";
            var rowCount = (int)(await countCommand.ExecuteScalarAsync())!;
            rowCount.ShouldBe(3);

            await using var partitionCommand = verifyConnection.CreateCommand();
            partitionCommand.CommandText = @"
                SELECT COUNT(*) FROM sys.partition_range_values prv
                JOIN sys.partition_functions pf ON pf.function_id = prv.function_id
                WHERE pf.name = 'PartitionFunction_ResourceChangeData_Timestamp'";
            var boundaryCount = (int)(await partitionCommand.ExecuteScalarAsync())!;
            boundaryCount.ShouldBe(770);
        }
        finally
        {
            await using var masterConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
            await masterConnection.OpenAsync();
            await using var dropCommand = masterConnection.CreateCommand();
            dropCommand.CommandText = $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]";
            await dropCommand.ExecuteNonQueryAsync();
        }
    }
}
```
`TestDacpacLocator.FindDacpacPath()` doesn't exist yet — add a small static helper (in the same test project, e.g. `TestDacpacLocator.cs`) that resolves `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac` relative to the test assembly's location (walk up from `AppContext.BaseDirectory` to the repo root, matching however this test project already locates repo-relative paths elsewhere — check `SchemaDeployerDeploymentTests.cs` for an existing pattern before inventing a new one; if it uses the embedded resource instead of a file path, use `typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")` the same way and skip `TestDacpacLocator` entirely).

- [ ] **Step 5: Build the dacpac fresh and run the test**

```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
dotnet build All.sln
```
Set `TEST_SQL_CONNECTION_STRING=Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` and:
```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~PostDeploymentScriptIdempotencyTests
```
Expected: passes — the second `Deploy` call no longer throws, row count stays 3, boundary count stays 770 (not doubled).

- [ ] **Step 6: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Scripts/Script.PostDeployment.sql test/Ignixa.DataLayer.SqlServer.IntegrationTests/PostDeploymentScriptIdempotencyTests.cs
git commit -m "fix(datalayer-sqlserver): make Script.PostDeployment.sql idempotent on re-publish"
```

---

### Task 3: Destructive-operation classifier

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/DeployReportClassifier.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/DeployReportClassifierTests.cs`
- Test fixtures: `test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/*.xml` (real and synthetic `DeployReport` XML)

**Interfaces:**
- Consumes: raw `DeployReport` XML (a `string`, as returned by `DacServices.GenerateDeployReport`).
- Produces: `public static class DeployReportClassifier { public static bool IsAutoSafe(string deployReportXml); }` — consumed by Task 5's `SchemaDeployer` upgrade path.

- [ ] **Step 1: Capture a real fixture — self-consistency (expected: safe)**

```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
```
Deploy the current dacpac fresh to a scratch database, then generate a `DeployReport` of that same dacpac against that same database (the self-consistency pattern already established in Phase B Tasks 2/5/9):
```
sqlcmd -S localhost -E -C -Q "CREATE DATABASE IgnixaPhaseCFixtureProbe"
sqlpackage /Action:Publish /SourceFile:src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac /TargetConnectionString:"Server=localhost;Database=IgnixaPhaseCFixtureProbe;Trusted_Connection=True;TrustServerCertificate=True"
sqlpackage /Action:DeployReport /SourceFile:src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac /TargetConnectionString:"Server=localhost;Database=IgnixaPhaseCFixtureProbe;Trusted_Connection=True;TrustServerCertificate=True" //OutputPath:test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/self-consistency-safe.xml
sqlcmd -S localhost -E -C -Q "DROP DATABASE IgnixaPhaseCFixtureProbe"
```
(Use `//OutputPath:` with a doubled leading slash if running from Git Bash — a single `/OutputPath:` gets mangled by MSYS path translation into a bogus argument; confirm the resulting XML file is non-empty and well-formed before proceeding.) This fixture must classify as **safe** — it contains only Category B/C/D operations (no Category A/E, confirmed empirically during this plan's own research: Category E only appears when comparing against an EF-migration-populated database, never in a pure self-consistency comparison).

- [ ] **Step 2: Write a synthetic destructive fixture (expected: unsafe)**

Create `test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/synthetic-destructive-drop.xml` by hand, modeled on the real schema captured in Step 1 but with an injected, clearly-destructive operation not on any allow-list:
```xml
<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations><Operation Name="Drop"><Item Value="[dbo].[SomeColumn]" Type="SqlSimpleColumn" /></Operation></Operations></DeploymentReport>
```

- [ ] **Step 3: Write a synthetic Category-E-shaped fixture (expected: safe)**

Create `test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/synthetic-category-e.xml` (Category E doesn't occur naturally in a self-consistency comparison per Step 1's note, so this must be hand-authored to prove the classifier still handles it correctly if it ever appears):
```xml
<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations><Operation Name="Drop"><Item Value="unnamed constraint on [dbo].[PackageResource]" Type="SqlDefaultConstraint" /></Operation><Operation Name="Create"><Item Value="Default Constraint: unnamed constraint on [dbo].[PackageResource]" Type="SqlDefaultConstraint" /></Operation></Operations></DeploymentReport>
```

- [ ] **Step 4: Write the failing tests**

Create `test/Ignixa.DataLayer.SqlServer.Tests/DeployReportClassifierTests.cs`:
```csharp
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class DeployReportClassifierTests
{
    private static string ReadFixture(string fileName)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    [Fact]
    public void GivenASelfConsistencyReport_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("self-consistency-safe.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenAReportWithAnUnrecognizedColumnDrop_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-drop.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }

    [Fact]
    public void GivenACategoryEShapedDefaultConstraintDiff_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-category-e.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenAnEmptyReport_WhenClassified_ThenIsAutoSafe()
    {
        const string xml = """<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations /></DeploymentReport>""";
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }
}
```
Ensure `Fixtures/*.xml` files are set to copy to the test output directory — add to `test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj`:
```xml
<ItemGroup>
  <None Include="Fixtures\*.xml" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Run the tests to verify they fail**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~DeployReportClassifierTests
```
Expected: FAIL — `DeployReportClassifier` doesn't exist yet.

- [ ] **Step 6: Write `DeployReportClassifier`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/DeployReportClassifier.cs`:
```csharp
using System.Xml.Linq;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Classifies a SqlPackage/DacFx DeployReport as safe to auto-apply unattended, or not.
/// Create and Refresh operations are never destructive by construction (Create fails loudly
/// at deploy time rather than silently corrupting; Refresh only recompiles a procedure's
/// schema binding). Drop/Alter/TableRebuild/UnbindTable operations must match an explicit
/// allow-list, seeded from Phase B's own proven-benign DeployReport findings (Categories
/// B/C/D/E -- see docs/superpowers/plans/2026-07-19-ignixa-datalayer-sqlserver-phase-c.md's
/// Global Constraints for the full rationale behind each entry).
/// </summary>
public static class DeployReportClassifier
{
    private static readonly XNamespace ReportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly string[] NeverDestructiveOperations = ["Create", "Refresh"];

    public static bool IsAutoSafe(string deployReportXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployReportXml);

        var document = XDocument.Parse(deployReportXml);
        var operations = document.Root?.Element(ReportNamespace + "Operations")?.Elements(ReportNamespace + "Operation")
            ?? [];

        foreach (var operation in operations)
        {
            var operationName = operation.Attribute("Name")?.Value ?? string.Empty;
            if (NeverDestructiveOperations.Contains(operationName))
            {
                continue;
            }

            foreach (var item in operation.Elements(ReportNamespace + "Item"))
            {
                var type = item.Attribute("Type")?.Value ?? string.Empty;
                var value = item.Attribute("Value")?.Value ?? string.Empty;

                if (!IsAllowListed(type, value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsAllowListed(string type, string value)
    {
        // Category B/E: default-value canonicalization noise -- never destructive to
        // existing rows, only affects future inserts.
        if (type == "SqlDefaultConstraint")
        {
            return true;
        }

        // Category C: hex-literal check-constraint canonicalization, proven specific to
        // this one constraint -- narrow, name-matched.
        if (type == "SqlCheckConstraint" && value.Contains("CH_Resource_RawResource_Length", StringComparison.Ordinal))
        {
            return true;
        }

        // Category D: the partition-function/scheme rebuild the post-deployment script's
        // imperative splitting causes on every non-empty-target comparison.
        if ((type == "SqlPartitionScheme" || type == "SqlPartitionFunction")
            && (value.Contains("PartitionScheme_ResourceChangeData_Timestamp", StringComparison.Ordinal)
                || value.Contains("PartitionFunction_ResourceChangeData_Timestamp", StringComparison.Ordinal)))
        {
            return true;
        }

        if (type == "SqlTable" && value.Contains("[dbo].[ResourceChangeData]", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~DeployReportClassifierTests
```
Expected: 4/4 passing.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/DeployReportClassifier.cs test/Ignixa.DataLayer.SqlServer.Tests/DeployReportClassifierTests.cs test/Ignixa.DataLayer.SqlServer.Tests/Fixtures test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj
git commit -m "feat(datalayer-sqlserver): add DeployReportClassifier for safe/unsafe upgrade diff classification"
```

---

### Task 4: `ISchemaVersionResolver`

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaVersionResolver.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionResolver.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs` (register the new resolver)
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SchemaVersionResolverTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaVersionResolverTests.cs`

**Interfaces:**
- Consumes: `SqlExecutionService.ResolveConnectionStringAsync` (Phase B, static/internal, already shared).
- Produces: `public interface ISchemaVersionResolver { Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken); }`, consumed by Task 5's `SchemaDeployer` upgrade path.

- [ ] **Step 1: Write `ISchemaVersionResolver`**

```csharp
namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Reads a tenant's currently-applied schema version. This is the version-gating
/// primitive -- Phase D/E's future version-dependent read/write code will call this to
/// decide which SQL shape to use for a given tenant. No real caller exists yet.
/// </summary>
public interface ISchemaVersionResolver
{
    /// <summary>Returns the tenant's currently-applied schema version, or 0 if untracked
    /// (a pre-Phase-C tenant that predates the SchemaVersion table).</summary>
    Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the unit tests (connection-resolution failure paths, no live DB needed)**

Create `test/Ignixa.DataLayer.SqlServer.Tests/SchemaVersionResolverTests.cs`, reusing the `FakeTenantConfigurationStore` pattern already established in `SqlExecutionServiceConnectionTests.cs`/`SchemaDeployerConnectionTests.cs`:
```csharp
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SchemaVersionResolverTests
{
    [Fact]
    public async Task GivenANonexistentTenant_WhenGetCurrentVersionAsyncCalled_ThenThrowsWithTenantMessage()
    {
        var store = new FakeTenantConfigurationStore(); // no tenant 999, matching the established fake
        var resolver = new SchemaVersionResolver(store, NullLogger<SchemaVersionResolver>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => resolver.GetCurrentVersionAsync(999, CancellationToken.None));

        ex.Message.ShouldBe("Tenant 999 does not exist or is inactive.");
    }
}
```
(`FakeTenantConfigurationStore` already exists in this test project from Phase B — re-read `SchemaDeployerConnectionTests.cs` for its exact constructor/API before writing this test, don't guess its shape.)

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~SchemaVersionResolverTests
```
Expected: FAIL — `SchemaVersionResolver` doesn't exist yet.

- [ ] **Step 4: Write `SchemaVersionResolver`**

```csharp
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SchemaVersionResolver : ISchemaVersionResolver
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SchemaVersionResolver> _logger;

    public SchemaVersionResolver(ITenantConfigurationStore tenantConfigurationStore, ILogger<SchemaVersionResolver> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _logger = logger;
    }

    public async Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var version = (int)result!;
        _logger.LogDebug("Tenant {TenantId}'s current schema version is {Version}.", tenantId, version);
        return version;
    }
}
```
(`ISNULL(MAX(Version), 0)` handles a pre-Phase-C tenant whose `SchemaVersion` table exists — deployed by this same dacpac once Task 5 upgrades it — but has no rows yet at the exact moment of the very first version check, before Task 5's upgrade stamps it. Returns `0`, correctly signaling "behind `CurrentVersion`.")

- [ ] **Step 5: Register the resolver**

In `ServiceCollectionExtensions.cs`, extend `AddIgnixaSqlServerSchemaDeployment`:
```csharp
        services.AddSingleton<ISchemaVersionResolver, SchemaVersionResolver>();
```
(Added alongside the existing `services.AddSingleton<ISchemaDeployer, SchemaDeployer>();` line — same method, same registration style.)

- [ ] **Step 6: Run the unit test to verify it passes**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~SchemaVersionResolverTests
```
Expected: PASS.

- [ ] **Step 7: Write and run an integration test against a real database**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaVersionResolverTests.cs`:
```csharp
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SchemaVersionResolverTests
{
    [Fact]
    public async Task GivenATenantWithAStampedVersion_WhenGetCurrentVersionAsyncCalled_ThenReturnsIt()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING is not set. Run the docker-compose.test.yml SQL Server " +
                "container and set this environment variable before running integration tests.");

        // Reuses the same tenant-fake/dacpac-deploy pattern already established in
        // SchemaDeployerDeploymentTests.cs -- deploy a fresh database via SchemaDeployer
        // (which now stamps SchemaVersion per Task 1), then confirm the resolver reads it back.
        // [Full setup: create a unique database, deploy via SchemaDeployer.DeployIfEmptyAsync,
        // construct SchemaVersionResolver against the same FakeTenantConfigurationStore, assert
        // GetCurrentVersionAsync returns SchemaVersionConstants.CurrentVersion, tear down.]
    }
}
```
Fill in the full test body following `SchemaDeployerDeploymentTests.cs`'s exact real-database setup/teardown pattern (unique database name, `FakeTenantConfigurationStore` pointed at it, `CREATE DATABASE`/`DROP DATABASE` in `finally`) — do not leave this as a placeholder; the implementer writes the real, complete test body using the sibling file as the concrete template.

```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SchemaVersionResolverTests
```
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaVersionResolver.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionResolver.cs src/DataLayer/Ignixa.DataLayer.SqlServer/ServiceCollectionExtensions.cs test/Ignixa.DataLayer.SqlServer.Tests/SchemaVersionResolverTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaVersionResolverTests.cs
git commit -m "feat(datalayer-sqlserver): add ISchemaVersionResolver"
```

---

### Task 5: `SchemaDeployer`'s existing-database upgrade path

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaDeployer.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SchemaDeployerUpgradeTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs`

**Interfaces:**
- Consumes: `ISchemaVersionResolver.GetCurrentVersionAsync` (Task 4), `DeployReportClassifier.IsAutoSafe` (Task 3), `DacServices.GenerateDeployReport` (confirmed real API, Global Constraints), `SchemaVersionConstants.CurrentVersion` (Task 1).
- Produces: `ISchemaDeployer.UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken)`, consumed by Task 7's wiring into `SqlEntityFrameworkRepositoryFactory`. Also consumed directly by Task 6's CLI tool for the "show me the diff" half of the operator flow (the CLI reuses `DeployReportClassifier`/`GenerateDeployReport` directly, not `UpgradeIfNeededAsync` itself, since the CLI's whole purpose is to apply a diff this method would refuse).

- [ ] **Step 1: Extend `ISchemaDeployer`**

```csharp
namespace Ignixa.DataLayer.SqlServer;

public interface ISchemaDeployer
{
    Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Upgrades a tenant's existing, non-empty database to the current schema version if it's
    /// behind and the pending diff is provably safe to auto-apply (no operations outside
    /// DeployReportClassifier's allow-list). Throws if the tenant is behind and the diff is
    /// NOT auto-safe -- the caller must use the operator-triggered CLI path instead. No-ops if
    /// the tenant is already current.
    /// </summary>
    Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the failing unit test (toggle-disabled path, no live DB needed)**

Create `test/Ignixa.DataLayer.SqlServer.Tests/SchemaDeployerUpgradeTests.cs`, following `SchemaDeployerConnectionTests.cs`'s established fake pattern:
```csharp
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SchemaDeployerUpgradeTests
{
    [Fact]
    public async Task GivenANonexistentTenant_WhenUpgradeIfNeededAsyncCalled_ThenThrowsWithTenantMessage()
    {
        var store = new FakeTenantConfigurationStore(); // no tenant 999
        var deployer = new SchemaDeployer(
            store,
            new FakeHostEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            new ThrowingSchemaVersionResolver(), // never reached -- ResolveConnectionStringAsync throws first
            NullLogger<SchemaDeployer>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.UpgradeIfNeededAsync(999, CancellationToken.None));

        ex.Message.ShouldBe("Tenant 999 does not exist or is inactive.");
    }

    private sealed class ThrowingSchemaVersionResolver : ISchemaVersionResolver
    {
        public Task<int> GetCurrentVersionAsync(int tenantId, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Not expected to be called in this test.");
    }
}
```
(`FakeHostEnvironment` and `FakeTenantConfigurationStore` already exist in this test project from Phase A/B — reuse them, don't redefine.)

- [ ] **Step 3: Run the test to verify it fails**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~SchemaDeployerUpgradeTests
```
Expected: FAIL — `UpgradeIfNeededAsync` doesn't exist yet.

- [ ] **Step 4: Implement `UpgradeIfNeededAsync`**

In `SchemaDeployer.cs`, add the resolver as a constructor dependency (this changes the constructor signature — update every existing caller, including `ServiceCollectionExtensions.cs`'s DI registration, which resolves it automatically, and any test that constructs `SchemaDeployer` directly):
```csharp
    private readonly ISchemaVersionResolver _schemaVersionResolver;

    public SchemaDeployer(
        ITenantConfigurationStore tenantConfigurationStore,
        IHostEnvironment environment,
        IOptions<SqlServerOptions> options,
        ISchemaVersionResolver schemaVersionResolver,
        ILogger<SchemaDeployer> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaVersionResolver);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _environment = environment;
        _options = options;
        _schemaVersionResolver = schemaVersionResolver;
        _logger = logger;
    }
```
Add the new method:
```csharp
    public async Task UpgradeIfNeededAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (await IsDatabaseEmptyAsync(connectionString, cancellationToken))
        {
            // Nothing to upgrade -- an empty database is DeployIfEmptyAsync's job, not this one.
            return;
        }

        var currentVersion = await _schemaVersionResolver.GetCurrentVersionAsync(tenantId, cancellationToken);
        if (currentVersion >= SchemaVersionConstants.CurrentVersion)
        {
            _logger.LogDebug("Tenant {TenantId} is already at schema version {Version}.", tenantId, currentVersion);
            return;
        }

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream(DacpacResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DacpacResourceName}' not found in {typeof(SchemaDeployer).Assembly.FullName}.");
        using var package = DacPackage.Load(dacpacStream);
        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);

        var deployReportXml = dacServices.GenerateDeployReport(package, databaseName, cancellationToken: cancellationToken);

        if (!DeployReportClassifier.IsAutoSafe(deployReportXml))
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is at schema version {currentVersion}, behind the current " +
                $"version {SchemaVersionConstants.CurrentVersion}, and the pending diff contains changes " +
                "that are not safe to apply automatically. Review the diff and apply it explicitly using " +
                "the schema-upgrade CLI tool (tools/Ignixa.SchemaUpgrade.Cli).");
        }

        if (!_options.Value.AutomaticSchemaDeploymentEnabled)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is behind schema version {SchemaVersionConstants.CurrentVersion} " +
                $"and {SqlServerOptions.SectionName}:{nameof(SqlServerOptions.AutomaticSchemaDeploymentEnabled)} is false. " +
                "Apply the upgrade manually using the schema-upgrade CLI tool, or enable automatic deployment.");
        }

        dacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken: cancellationToken);
        await StampSchemaVersionAsync(connectionString, SchemaVersionConstants.CurrentVersion, cancellationToken);
        _logger.LogInformation(
            "Upgraded tenant {TenantId}'s database from schema version {OldVersion} to {NewVersion}.",
            tenantId, currentVersion, SchemaVersionConstants.CurrentVersion);
    }
```
(The classifier check runs *before* the toggle check, deliberately — an unsafe diff must always be refused regardless of the toggle, since the toggle only ever governs whether *safe* changes apply unattended, matching the design doc's expand-only-auto-upgrade decision. `StampSchemaVersionAsync` is Task 1's helper, reused as-is.)

- [ ] **Step 5: Update the DI registration and existing test constructors**

`ServiceCollectionExtensions.cs`'s `services.AddSingleton<ISchemaDeployer, SchemaDeployer>()` resolves the new constructor parameter automatically (DI container), no change needed there beyond Task 4 Step 5's `ISchemaVersionResolver` registration already being in place. Update every existing test that constructs `SchemaDeployer` directly (`SchemaDeployerConnectionTests.cs`, `SchemaDeployerDeploymentTests.cs` — grep for `new SchemaDeployer(` across the test projects) to pass a fake or real `ISchemaVersionResolver` in the new constructor position.

- [ ] **Step 6: Run the unit test to verify it passes**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~SchemaDeployerUpgradeTests
```
Expected: PASS. Also re-run the full unit suite to confirm the constructor change didn't break existing tests:
```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests
```
Expected: all passing.

- [ ] **Step 7: Write and run the already-current integration test**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs`:
```csharp
    [Fact]
    public async Task GivenATenantAlreadyAtCurrentVersion_WhenUpgradeIfNeededAsyncCalled_ThenDoesNothing()
    {
        // Deploy fresh via DeployIfEmptyAsync (stamps CurrentVersion per Task 1), then call
        // UpgradeIfNeededAsync -- assert it returns without throwing and without modifying
        // sys.tables/SchemaVersion row count.
    }
```
Write the complete test body (unique database, `DeployIfEmptyAsync` first, then `UpgradeIfNeededAsync`, exact `sys.tables` count and `SchemaVersion` row-count assertions before/after, teardown in `finally` — matching every other integration test's discipline in this plan). This is the only `SchemaDeployerUpgradeTests` scenario Task 5 owns — the "tenant on an older real schema" scenario needs a genuinely older schema fixture, which only exists starting in Task 8; Task 8 adds its test to this same file rather than this task attempting a premature, throwaway version of it.

```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SchemaDeployerUpgradeTests
```
Expected: the already-current test passes now; the older-version test passes once its fixture exists (Task 5 or Task 8, per the coordination note above).

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaDeployer.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs test/Ignixa.DataLayer.SqlServer.Tests/SchemaDeployerUpgradeTests.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs
git commit -m "feat(datalayer-sqlserver): add SchemaDeployer.UpgradeIfNeededAsync for existing-database upgrades"
```

---

### Task 6: Operator-triggered upgrade CLI

**Files:**
- Create: `tools/Ignixa.SchemaUpgrade.Cli/Ignixa.SchemaUpgrade.Cli.csproj`
- Create: `tools/Ignixa.SchemaUpgrade.Cli/Program.cs`
- Test: `test/Ignixa.SchemaUpgrade.Cli.Tests/` (new project, matching this repo's `tools/*.Cli` + `test/*.Cli.Tests` pairing convention)
- Modify: `All.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: `DeployReportClassifier.IsAutoSafe` (Task 3, for display purposes — shows the operator whether the auto-path would have refused this, though the CLI applies regardless of classification once the operator explicitly confirms), `SqlExecutionService.ResolveConnectionStringAsync` (Phase B), `DacServices.GenerateDeployReport`/`Deploy` (DacFx).
- Produces: a standalone tool, not consumed by any other task in this plan — this is the terminal node of the operator path `SchemaDeployer.UpgradeIfNeededAsync` (Task 5) points at in its refusal error message.

- [ ] **Step 1: Scaffold the project**

Follow `tools/Ignixa.SqlOnFhir.Cli`'s established shape (`System.CommandLine`, `PackAsTool=true`, `net10.0`) — read that project's `.csproj` directly and copy its structure, adding a project reference to `Ignixa.DataLayer.SqlServer` and `Ignixa.Application` (for `ITenantConfigurationStore`'s real implementation, `AppSettingsTenantConfigurationStore`, and standalone `IConfiguration` loading).

```
dotnet sln All.sln add tools/Ignixa.SchemaUpgrade.Cli/Ignixa.SchemaUpgrade.Cli.csproj
```

- [ ] **Step 2: Write `Program.cs`**

```csharp
using System.CommandLine;
using Ignixa.DataLayer.SqlServer;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SqlServer.Dac;

var tenantIdOption = new Option<int>("--tenant-id") { Required = true, Description = "The tenant ID to upgrade." };
var confirmOption = new Option<bool>("--confirm") { Description = "Apply the upgrade without an interactive prompt (for scripted/CI use)." };

var rootCommand = new RootCommand("Reviews and applies a pending schema upgrade for a tenant database that SchemaDeployer's automatic path refused.");
rootCommand.Options.Add(tenantIdOption);
rootCommand.Options.Add(confirmOption);

rootCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var tenantId = parseResult.GetValue(tenantIdOption);
    var autoConfirm = parseResult.GetValue(confirmOption);

    var configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    ITenantConfigurationStore tenantConfigurationStore = new AppSettingsTenantConfigurationStore(configuration);
    var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(tenantConfigurationStore, tenantId, cancellationToken);

    using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream("Ignixa.DataLayer.SqlServer.Schema.dacpac")
        ?? throw new InvalidOperationException("Embedded schema dacpac not found.");
    using var package = DacPackage.Load(dacpacStream);
    var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    var dacServices = new DacServices(connectionString);

    var deployReportXml = dacServices.GenerateDeployReport(package, databaseName, cancellationToken: cancellationToken);
    Console.WriteLine($"Pending schema diff for tenant {tenantId} ({databaseName}):");
    Console.WriteLine(deployReportXml);
    Console.WriteLine();
    Console.WriteLine(DeployReportClassifier.IsAutoSafe(deployReportXml)
        ? "This diff IS classified as auto-safe -- SchemaDeployer's automatic path should have applied it. Applying it here anyway is redundant but harmless."
        : "This diff is NOT classified as auto-safe -- it contains operations outside the known-benign allow-list. Review the XML above carefully before proceeding.");

    if (!autoConfirm)
    {
        Console.Write("Apply this diff? [y/N] ");
        var response = Console.ReadLine();
        if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted, nothing was applied.");
            return 1;
        }
    }

    dacServices.Deploy(package, databaseName, upgradeExisting: true, cancellationToken: cancellationToken);
    Console.WriteLine($"Applied. Tenant {tenantId}'s database is now on the current schema.");
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();
```
(Re-derive the exact `System.CommandLine` API shape — `Option<T>`, `RootCommand.SetAction`, `ParseResult.GetValue`, `.Parse(args).InvokeAsync()` — from `tools/Ignixa.SqlOnFhir.Cli/Program.cs`'s real usage before finalizing this file; `System.CommandLine`'s API has shifted across preview versions and this repo's actual installed version is the ground truth, not this plan's best-effort transcription. Also confirm `AppSettingsTenantConfigurationStore`'s real constructor signature by reading `src/Application/Ignixa.Application/Infrastructure/AppSettingsTenantConfigurationStore.cs` directly — the one-argument `(IConfiguration)` form shown above is this plan's best guess, not confirmed.)

Note: this CLI does **not** call `SchemaDeployer.UpgradeIfNeededAsync` (Task 5) — it deliberately re-implements the "generate report, show it, deploy" sequence directly against `DacServices`, because `UpgradeIfNeededAsync` throws when the diff isn't auto-safe, which is exactly the case this tool exists to handle. Reusing `DeployReportClassifier`/`SqlExecutionService.ResolveConnectionStringAsync` (not `SchemaDeployer` itself) is the correct amount of code reuse here.

- [ ] **Step 3: Write a test project**

Create `test/Ignixa.SchemaUpgrade.Cli.Tests/`, matching `test/Ignixa.SqlOnFhir.Cli.Tests/`'s structure. At minimum, a test confirming `--help` output includes both options and a test confirming the tool exits non-zero when the operator declines the confirmation prompt (simulate via redirected stdin, matching whatever pattern the sibling `*.Cli.Tests` project already uses for interactive-prompt testing — read it first).

- [ ] **Step 4: Build and run**

```
dotnet build All.sln
dotnet test test/Ignixa.SchemaUpgrade.Cli.Tests
```
Expected: 0 warnings/errors, new tests passing.

- [ ] **Step 5: Commit**

```bash
git add tools/Ignixa.SchemaUpgrade.Cli test/Ignixa.SchemaUpgrade.Cli.Tests All.sln
git commit -m "feat(datalayer-sqlserver): add Ignixa.SchemaUpgrade.Cli for operator-reviewed upgrades"
```

---

### Task 7: Wire the upgrade path into the real tenant-connection trigger point

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs`

**Interfaces:**
- Consumes: `ISchemaDeployer.UpgradeIfNeededAsync` (Task 5).
- Produces: nothing new — this is the final wiring step, no later task depends on it.

- [ ] **Step 1: Read the current wiring**

Re-read `SqlEntityFrameworkRepositoryFactory.CreateServiceFactory` (already modified once in Phase B Task 6 to call `_schemaDeployer.DeployIfEmptyAsync(tenantId, CancellationToken.None).GetAwaiter().GetResult()`) — confirm the exact current line/call site before editing.

- [ ] **Step 2: Add the upgrade call alongside the existing empty-deploy call**

```csharp
        _schemaDeployer.DeployIfEmptyAsync(tenantId, CancellationToken.None).GetAwaiter().GetResult();
        _schemaDeployer.UpgradeIfNeededAsync(tenantId, CancellationToken.None).GetAwaiter().GetResult();
```
(Same synchronous-wait pattern already established in Phase B — `CreateServiceFactory` isn't async, matching the existing, already-reviewed convention; not this task's place to change that.) `DeployIfEmptyAsync` already no-ops if the database isn't empty (Phase B), and `UpgradeIfNeededAsync` already no-ops if the database IS empty (Task 5, Step 4) — the two calls are mutually exclusive in effect for any given tenant/call, safe to run back-to-back unconditionally.

- [ ] **Step 3: Full solution build and test**

```
dotnet build All.sln
```
Expected: 0 warnings, 0 errors.
```
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```
(`TEST_SQL_CONNECTION_STRING=Server=localhost;Trusted_Connection=True;TrustServerCertificate=True`) Expected: all passing except the 2 known pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 4: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs
git commit -m "feat(datalayer-sqlserver): wire SchemaDeployer.UpgradeIfNeededAsync into the tenant-factory trigger point"
```

---

### Task 8: Real "N versions behind" integration test, destructive-diff refusal test, and final verification

**Files:**
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs` (fill in the deferred test from Task 5, plus a new one)

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: the actual proof this phase's mechanism works end-to-end against a real "behind" tenant, not just against synthetic fixtures.

- [ ] **Step 1: Build an older dacpac from git history**

The current dacpac has always been at `SchemaVersionConstants.CurrentVersion = 1` (this phase's own first version) — there is no genuinely *older* real dacpac in this branch's history to upgrade *from* using the real version-tracking mechanism yet, since Task 1 introduces versioning for the first time. Use Phase B's pre-Task-9 state instead, purely as a structurally-different "older schema" stand-in (missing the terminology tables Task 9 of Phase B added) to prove the upgrade *mechanism* works against a real schema gap — not to prove a real version-1-to-version-2 transition, which doesn't exist yet:
```
git show 0db642e3:src/DataLayer/Ignixa.DataLayer.SqlServer.Database > /tmp/old-sqlproj-tree-manifest  # (illustrative; see below for the real approach)
```
Concretely: check out the whole `Ignixa.DataLayer.SqlServer.Database` directory as it existed at commit `0db642e3` (Phase B, before Task 9's terminology tables) into a scratch location, build it there as the "old" dacpac:
```
git worktree add /tmp/ignixa-phase-c-old-schema 0db642e3
dotnet build /tmp/ignixa-phase-c-old-schema/src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
```
(Use a real OS temp directory, not this session's Windows scratchpad path, since `git worktree add` needs a location outside the current worktree's own tracked tree.)

- [ ] **Step 2: Deploy the old dacpac to a test database, stamp it manually at a "version 0" state**

```csharp
    [Fact]
    public async Task GivenATenantOnAnOlderRealSchema_WhenUpgradeIfNeededAsyncCalled_ThenUpgradesToCurrentAndStampsTheVersion()
    {
        // Deploy the OLD dacpac (built from commit 0db642e3, missing Term* tables) to a fresh
        // database -- this database now genuinely lacks TermCodeSystem/TermConcept/etc, a real
        // structural gap versus the current dacpac, not a synthetic fixture.
        //
        // The old dacpac's deploy does NOT create a SchemaVersion table at all (it predates
        // Task 1) -- confirm SchemaVersionResolver.GetCurrentVersionAsync correctly returns 0
        // for this tenant (no SchemaVersion table exists yet -- adjust
        // SchemaVersionResolver.GetCurrentVersionAsync's query to tolerate "table doesn't
        // exist" as equivalent to "version 0", via a sys.tables existence check before the
        // SELECT, if Task 4's implementation doesn't already handle this -- an un-versioned
        // pre-Phase-C tenant is exactly the scenario this whole phase must handle gracefully).
        //
        // Call SchemaDeployer.UpgradeIfNeededAsync against this database. Assert: it does not
        // throw (the diff should classify as auto-safe -- it's pure net-new tables/columns,
        // no drops); afterward sys.tables includes TermCodeSystem etc.; SchemaVersion now has
        // exactly one row at SchemaVersionConstants.CurrentVersion.
    }
```
Write the complete test body — real `CREATE DATABASE`, real deploy of the scratch old dacpac (reference its built path under `/tmp/ignixa-phase-c-old-schema/.../bin/Release/...dacpac`), real `SchemaDeployer` construction, real assertions on `sys.tables` and `SchemaVersion` contents, real teardown in a `finally` block — matching every other integration test's discipline in this plan and in Phase B.

**Important finding to confirm, not assume**: does `SchemaVersionResolver.GetCurrentVersionAsync`'s `SELECT ISNULL(MAX(Version), 0) FROM dbo.SchemaVersion` query (Task 4) actually succeed against a database where the `SchemaVersion` table doesn't exist at all (the old-dacpac case), or does it throw `SqlException` "Invalid object name 'dbo.SchemaVersion'"? If it throws, `SchemaVersionResolver` needs a `sys.tables` existence check first (mirroring `SchemaDeployer.IsDatabaseEmptyAsync`'s own pattern) — fix this now if the test reveals it, in `SchemaVersionResolver.cs`, with an added unit test in `SchemaVersionResolverTests.cs` covering the missing-table case explicitly.

- [ ] **Step 3: Write the destructive-diff refusal integration test**

Concrete mechanism (no second dacpac build needed — uses the real, unmodified current dacpac both as what `UpgradeIfNeededAsync` embeds and as the comparison source): deploy the current dacpac to a fresh test database via `DeployIfEmptyAsync` (giving it the exact schema the dacpac declares), then execute raw SQL directly against that live database to add a column the dacpac's model does **not** know about — e.g. `ALTER TABLE dbo.BackgroundJobs ADD ExtraTestColumn INT NULL`. The live database now has *more* than the dacpac declares. When `UpgradeIfNeededAsync` (which always loads the real embedded dacpac) generates a `DeployReport` against this diverged database, DacFx proposes **dropping** `ExtraTestColumn` (a genuine `Drop` operation, `Type="SqlSimpleColumn"`, not on any allow-list entry) to reconcile the live database back to the dacpac's declared shape — exactly a real, unambiguous destructive operation, produced without needing to build or swap in a second dacpac anywhere.

```csharp
    [Fact]
    public async Task GivenATenantWithAGenuinelyDestructiveDiffPending_WhenUpgradeIfNeededAsyncCalled_ThenThrowsAndDoesNotModifySchema()
    {
        // 1. Create a fresh database, call DeployIfEmptyAsync (gives it the current dacpac's
        //    real schema, stamps SchemaVersion at SchemaVersionConstants.CurrentVersion per Task 1).
        // 2. Execute raw SQL directly against the database:
        //    "ALTER TABLE dbo.BackgroundJobs ADD ExtraTestColumn INT NULL" -- this column exists
        //    live but is NOT in the dacpac's declared model.
        // 3. Manually UPDATE dbo.SchemaVersion to a version below SchemaVersionConstants.CurrentVersion
        //    (e.g. 0) so UpgradeIfNeededAsync actually attempts the diff instead of no-op'ing.
        // 4. Call UpgradeIfNeededAsync. Assert it throws InvalidOperationException whose message
        //    mentions "tools/Ignixa.SchemaUpgrade.Cli" (matching Task 5's exact refusal message).
        // 5. Confirm via a direct follow-up query (SELECT 1 FROM sys.columns WHERE object_id =
        //    OBJECT_ID('dbo.BackgroundJobs') AND name = 'ExtraTestColumn') that ExtraTestColumn
        //    STILL EXISTS -- proving the refused diff was never actually applied, not just that
        //    the method threw.
    }
```
Write the complete test body (unique database, exact SQL/assertions as outlined above, teardown in `finally`).

- [ ] **Step 4: Clean up the scratch worktree**

```
git worktree remove /tmp/ignixa-phase-c-old-schema
```

- [ ] **Step 5: Run the full test suite**

```
dotnet build All.sln
```
Set `TEST_SQL_CONNECTION_STRING=Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` and:
```
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```
Expected: all passing except the 2 known pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 6: Commit**

```bash
git add test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerUpgradeTests.cs test/Ignixa.DataLayer.SqlServer.Tests/SchemaVersionResolverTests.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaVersionResolver.cs
git commit -m "test(datalayer-sqlserver): prove the upgrade path end-to-end against a real older schema and a genuine destructive-diff refusal"
```

---

### Task 9: Generalize `DeployReportClassifier` using the `DataIssue` alert signal

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/DeployReportClassifier.cs`
- Modify: `test/Ignixa.DataLayer.SqlServer.Tests/DeployReportClassifierTests.cs`

**Interfaces:**
- Consumes: nothing new — this is a pure internal redesign of `DeployReportClassifier.IsAutoSafe(string deployReportXml)`. Its public signature is unchanged; every existing caller (`SchemaDeployer.UpgradeIfNeededAsync`, `tools/Ignixa.SchemaUpgrade.Cli`) needs no changes.
- Produces: the same `IsAutoSafe` method, now classifying via a general signal instead of a growing per-table allow-list.

**Why this task exists**: Task 8's real older-schema test found that `DeployReportClassifier`'s per-category, name-matched allow-list (Categories B through F) doesn't scale — every future migration that touches a not-yet-allow-listed table needs a new hand-added entry, discovered only when someone happens to run a real diff against it. While investigating, Task 8's implementer found a more general, already-present signal: SqlPackage's `DeployReport` XML marks a genuinely destructive operation with a child `<Issue>` element on the affected `<Item>`, cross-referencing an `<Alert Name="DataIssue">` entry in the report's `<Alerts>` section — and a purely-additive change (like the `PackageResource` column-add Category F was hand-added for) carries no such `<Issue>` child at all. The user chose to adopt this as the classifier's general safety rule now, retiring the per-table entries.

**Ground truth, verified directly against this repo's real dacpac** (not assumed — generated fresh during this plan's own writing, following the same discipline Task 3 established): a genuinely destructive diff (an undeclared `BackgroundJobs.ExtraTestColumn` column, forcing DacFx to propose dropping it) produces:
```xml
<Alerts><Alert Name="DataMotion"><Issue Value="[dbo].[ResourceChangeData]" /></Alert><Alert Name="DataIssue"><Issue Value="The column [dbo].[BackgroundJobs].[ExtraTestColumn] is being dropped, data loss could occur." Id="1" /></Alert></Alerts><Operations>...<Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Item></Operation>...</Operations>
```
Note two distinct alert shapes: `DataMotion`'s `<Issue Value="..." />` has no `Id` and never cross-references into any `<Operation>`'s `<Item>` — it's purely informational, unrelated to destructiveness. `DataIssue`'s `<Issue Value="..." Id="1" />` DOES have an `Id`, and the `<Item>` that triggered it carries a matching child `<Issue Id="1" />` (no `Value`, just the reference). This means the classifier does **not** need to parse or cross-reference the `<Alerts>` section at all — checking whether an `<Item>` has **any** child `<Issue>` element is sufficient and simpler: in every real report generated so far (self-consistency comparisons, the real `0db642e3`→current diff, and this destructive case), only `DataIssue`-flagged items ever carry a child `<Issue>` element; `DataMotion` never does, and non-flagged items never do.

- [ ] **Step 1: Independently re-verify the signal holds for the known benign cases, not just the one already documented**

Before removing any existing logic, confirm empirically that none of the previously-allow-listed categories' `Item`s ever carry a child `<Issue>` element:
```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
```
Regenerate the self-consistency report (Categories B/C/D — deploy the current dacpac fresh to a scratch database, `DeployReport` that same dacpac against that same database) and grep the output for `<Issue` — expect matches only inside `<Alerts>`, never inside `<Operations>...<Item>`. Then regenerate the real `0db642e3`→current diff using the committed fixture (`test/Ignixa.DataLayer.SqlServer.IntegrationTests/Fixtures/phase-b-pre-task9-schema.dacpac` — deploy it fresh to a scratch database, `DeployReport` the *current* dacpac against it) and confirm the same: the `PackageResource` `Alter` `Item` (Category F's case) has no child `<Issue>` element, even though the overall report's `<Alerts>` may contain a `DataMotion` entry. If either check finds a counter-example (a benign category's `Item` DOES carry an `<Issue>` child), stop and report it as a real finding — do not proceed with the redesign until it's understood.

- [ ] **Step 2: Rewrite `DeployReportClassifier`**

Replace `IsAllowListed` and its per-category logic entirely:
```csharp
using System.Xml.Linq;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Classifies a SqlPackage/DacFx DeployReport as safe to auto-apply unattended, or not.
/// Create and Refresh operations are never destructive by construction (Create fails loudly
/// at deploy time rather than silently corrupting; Refresh only recompiles a procedure's
/// schema binding). For every other operation, an Item is unsafe if and only if SqlPackage's
/// own comparison engine flagged it with a child &lt;Issue&gt; element -- this is the same signal
/// DacFx uses internally to raise a DataIssue alert (e.g. "this column is being dropped, data
/// loss could occur"). A purely additive change (a new nullable column, a canonicalization-only
/// default/check-constraint rewrite, the partition-rebuild cascade Script.PostDeployment.sql's
/// imperative splitting causes) never carries this marker. Verified directly against this
/// project's real DeployReport XML -- see docs/superpowers/plans/2026-07-19-ignixa-datalayer-sqlserver-phase-c.md
/// Task 9 for the captured example and the reasoning. This replaces an earlier, narrower design
/// (a hand-maintained allow-list of known-benign object type/name patterns, "Categories B
/// through F") that needed a new entry every time a migration touched a not-yet-seen table --
/// the DataIssue-alert signal is general and needs no future entries.
/// </summary>
public static class DeployReportClassifier
{
    private static readonly XNamespace ReportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";

    private static readonly string[] NeverDestructiveOperations = ["Create", "Refresh"];

    public static bool IsAutoSafe(string deployReportXml)
    {
        ArgumentException.ThrowIfNullOrEmpty(deployReportXml);

        var document = XDocument.Parse(deployReportXml);
        var operations = document.Root?.Element(ReportNamespace + "Operations")?.Elements(ReportNamespace + "Operation")
            ?? [];

        foreach (var operation in operations)
        {
            var operationName = operation.Attribute("Name")?.Value ?? string.Empty;
            if (NeverDestructiveOperations.Contains(operationName))
            {
                continue;
            }

            foreach (var item in operation.Elements(ReportNamespace + "Item"))
            {
                if (item.Element(ReportNamespace + "Issue") is not null)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
```

- [ ] **Step 3: Regenerate the test fixtures to match the general mechanism**

The existing `synthetic-category-e.xml` and `synthetic-category-f.xml` fixtures were hand-authored to exercise the old per-category matching (`Type="SqlDefaultConstraint"`, `Type="SqlTable" Value="[dbo].[PackageResource]"`) — they remain valid inputs (no child `<Issue>` element, so still correctly classify safe under the new rule), but their names now describe an obsolete category system. Rename them for clarity: `synthetic-category-e.xml` → `synthetic-safe-default-constraint-alter.xml`, `synthetic-category-f.xml` → `synthetic-safe-table-alter-no-issue.xml`. Add one new fixture proving the general mechanism actually works for a case the old allow-list never covered — a purely-additive `Alter` on a **different**, never-before-allow-listed table:
```xml
<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Operations><Operation Name="Alter"><Item Value="[dbo].[SomeFutureTable]" Type="SqlTable" /></Operation></Operations></DeploymentReport>
```
Save as `test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/synthetic-safe-alter-unrecognized-table.xml`. Also add a fixture proving the destructive case still correctly rejects, using the *real* XML shape captured in this task's own "Ground truth" section above (an `Item` with a genuine child `<Issue Id="1" />`), not the old `synthetic-destructive-drop.xml`'s `Drop`-shaped example — save as `test/Ignixa.DataLayer.SqlServer.Tests/Fixtures/synthetic-destructive-alter-with-issue.xml`:
```xml
<?xml version="1.0" encoding="utf-8"?><DeploymentReport xmlns="http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02"><Alerts><Alert Name="DataIssue"><Issue Value="The column [dbo].[BackgroundJobs].[ExtraTestColumn] is being dropped, data loss could occur." Id="1" /></Alert></Alerts><Operations><Operation Name="Alter"><Item Value="[dbo].[BackgroundJobs]" Type="SqlTable"><Issue Id="1" /></Item></Operation></Operations></DeploymentReport>
```
Keep the original `synthetic-destructive-drop.xml` too — it's still a valid unsafe case (a `Drop` operation, distinct code path from `Alter`, worth continued coverage) and needs no change, since a genuine `Drop` with no child `<Issue>` would be a gap worth testing for on its own terms — but check whether a real, uncontrived `Drop` of something genuinely destructive (not on the old allow-list) would actually carry an `<Issue>` child in practice. If you're not certain, note it as a follow-up rather than asserting either way.

- [ ] **Step 4: Update `DeployReportClassifierTests.cs`**

Update the test method bodies to reference the renamed/new fixture files, keeping the same `Given...When...Then...` test names where the scenario is unchanged (the self-consistency-safe and synthetic-destructive-drop tests need no rename), and add:
```csharp
    [Fact]
    public void GivenAnAdditiveAlterOnATableNeverPreviouslyAllowListed_WhenClassified_ThenIsAutoSafe()
    {
        var xml = ReadFixture("synthetic-safe-alter-unrecognized-table.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeTrue();
    }

    [Fact]
    public void GivenARealDataIssueAlertShape_WhenClassified_ThenIsNotAutoSafe()
    {
        var xml = ReadFixture("synthetic-destructive-alter-with-issue.xml");
        DeployReportClassifier.IsAutoSafe(xml).ShouldBeFalse();
    }
```
Remove the old `GivenACategoryEShapedDefaultConstraintDiff...`/`GivenAnAlterReportForAnUnrelatedTable...`-style tests that specifically asserted on the retired per-category matching's exact boundaries (they tested implementation details of a design that no longer exists) — keep only tests that assert on `IsAutoSafe`'s observable behavior.

- [ ] **Step 5: Run the classifier tests**

```
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName~DeployReportClassifierTests
```
Expected: all passing, including the two new tests.

- [ ] **Step 6: Regression-prove against the real Task 8 scenario**

The most important proof this task can offer: confirm the real `0db642e3`→current diff (Task 8's committed fixture) now classifies safe via the *general* mechanism, not the retired `PackageResource`-specific entry. Re-run Task 8's own older-schema integration test:
```
dotnet build All.sln
```
Set `TEST_SQL_CONNECTION_STRING=Server=localhost;Trusted_Connection=True;TrustServerCertificate=True` and:
```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter FullyQualifiedName~SchemaDeployerUpgradeTests
```
Expected: `GivenATenantOnAnOlderRealSchema_WhenUpgradeIfNeededAsyncCalled_ThenUpgradesToCurrentAndStampsTheVersion` still passes — proving the new general classifier correctly handles this real case without any table-specific code. Also re-run the destructive-diff refusal test in the same filter and confirm it still passes (proving the general rule still correctly refuses).

- [ ] **Step 7: Full solution regression**

```
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```
Expected: all passing except the 2 known pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 8: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/DeployReportClassifier.cs test/Ignixa.DataLayer.SqlServer.Tests/DeployReportClassifierTests.cs test/Ignixa.DataLayer.SqlServer.Tests/Fixtures
git commit -m "refactor(datalayer-sqlserver): generalize DeployReportClassifier using DacFx's DataIssue alert signal"
```

---

## Final steps (controller, not a task subagent)

After all 9 tasks are complete and reviewed clean:
1. Full solution build + test (`dotnet build All.sln`, `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`).
2. Generate the final whole-branch review package (`scripts/review-package` against the merge-base with `feature/fhir-to-sql-compiler` — the same merge-base Phase A and Phase B used, since this branch never merged there) and dispatch the final reviewer on the most capable available model.
3. Report the full picture to the user; ask explicitly before merging (this branch has stayed standalone through Phase A and Phase B — confirm with the user rather than assuming Phase C follows the same choice) and again before pushing.
