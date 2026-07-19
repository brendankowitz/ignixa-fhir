# Ignixa.DataLayer.SqlServer Phase B: SQL Database Projects Adoption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Decompose the legacy `97.sql` monolith into a proper SDK-style SQL Database Project (`.sqlproj`), retire EF Core Migrations as the schema-authoring/bootstrap mechanism, and switch new-tenant database bootstrap to an in-process DacFx deploy — with zero actual schema change (verified byte-for-byte via a zero-diff `SqlPackage /Action:DeployReport`).

**Architecture:** A new `Ignixa.DataLayer.SqlServer.Database` project (schema-only, `Microsoft.Build.Sql`) becomes the single source of truth for DDL, replacing both `97.sql` and the 6 hand-written EF migrations. A new `SchemaDeployer` class in `Ignixa.DataLayer.SqlServer` (Phase A's project) deploys that project's `.dacpac` via the DacFx API (`Microsoft.SqlServer.Dac`) to brand-new, empty tenant databases only — existing tenant databases are never auto-touched by this phase (that's Phase C's job). `SqlCatalogGenerator` (a Roslyn source generator) switches from reading one file to reading the new project's `Tables/*.sql` files. `FhirDbContext`'s Fluent API query-mapping model is untouched throughout.

**Tech Stack:** `Microsoft.Build.Sql` SDK-style `.sqlproj`, `Microsoft.SqlPackage` (CLI, build-time/CI verification only), `Microsoft.SqlServer.DacFx` (NuGet package, `Microsoft.SqlServer.Dac` namespace, in-process runtime deploy), PowerShell (decomposition script), the existing `docker-compose.test.yml`/LocalDB pattern for integration tests.

**Full design:** `docs/superpowers/specs/2026-07-19-ignixa-datalayer-sqlserver-phase-b-design.md` — read this first for the *why* and every locked-in decision. Parent design: `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` (§3, §5). Phases C–F (schema-version compatibility, write-path migration, read cutover, retiring `SqlEntityFramework`) are explicitly out of scope for this plan.

## Global Constraints

- This plan runs directly in the git worktree already in use for this initiative: `C:\src\ignixa-fhir\.claude\worktrees\ignixa-datalayer-sqlserver` (branch `worktree-ignixa-datalayer-sqlserver`). No new worktree, no new branch — continues Phase A's branch directly, per explicit user decision. This branch does **not** merge into `feature/fhir-to-sql-compiler`; it stays standalone and gets pushed to origin directly, matching what happened after Phase A.
- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures are known and out of scope, per every prior increment on this branch.
- **The real `97.sql` object inventory** (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`, 6,349 lines, counted directly): 37 `CREATE TABLE` (one, `dbo.CurrentResource` at line 571, is a throwaway — see Task 1), 1 `CREATE VIEW` (`dbo.CurrentResource`, the real permanent object, at line 6343), 59 `CREATE PROCEDURE`, 23 `CREATE TYPE ... AS TABLE`, 1 `CREATE SEQUENCE`, 4 `CREATE PARTITION FUNCTION` + 4 `CREATE PARTITION SCHEME` = **125 top-level `CREATE` statements total**. No object is ever redefined (no duplicate `CREATE`, no `ALTER PROCEDURE`/`ALTER VIEW`/`ALTER FUNCTION`) — every object appears exactly once. 58 standalone `CREATE INDEX` + 20 `ALTER TABLE` statements are interleaved among the tables and belong in each table's own file, not separate files.
- **Verification is automated, not hand-review**: the pass/fail gate for decomposition is `dotnet build` → `.dacpac` → `sqlpackage /Action:DeployReport` showing **zero** differences against a database bootstrapped the old way (raw `97.sql` execution + the 6 EF migrations). Per the user's explicit choice — do not hand-verify object-by-object.
- **`/Action:Publish` never runs unattended in CI** — CI only ever produces a `DeployReport` artifact for human review. The sole exception, elsewhere in this plan, is `SchemaDeployer`'s in-process `DacServices.Deploy` call, which only ever targets a database already confirmed empty (nothing to review, nothing to destroy) — this is a different mechanism (an in-process API call gated by config, not the CI pipeline) applied to a fundamentally different case (empty target vs. existing/populated target).
- **No changes to `FhirDbContext.OnModelCreating`** — its Fluent API model is hand-synced to `97.sql`'s shape already and must produce the byte-for-byte identical schema after decomposition (that's what the zero-diff check proves). Do not touch it in this plan.
- **`SqlCatalogGenerator`'s real current parsing logic** (`src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`, read in full): only extracts `CREATE TABLE` facts, via a predicate `name => name.EndsWith("SearchParam") || name == "ResourceType" || name == "Resource"` — an **exact** match on `"Resource"`, not `EndsWith("Resource")`, so `CurrentResource` is never one of the tables it parses. Only needs `Tables/*.sql` as input — `Views/`/`Types/`/`StoredProcedures/` are irrelevant to this generator and must not be added as `AdditionalFiles`.
- **`ISqlExecutionService`/`SqlExecutionService`'s established pattern** (`src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`, Phase A, already reviewed and approved — read it in full before starting Task 4): tenant resolution validates, in order, tenant existence (`InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.")`), storage type (`InvalidOperationException($"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' -- ...")`), and non-empty connection string (`InvalidOperationException($"Tenant {tenantId} is configured for 'SqlServer' storage but has no ConnectionString.")`) — via `ITenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken)`. `SchemaDeployer` (Task 4) must reuse this exact validation, not duplicate it — Task 4 extracts it into a shared internal method.
- **`Microsoft.SqlServer.DacFx`'s real API** (confirmed directly against Microsoft's own API reference and code samples, not assumed): `new DacServices(connectionString)`; `DacPackage.Load(Stream)` (also has `Load(string fileName)`); `void DacServices.Deploy(DacPackage package, string targetDatabaseName, bool upgradeExisting = false, DacDeployOptions options = default, CancellationToken? cancellationToken = default)` — `upgradeExisting: false` blocks any modification of an existing/populated target database and throws `DacServicesException` if the target already has schema that differs from the package; this is the load-bearing safety mechanism behind "existing tenant databases are never auto-touched." Namespace: `Microsoft.SqlServer.Dac`. NuGet package id: `Microsoft.SqlServer.DacFx` (distinct from the namespace name — do not confuse the two in the `.csproj`). Not yet present in `Directory.Packages.props` — this plan adds it.
- **`Ignixa.Web`'s config convention** (`src/Application/Ignixa.Web/appsettings.json`/`appsettings.Development.json`, read in full): top-level sections each carry an `"_Comment"` string plus settings (see `DurableTask`, `TransactionWatcher`, `Sidecar`, `Experimental` for examples). `appsettings.Development.json` only re-declares sections it overrides, inheriting everything else from the base file. No `"SqlServer"` section exists yet.
- **This repo's `IOptions<T>` binding convention** (confirmed via `BlobClientFactory.cs`, `OpenIddictServiceExtensions.cs`, `SearchServicesRegistration.cs`): `services.Configure<TOptions>(configuration.GetSection(TOptions.SectionName))` + constructor injection of `IOptions<TOptions>`, with a `public const string SectionName = "..."` on the options class itself.
- **The CI SQL Server container** (`.github/workflows/pr-build.yml`, `e2e-tests-sql` job, read in full): `docker compose -f docker-compose.test.yml up -d --wait` (container name `ignixa-test-sql`), a "Verify SQL Server connection" step running `sqlcmd` via `docker exec`, then test steps using `TEST_SQL_CONNECTION_STRING: "Server=localhost,1433;Database=FhirTest;User Id=sa;Password=${{ env.SQL_SA_PASSWORD }};TrustServerCertificate=true;Encrypt=false"`.
- **Docker is unavailable in this sandboxed session** — every task in this plan that needs a real SQL Server instance uses LocalDB instead (`Server=(localdb)\MSSQLLocalDB`), a disclosed, already-established substitution from Phase A. Never write a LocalDB connection string into any committed file — set it only as a shell environment variable for the duration of a task. CI-facing code/config must always reference the real Docker-based pattern above, unchanged.
- **Testing discipline**: exact assertions (real object counts, real `sys.tables`/`sys.columns` contents, real error messages), never loose non-null checks — matching this project's established discipline throughout.

---

### Task 1: Scaffold `Ignixa.DataLayer.SqlServer.Database` and decompose `97.sql`

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/*.sql` (36 files — 37 `CREATE TABLE` minus the discarded `CurrentResource` throwaway)
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Views/CurrentResource.sql`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/StoredProcedures/*.sql` (59 files)
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Types/*.sql` (23 files)
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Storage/*.sql` (9 files — 4 partition functions, 4 partition schemes, 1 sequence)
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Scripts/Script.PostDeployment.sql`
- Create: `scripts/decompose-97-sql.ps1` (the extraction script — kept as a checked-in tool, not a throwaway, since it's the reproducible record of exactly how the decomposition was derived)
- Modify: `All.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: nothing new (reads the existing `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`, unmodified).
- Produces: a building `.sqlproj` containing ~125 per-object `.sql` files, ready for Task 2's zero-diff verification.

Two things must **not** be ported into any generated file:

1. **The top-of-file idempotency guard** (`97.sql` lines 5–13: `SET XACT_ABORT ON; BEGIN TRAN; IF EXISTS (...) BEGIN ROLLBACK; RETURN; END`, closed by a bare `COMMIT` at line 1021, right after the last table) — this exists only because the old script could be executed against a possibly-non-empty database. SSDT's dacpac-diff deployment engine has no equivalent concept and provides its own idempotency. Drop entirely.
2. **The throwaway `CREATE TABLE dbo.CurrentResource (...); GO DROP TABLE dbo.CurrentResource;` pair** (lines 571–588) — exists only to let older tooling infer the later `CurrentResource` *view*'s column shape from a `CREATE TABLE` statement. `SqlCatalogGenerator`'s predicate (exact match on `"Resource"`, not `EndsWith("Resource")`) does not depend on it, and nothing else does either. Discard entirely — do not create `Tables/CurrentResource.sql`. The real, permanent object is the view at line 6343, which gets its own `Views/CurrentResource.sql` normally.

One thing needs special handling, not a plain per-object file: the dynamic partition-splitting loop that follows `PartitionScheme_ResourceChangeData_Timestamp`'s definition (lines 33–48: a `WHILE` loop calling `ALTER PARTITION SCHEME ... NEXT USED` / `ALTER PARTITION FUNCTION ... SPLIT RANGE` 768 times to pre-populate that scheme's boundaries). This is imperative *state* setup, not a static schema object — it must run once, after the scheme/function it references already exist. SSDT's native mechanism for this is a post-deployment script (`Scripts/Script.PostDeployment.sql`, which always runs after every schema object in the project — no manual ordering needed).

- [ ] **Step 1: Scaffold the `.sqlproj` skeleton**

```
dotnet tool install -g Microsoft.SqlPackage
dotnet new install Microsoft.Build.Sql.Templates
```

Then from `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/`:
```
dotnet new sqlproj -n Ignixa.DataLayer.SqlServer.Database
```

Create the folder structure (empty except for `.gitkeep` placeholders, populated by Step 3):
```
mkdir -p Tables Views StoredProcedures Types Storage Scripts
```

- [ ] **Step 2: Confirm the empty project builds**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj`
Expected: succeeds, produces `bin/Debug/Ignixa.DataLayer.SqlServer.Database.dacpac` (empty schema).

- [ ] **Step 3: Write and run the decomposition script**

Create `scripts/decompose-97-sql.ps1`:

```powershell
<#
    Decomposes the legacy 97.sql monolith into one .sql file per top-level
    object, under Ignixa.DataLayer.SqlServer.Database. Run once from the repo
    root: pwsh scripts/decompose-97-sql.ps1

    97.sql's object inventory at the time this script was written (verified
    by direct count -- see docs/superpowers/plans/2026-07-19-ignixa-datalayer-sqlserver-phase-b.md
    Global Constraints): 37 tables (1 discarded, see below), 1 view,
    59 stored procedures, 23 TVP types, 1 sequence, 4 partition functions,
    4 partition schemes = 125 top-level CREATE statements.
#>
[CmdletBinding()]
param(
    [string]$SourceSql = "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql",
    [string]$OutputRoot = "src/DataLayer/Ignixa.DataLayer.SqlServer.Database"
)

$ErrorActionPreference = "Stop"

$lines = Get-Content -LiteralPath $SourceSql

$folderByKind = @{
    "TABLE"              = "Tables"
    "VIEW"               = "Views"
    "PROCEDURE"          = "StoredProcedures"
    "PROC"               = "StoredProcedures"
    "TYPE"               = "Types"
    "SEQUENCE"           = "Storage"
    "PARTITION FUNCTION" = "Storage"
    "PARTITION SCHEME"   = "Storage"
}

foreach ($folder in ($folderByKind.Values | Sort-Object -Unique)) {
    New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot $folder) | Out-Null
}
New-Item -ItemType Directory -Force -Path (Join-Path $OutputRoot "Scripts") | Out-Null

# 1. Find every top-level object's starting line. Anchored at column 1 --
#    97.sql is consistently formatted (auto-generated), so this reliably
#    distinguishes real top-level CREATE statements from anything indented
#    inside a procedure body.
$objectPattern = '^CREATE\s+(TABLE|VIEW|PROCEDURE|PROC|TYPE|SEQUENCE|PARTITION FUNCTION|PARTITION SCHEME)\s+(.+)$'
$objects = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $objectPattern) {
        $objects += [PSCustomObject]@{
            StartIndex = $i
            Kind       = $Matches[1]
            RestOfLine = $Matches[2]
        }
    }
}

if ($objects.Count -ne 125) {
    throw "Expected 125 top-level CREATE statements in $SourceSql, found $($objects.Count). " +
          "Boundary detection is unreliable for a changed source file -- stop and investigate " +
          "before proceeding; do not adjust this count without re-verifying the real inventory."
}

Write-Host "Found $($objects.Count) top-level objects."

# 2. Extract each object's name from the text following the CREATE <kind>
#    keyword. Handles dbo.Name, [dbo].[Name], and bare Name forms.
function Get-ObjectName([string]$restOfLine) {
    $name = $restOfLine -replace '^\[dbo\]\.\[([^\]]+)\].*$', '$1'
    if ($name -eq $restOfLine) {
        $name = $restOfLine -replace '^dbo\.([A-Za-z0-9_]+).*$', '$1'
    }
    if ($name -eq $restOfLine) {
        $name = $restOfLine -replace '^([A-Za-z0-9_]+).*$', '$1'
    }
    return $name.Trim()
}

for ($idx = 0; $idx -lt $objects.Count; $idx++) {
    $obj = $objects[$idx]
    $obj | Add-Member -NotePropertyName Name -NotePropertyValue (Get-ObjectName $obj.RestOfLine)

    $endIndex = if ($idx -lt $objects.Count - 1) { $objects[$idx + 1].StartIndex - 1 } else { $lines.Count - 1 }

    # Special case: PartitionScheme_ResourceChangeData_Timestamp's real
    # content ends at its own closing ";" -- everything between that and the
    # next object is the dynamic partition-splitting loop (imperative setup,
    # not a static schema object), captured separately below into
    # Script.PostDeployment.sql instead of this object's own file.
    if ($obj.Name -eq "PartitionScheme_ResourceChangeData_Timestamp") {
        for ($j = $obj.StartIndex; $j -le $endIndex; $j++) {
            if ($lines[$j].TrimEnd().EndsWith(";")) { $endIndex = $j; break }
        }
    }

    $obj | Add-Member -NotePropertyName EndIndex -NotePropertyValue $endIndex
}

# 3. Write each object's file, trimming trailing blank lines and stray
#    GO/COMMIT batch-separator residue (the bare COMMIT at 97.sql's line 1021
#    closes the discarded idempotency guard and lands at the tail of
#    WatchdogLeases' generically-computed block -- this trim removes it
#    generically rather than special-casing that one table).
$writtenCount = 0
foreach ($obj in $objects) {
    if ($obj.Kind -eq "TABLE" -and $obj.Name -eq "CurrentResource") {
        Write-Host "Discarding throwaway CREATE TABLE dbo.CurrentResource (line $($obj.StartIndex + 1)) -- see plan Task 1."
        continue
    }

    $blockLines = $lines[$obj.StartIndex..$obj.EndIndex]
    while ($blockLines.Count -gt 0) {
        $last = $blockLines[-1].Trim()
        if ($last -eq "" -or $last -eq "GO" -or $last -eq "COMMIT") {
            $blockLines = $blockLines[0..($blockLines.Count - 2)]
        } else {
            break
        }
    }

    $folder = $folderByKind[$obj.Kind]
    $outPath = Join-Path $OutputRoot (Join-Path $folder "$($obj.Name).sql")
    Set-Content -LiteralPath $outPath -Value $blockLines -Encoding utf8
    $writtenCount++
}

# 4. Capture the dynamic partition-splitting loop into a post-deployment
#    script. SSDT always runs post-deployment scripts after every schema
#    object in the project, so it is guaranteed to run after the scheme and
#    function it references already exist -- no manual ordering needed.
$schemeObj = $objects | Where-Object { $_.Name -eq "PartitionScheme_ResourceChangeData_Timestamp" }
$schemeIdx = [array]::IndexOf($objects, $schemeObj)
$nextObj = $objects[$schemeIdx + 1]
$loopLines = $lines[($schemeObj.EndIndex + 1)..($nextObj.StartIndex - 1)] | Where-Object { $_.Trim() -ne "" }

$postDeployPath = Join-Path $OutputRoot "Scripts/Script.PostDeployment.sql"
Set-Content -LiteralPath $postDeployPath -Value $loopLines -Encoding utf8

Write-Host "Decomposition complete: $writtenCount object files + 1 post-deployment script written."
```

Run it from the repo root:
```
pwsh scripts/decompose-97-sql.ps1
```
Expected output: `Found 125 top-level objects.` then `Decomposition complete: 124 object files + 1 post-deployment script written.` (125 matched objects minus the 1 discarded `CurrentResource` table = 124 written files).

- [ ] **Step 4: Sanity-check a representative sample by hand**

Before relying on Task 2's automated zero-diff check, spot-check that the script did the right thing — open and read:
- `Tables/Resource.sql` — should start with `CREATE TABLE dbo.Resource (` and contain no trailing `GO`/blank lines.
- `Tables/WatchdogLeases.sql` — the table whose generic block originally included the guard's trailing `COMMIT`/`GO` residue (97.sql lines 1012–1022) — confirm the file ends cleanly at the closing `);` of the table definition, with no `COMMIT` or `GO` line.
- `Views/CurrentResource.sql` — should contain the real `CREATE VIEW dbo.CurrentResource` statement (line 6343 in the original), not the discarded throwaway table.
- `Types/BigintList.sql`, `StoredProcedures/MergeResources.sql` — spot-check two more objects of different kinds for sane content.
- `Storage/PartitionScheme_ResourceChangeData_Timestamp.sql` — should contain only 3 lines (`CREATE PARTITION SCHEME ... AS PARTITION ... ALL TO ([PRIMARY]);`), not the splitting loop.
- `Scripts/Script.PostDeployment.sql` — should contain the `DECLARE @numberOfHistoryPartitions ...` through the `WHILE ... END` block, with no blank lines.

Confirm file counts: `(Get-ChildItem -Recurse -Filter *.sql src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables,.../Views,.../StoredProcedures,.../Types,.../Storage | Measure-Object).Count` should total 124.

- [ ] **Step 5: Wire the post-deployment script into the `.sqlproj` and add build item globs**

The `Microsoft.Build.Sql` SDK auto-globs `.sql` files under known folders as `Build` items by default, but a post-deployment script needs an explicit `PostDeploy` item type. Add to `Ignixa.DataLayer.SqlServer.Database.sqlproj`:
```xml
<ItemGroup>
  <PostDeploy Include="Scripts\Script.PostDeployment.sql" />
</ItemGroup>
```
Confirm (via `dotnet build` output or the SDK's default glob docs) whether the auto-include behavior would otherwise also pick up `Scripts/Script.PostDeployment.sql` as a plain `Build` item and cause a duplicate/conflicting item warning — if so, exclude it from the default glob explicitly:
```xml
<ItemGroup>
  <Build Remove="Scripts\Script.PostDeployment.sql" />
</ItemGroup>
```

- [ ] **Step 6: Confirm the populated project builds**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj`
Expected: 0 warnings, 0 errors, produces a `.dacpac` containing all 124 objects + the post-deployment script. Any build error here (e.g. a T-SQL syntax error introduced by an incorrect line-boundary cut) must be fixed by correcting the specific generated `.sql` file — do not modify the extraction script's committed output for one-off fixes without also fixing the script itself if the bug is systematic (e.g. affects every object of one kind).

- [ ] **Step 7: Register the new project in the solution**

```
dotnet sln All.sln add src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj
```

- [ ] **Step 8: Commit**

```bash
git add scripts/decompose-97-sql.ps1 src/DataLayer/Ignixa.DataLayer.SqlServer.Database All.sln
git commit -m "feat(datalayer-sqlserver): decompose 97.sql into a SQL Database Project"
```

---

### Task 2: Zero-diff verification against a real database

**Files:** none created — this task verifies Task 1's output and fixes any discrepancies found in `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/**/*.sql` if the diff is non-zero.

**Interfaces:**
- Consumes: Task 1's `.dacpac` build output.
- Produces: a confirmed-zero-diff decomposition — the actual correctness gate for Task 1's mechanical work. Later tasks assume this is true.

- [ ] **Step 1: Bootstrap a reference database the OLD way**

Using LocalDB (Docker unavailable in this sandbox — see Global Constraints):
```
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "CREATE DATABASE IgnixaPhaseBReference"
```
Execute the *unmodified* `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql` against it (batch-split on `GO`, matching what `DatabaseInitializer.CreateDatabaseSchemaAsync` does today — re-read that method, lines 186–246, for the exact batching approach before scripting this), then apply the 6 EF migrations:
```
dotnet ef database update --project src/DataLayer/Ignixa.DataLayer.SqlEntityFramework --connection "Server=(localdb)\MSSQLLocalDB;Database=IgnixaPhaseBReference;Integrated Security=true;TrustServerCertificate=true"
```
(If `97.sql`'s raw text needs a small helper script to batch-split and execute via `sqlcmd -i`, write it as a one-off command, not a new committed file — this reference database is scaffolding for this task only, not a permanent artifact.)

- [ ] **Step 2: Build the dacpac and run DeployReport**

```
dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release
sqlpackage /Action:DeployReport `
  /SourceFile:src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac `
  /TargetConnectionString:"Server=(localdb)\MSSQLLocalDB;Database=IgnixaPhaseBReference;Integrated Security=true;TrustServerCertificate=true" `
  /OutputPath:deployreport.xml
```

Expected: `deployreport.xml` contains zero `<Operation>` elements (no table/column/index/constraint/procedure/type differences).

- [ ] **Step 3: Investigate and fix any reported differences**

If the report is non-empty, for each difference: identify which object it belongs to, compare that object's extracted `.sql` file against the original text in `97.sql` at the relevant line range, and correct the extracted file (whitespace/formatting differences that SqlPackage's semantic diff ignores are fine and expected — only genuine content differences, e.g. a dropped constraint or a wrong data type from an imprecise line-boundary cut, need fixing). Re-run Step 2 after each fix until the report is empty.

- [ ] **Step 4: Confirm the post-deployment script actually ran**

Query the reference-shaped database that the dacpac deployed into (a *fresh* empty LocalDB database, not the reference DB from Step 1 — deploy into a new one to prove the post-deployment script executes correctly on a real empty target):
```
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "CREATE DATABASE IgnixaPhaseBFreshDeploy"
sqlpackage /Action:Publish `
  /SourceFile:src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac `
  /TargetConnectionString:"Server=(localdb)\MSSQLLocalDB;Database=IgnixaPhaseBFreshDeploy;Integrated Security=true;TrustServerCertificate=true"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d IgnixaPhaseBFreshDeploy -Q "SELECT $PARTITION.PartitionFunction_ResourceChangeData_Timestamp(sysutcdatetime())"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d IgnixaPhaseBFreshDeploy -Q "SELECT COUNT(*) FROM sys.partition_range_values WHERE function_id = (SELECT function_id FROM sys.partition_functions WHERE name = 'PartitionFunction_ResourceChangeData_Timestamp')"
```
Expected: the partition-range-values count is 768 (matching the loop's 48+720 iterations), confirming the post-deployment script ran and correctly pre-split the scheme's boundaries.

Also confirm re-running `/Action:Publish` a second time against `IgnixaPhaseBFreshDeploy` (now non-empty) either no-ops cleanly or fails safely — do not assume; this DB is disposable, so it's safe to test. Record the actual observed behavior in this task's completion notes, since it's relevant context for Phase C's future existing-database upgrade design, even though fixing any issue found here is out of scope for this plan (Phase B never re-deploys to a non-empty database in the shipped `SchemaDeployer`, per Task 4).

- [ ] **Step 5: Clean up the reference databases**

```
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE IgnixaPhaseBReference"
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE IgnixaPhaseBFreshDeploy"
```

- [ ] **Step 6: Commit any fixes from Step 3**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer.Database
git commit -m "fix(datalayer-sqlserver): correct decomposition diffs found by DeployReport"
```
(Skip this step if Step 3 found no differences to fix.)

---

### Task 3: `SqlCatalogGenerator` input-source change

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
- Modify: `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`

**Interfaces:**
- Consumes: `src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Tables/*.sql` (Task 1's output, zero-diff-verified by Task 2).
- Produces: the same `SqlCatalog.Default` facts as before — this task must not change what the generator emits, only where it reads from.

- [ ] **Step 1: Write a regression baseline**

Before changing anything, capture the generator's current output for comparison:
```
dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```
Locate the generated `SqlCatalog.g.cs` (under `obj/`) and copy it aside (e.g. `/tmp/SqlCatalog.g.cs.before`) — do not commit this copy, it's a throwaway comparison baseline for Step 4.

- [ ] **Step 2: Change the `AdditionalFiles` glob**

In `src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`, replace:
```xml
<AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlEntityFramework\Resources\97.sql" />
```
with:
```xml
<AdditionalFiles Include="..\..\DataLayer\Ignixa.DataLayer.SqlServer.Database\Tables\*.sql" />
```

- [ ] **Step 3: Update `SqlCatalogGenerator` to merge multiple files**

In `src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs`, change the predicate (currently `file.Path.EndsWith("97.sql", ...)`) to match the new files:
```csharp
var ddlFiles = context.AdditionalTextsProvider
    .Where(static file => file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
    .Collect();
```
Change the source-output registration from reading `files[0]` to concatenating every matched file's text before parsing (each object lives in exactly one file under the new layout, so a straight concatenation is equivalent to the old single-file text — no merge/precedence logic needed):
```csharp
context.RegisterSourceOutput(ddlFiles, static (spc, files) =>
{
    if (files.Length == 0)
    {
        return;
    }

    var ddlText = string.Join(
        Environment.NewLine,
        files.Select(f => f.GetText(spc.CancellationToken)?.ToString() ?? string.Empty));
    var tables = DdlTableParser.ParseTables(ddlText,
        name => name.EndsWith("SearchParam", StringComparison.Ordinal) || name == "ResourceType" || name == "Resource");

    spc.AddSource("SqlCatalog.g.cs", Emit(tables));
});
```
(`DdlTableParser.ParseTables`'s internals are unchanged — it still just parses T-SQL text, now assembled from many files instead of one.)

- [ ] **Step 4: Rebuild and diff against the baseline**

```
dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj
```
Compare the newly generated `SqlCatalog.g.cs` against the Step 1 baseline (`diff` or equivalent). Expected: **identical** table/column facts (the `Dictionary<string, TableDescriptor>` entries should match exactly — same tables, same columns, same types/nullability/collation/max-length), modulo harmless ordering differences from the new multi-file concatenation order (if ordering differs, confirm it doesn't matter — the emitted structure is a `Dictionary`, not an ordered list, so key/value equivalence is what matters, not textual order).

- [ ] **Step 5: Run `Ignixa.Search.Sql.Tests` as a regression check**

```
dotnet test src/Core/Ignixa.Search.Sql.Tests --filter "FullyQualifiedName!~E2ETests"
```
Expected: all passing, matching the pre-change baseline (this project's compiler tests depend on `SqlCatalog.Default`'s facts — any regression in the generator's output would show up here).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj src/Core/Ignixa.Search.Sql.Generators/SqlCatalogGenerator.cs
git commit -m "feat(datalayer-sqlserver): point SqlCatalogGenerator at the decomposed Tables/ folder"
```

---

### Task 4: `SchemaDeployer` — DacFx-based schema deployment

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaDeployer.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerOptions.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs` (extract shared connection-string resolution)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj` (add `Microsoft.SqlServer.DacFx`, add a project reference to `Ignixa.DataLayer.SqlServer.Database` for the embedded dacpac)
- Modify: `Directory.Packages.props` (add `Microsoft.SqlServer.DacFx` central version)
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SchemaDeployerConnectionTests.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerDeploymentTests.cs`

**Interfaces:**
- Consumes: `SqlExecutionService`'s existing `ITenantConfigurationStore`-based tenant resolution (Phase A, Task 2) — extracted into a shared method this task adds. `Ignixa.DataLayer.SqlServer.Database`'s built `.dacpac` (Tasks 1–2).
- Produces: `public interface ISchemaDeployer { Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken); }`, consumed by Task 5's app-startup wiring.

- [ ] **Step 1: Confirm the exact `Microsoft.SqlServer.DacFx` version to pin**

Check the latest stable version on NuGet.org for `Microsoft.SqlServer.DacFx` (this plan was written against v162.2.111, confirmed via Microsoft's own API reference — verify this is still current or find the actual latest stable release before pinning). Add to `Directory.Packages.props` (matching the existing alphabetized `<PackageVersion Include="..." Version="..." />` convention):
```xml
<PackageVersion Include="Microsoft.SqlServer.DacFx" Version="<confirmed-version>" />
```

- [ ] **Step 2: Add the package reference and project reference**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`, add:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.SqlServer.DacFx" />
</ItemGroup>
```
The `.dacpac` needs to ship as an embedded resource. Since `Ignixa.DataLayer.SqlServer.Database` is a `.sqlproj` (not a `.csproj`), a plain `<ProjectReference>` doesn't apply the same way — instead, reference its build output directly. Add an MSBuild target that copies the built `.dacpac` into this project and embeds it:
```xml
<ItemGroup>
  <None Include="..\Ignixa.DataLayer.SqlServer.Database\bin\$(Configuration)\Ignixa.DataLayer.SqlServer.Database.dacpac">
    <Link>Resources\Schema.dacpac</Link>
    <CopyToOutputDirectory>Never</CopyToOutputDirectory>
  </None>
  <EmbeddedResource Include="..\Ignixa.DataLayer.SqlServer.Database\bin\$(Configuration)\Ignixa.DataLayer.SqlServer.Database.dacpac">
    <LogicalName>Ignixa.DataLayer.SqlServer.Schema.dacpac</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```
This makes `Ignixa.DataLayer.SqlServer.Database` a **build-order dependency** without a formal `ProjectReference` (`.sqlproj` and `.csproj` don't compose via `ProjectReference` the normal way) — add an explicit MSBuild `ProjectReference` anyway if `dotnet build` doesn't already infer the correct build order from the embedded-resource path reference; verify by running a clean build (`dotnet clean && dotnet build All.sln`) and confirming `Ignixa.DataLayer.SqlServer.Database` builds before `Ignixa.DataLayer.SqlServer`. If ordering isn't automatic, add:
```xml
<ItemGroup>
  <ProjectReference Include="..\Ignixa.DataLayer.SqlServer.Database\Ignixa.DataLayer.SqlServer.Database.sqlproj" ReferenceOutputAssembly="false" />
</ItemGroup>
```

- [ ] **Step 3: Extract shared tenant connection-string resolution in `SqlExecutionService`**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`, extract the validation logic currently inline in `OpenConnectionAsync` (lines 34–51) into a new internal static method, preserving the exact three `InvalidOperationException` messages verbatim (Task 2's existing tests assert on them):
```csharp
internal static async Task<string> ResolveConnectionStringAsync(
    ITenantConfigurationStore tenantConfigurationStore, int tenantId, CancellationToken cancellationToken)
{
    var tenant = await tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
    if (tenant is null)
    {
        throw new InvalidOperationException($"Tenant {tenantId} does not exist or is inactive.");
    }

    if (tenant.Storage.Type != "SqlServer")
    {
        throw new InvalidOperationException(
            $"Tenant {tenantId} is configured for storage type '{tenant.Storage.Type}', not 'SqlServer' -- " +
            "ISqlExecutionService can only be used for tenants configured for SQL Server storage.");
    }

    if (string.IsNullOrEmpty(tenant.Storage.ConnectionString))
    {
        throw new InvalidOperationException(
            $"Tenant {tenantId} is configured for 'SqlServer' storage but has no ConnectionString.");
    }

    return tenant.Storage.ConnectionString;
}
```
Update `OpenConnectionAsync` to call it:
```csharp
internal async Task<SqlConnection> OpenConnectionAsync(int tenantId, CancellationToken cancellationToken)
{
    var connectionString = await ResolveConnectionStringAsync(_tenantConfigurationStore, tenantId, cancellationToken);
    var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);
    return connection;
}
```
Run the existing Task 2 tests to confirm nothing broke: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName!~Integration`. Expected: still 4/4 passing (same messages, same behavior, just refactored).

- [ ] **Step 4: Add `SqlServerOptions`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlServerOptions.cs`:
```csharp
namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    public bool AutomaticSchemaDeploymentEnabled { get; set; }
}
```

- [ ] **Step 5: Write `ISchemaDeployer`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/ISchemaDeployer.cs`:
```csharp
namespace Ignixa.DataLayer.SqlServer;

public interface ISchemaDeployer
{
    /// <summary>
    /// Deploys the schema to a tenant's database if -- and only if -- that database is
    /// currently empty. Never modifies a database that already has schema.
    /// </summary>
    Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Write `SchemaDeployer`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/SchemaDeployer.cs`:
```csharp
using System.Reflection;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SqlServer.Dac;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SchemaDeployer : ISchemaDeployer
{
    private const string DacpacResourceName = "Ignixa.DataLayer.SqlServer.Schema.dacpac";

    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<SqlServerOptions> _options;
    private readonly ILogger<SchemaDeployer> _logger;

    public SchemaDeployer(
        ITenantConfigurationStore tenantConfigurationStore,
        IHostEnvironment environment,
        IOptions<SqlServerOptions> options,
        ILogger<SchemaDeployer> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task DeployIfEmptyAsync(int tenantId, CancellationToken cancellationToken)
    {
        var connectionString = await SqlExecutionService.ResolveConnectionStringAsync(
            _tenantConfigurationStore, tenantId, cancellationToken);

        if (_environment.IsDevelopment() && !await CanConnectAsync(connectionString, cancellationToken))
        {
            await CreateEmptyDatabaseAsync(connectionString, cancellationToken);
        }

        if (!await IsDatabaseEmptyAsync(connectionString, cancellationToken))
        {
            _logger.LogDebug("Tenant {TenantId}'s database already has schema; skipping deploy.", tenantId);
            return;
        }

        if (!_options.Value.AutomaticSchemaDeploymentEnabled)
        {
            throw new InvalidOperationException(
                $"Tenant {tenantId}'s database is not initialized and " +
                $"{SqlServerOptions.SectionName}:{nameof(SqlServerOptions.AutomaticSchemaDeploymentEnabled)} is false. " +
                "Deploy the schema manually (sqlpackage /Action:Publish against the " +
                "Ignixa.DataLayer.SqlServer.Database dacpac) before starting the app, or enable automatic deployment.");
        }

        using var dacpacStream = typeof(SchemaDeployer).Assembly.GetManifestResourceStream(DacpacResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DacpacResourceName}' not found in {typeof(SchemaDeployer).Assembly.FullName}.");
        using var package = DacPackage.Load(dacpacStream);

        var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var dacServices = new DacServices(connectionString);
        dacServices.Deploy(package, databaseName, upgradeExisting: false, cancellationToken: cancellationToken);
        _logger.LogInformation("Deployed schema to tenant {TenantId}'s new database '{DatabaseName}'.", tenantId, databaseName);
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    }

    private static async Task CreateEmptyDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IsDatabaseEmptyAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Resource') THEN 0 ELSE 1 END";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return (int)result! == 1;
    }
}
```

Notes for the implementer:
- `CreateEmptyDatabaseAsync`'s raw `CREATE DATABASE` against `master` preserves `DatabaseInitializer.CreateEmptyDatabaseAsync`'s existing dev-only behavior (`src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/DatabaseInitializer.cs:315-357`, read it for comparison) — this is **not** gated by `AutomaticSchemaDeploymentEnabled` (that toggle only gates the actual schema *deploy* step), it's gated by `IHostEnvironment.IsDevelopment()`, replacing the old class's string-comparison `IsDevelopmentMode()` check with the idiomatic ASP.NET Core equivalent, since this is a from-scratch rewrite with no compatibility constraint forcing the old approach forward.
- The old `DatabaseInitializer` constructor had a documented-but-never-implemented `managedIdentityName` parameter (confirmed dead code — grep the whole 383-line file, it's referenced nowhere in the body). Do not carry it forward into `SchemaDeployer`.
- `upgradeExisting: false` is the concrete mechanism enforcing "existing tenant databases are never auto-touched" — if `IsDatabaseEmptyAsync` is ever wrong (a bug elsewhere routes a non-empty database here), `DacServices.Deploy` throws `DacServicesException` rather than silently attempting an unreviewed upgrade.
- Before finalizing, confirm `DacServices.Deploy`'s exact overload resolution compiles as written — it takes named/optional parameters (`options` and `cancellationToken` both have defaults); this plan's call passes `cancellationToken:` by name and omits `options`, which should resolve correctly, but confirm via a successful build rather than assuming.

- [ ] **Step 7: Wire up DI registration**

Add a small extension method (matching `BlobClientFactory`'s pattern) — create or extend an existing `*ServiceCollectionExtensions.cs` in `Ignixa.DataLayer.SqlServer`:
```csharp
public static IServiceCollection AddIgnixaSqlServerSchemaDeployment(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<SqlServerOptions>(configuration.GetSection(SqlServerOptions.SectionName));
    services.AddSingleton<ISchemaDeployer, SchemaDeployer>();
    return services;
}
```
(This method is called from `Ignixa.Web`/`Ignixa.Api` in Task 5, not here — Task 4 stays isolated per the "zero unrelated production wiring within this task" spirit, though note Task 5 is where the actual wiring happens since Phase B as a whole *does* touch production wiring, per the Global Constraints.)

- [ ] **Step 8: Unit tests for `SchemaDeployer`'s non-DB-touching logic**

Create `test/Ignixa.DataLayer.SqlServer.Tests/SchemaDeployerConnectionTests.cs`, reusing the `FakeTenantConfigurationStore` pattern already established in `SqlExecutionServiceConnectionTests.cs`:
```csharp
public class SchemaDeployerConnectionTests
{
    [Fact]
    public async Task GivenANonexistentTenant_WhenDeployIfEmptyAsyncCalled_ThenThrowsWithTenantMessage()
    {
        var store = new FakeTenantConfigurationStore(); // no tenant 999
        var deployer = new SchemaDeployer(
            store,
            new HostingEnvironment { EnvironmentName = "Production" },
            Options.Create(new SqlServerOptions { AutomaticSchemaDeploymentEnabled = true }),
            NullLogger<SchemaDeployer>.Instance);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => deployer.DeployIfEmptyAsync(999, CancellationToken.None));

        ex.Message.ShouldBe("Tenant 999 does not exist or is inactive.");
    }
}
```
(Add a second test confirming a tenant configured for `FileSystem` storage throws the storage-type message, mirroring `SqlExecutionServiceConnectionTests.cs`'s existing coverage for the same failure mode.)

- [ ] **Step 9: Integration tests against a real database**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SchemaDeployerDeploymentTests.cs`, using the same `TEST_SQL_CONNECTION_STRING` pattern as `SqlExecutionServiceExecutionTests.cs`:
```csharp
public class SchemaDeployerDeploymentTests
{
    [Fact]
    public async Task GivenAnEmptyDatabase_WhenDeployIfEmptyAsyncCalled_ThenCreatesTheExpectedTables()
    {
        // Arrange: a real, empty, freshly-created database (unique name per test run).
        // Act: deployer.DeployIfEmptyAsync(tenantId, CancellationToken.None)
        // Assert: query sys.tables directly, confirm at least Resource, TokenSearchParam,
        // and ResourceType exist (golden-shape assertion, not a loose row-count check) --
        // matching Task 2's independently-verified zero-diff decomposition.
    }

    [Fact]
    public async Task GivenANonEmptyDatabase_WhenDeployIfEmptyAsyncCalled_ThenDoesNotAttemptDeploy()
    {
        // Arrange: a database that already has the Resource table (e.g. deploy once, then
        // call DeployIfEmptyAsync again).
        // Act & Assert: the second call returns without throwing and without modifying
        // anything (confirm no DacServicesException, confirm schema is unchanged) --
        // proving the emptiness check short-circuits before ever calling DacServices.Deploy.
    }

    [Fact]
    public async Task GivenAnEmptyDatabaseAndTheToggleDisabled_WhenDeployIfEmptyAsyncCalled_ThenThrowsAnActionableError()
    {
        // Arrange: AutomaticSchemaDeploymentEnabled = false, a real empty database.
        // Act & Assert: throws InvalidOperationException mentioning both the config key
        // name and the manual sqlpackage command -- not a silent no-op, not a hang.
    }
}
```
Write the actual test bodies with real connection strings (`TEST_SQL_CONNECTION_STRING`, throwing if unset per the established pattern), real database creation/teardown, and exact `sys.tables` assertions — not placeholders.

- [ ] **Step 10: Run all new and existing tests**

```
dotnet build All.sln
dotnet test test/Ignixa.DataLayer.SqlServer.Tests --filter FullyQualifiedName!~Integration
```
Set `TEST_SQL_CONNECTION_STRING` (LocalDB substitute) and run:
```
dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests
```
Expected: 0 warnings/errors on build; all unit and integration tests passing, including Phase A's pre-existing ones (unaffected by Step 3's refactor).

- [ ] **Step 11: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer test/Ignixa.DataLayer.SqlServer.Tests test/Ignixa.DataLayer.SqlServer.IntegrationTests Directory.Packages.props
git commit -m "feat(datalayer-sqlserver): add SchemaDeployer -- DacFx-based schema deployment for new tenants"
```

---

### Task 5: Wire `SchemaDeployer` into app startup; retire the EF migration/bootstrap path

**Files:**
- Modify: `Ignixa.Web`'s (or `Ignixa.Api`'s — confirm which project actually calls `DatabaseInitializer` today via grep before assuming) startup wiring
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/DatabaseInitializer.cs`
- Delete: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/DatabaseSchemaInitializationTests.cs`
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Migrations/` (all 13 files: 6 migration `.cs` + 6 `.Designer.cs` + `FhirDbContextModelSnapshot.cs`)
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Ignixa.DataLayer.SqlEntityFramework.csproj` (remove the two `97.sql`-specific entries only — `<None Remove="Resources\97.sql" />` and `<EmbeddedResource Include="Resources\97.sql" />` — do **not** touch the separate `SetupManagedIdentity.sql` `EmbeddedResource` entry, an unrelated file)
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/FhirDbContext.cs` (remove the `OnConfiguring` override that ignores `PendingModelChangesWarning` — no longer relevant once nothing calls `Migrate*`)
- Modify: `src/Application/Ignixa.Web/appsettings.json`
- Modify: `src/Application/Ignixa.Web/appsettings.Development.json`

**Interfaces:**
- Consumes: `ISchemaDeployer` (Task 4).
- Produces: app startup that deploys schema via `SchemaDeployer` instead of `DatabaseInitializer`; zero references to the deleted EF migration machinery anywhere in the codebase.

- [ ] **Step 1: Find the actual call site**

```
grep -rn "DatabaseInitializer" src/ --include=*.cs
```
Confirm which file constructs/calls `DatabaseInitializer.InitializeAsync` today (likely `Program.cs` or a startup extension method in `Ignixa.Web` or `Ignixa.Api`) — this plan's earlier research did not trace the call site, only the class itself.

- [ ] **Step 2: Replace the call site**

Register `SchemaDeployer` via `AddIgnixaSqlServerSchemaDeployment` (Task 4, Step 7) in the same startup location, and replace the `DatabaseInitializer.InitializeAsync(...)` call with `ISchemaDeployer.DeployIfEmptyAsync(tenantId, cancellationToken)` for each configured tenant (confirm from the original call site whether initialization loops over all configured tenants or just tenant 1 — preserve that same iteration behavior, don't narrow or widen scope silently).

- [ ] **Step 3: Delete the old bootstrap machinery**

```bash
git rm src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/DatabaseInitializer.cs
git rm test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/DatabaseSchemaInitializationTests.cs
git rm -r src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Migrations
git rm src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql
```

- [ ] **Step 4: Clean up the csproj**

In `Ignixa.DataLayer.SqlEntityFramework.csproj`, remove exactly these two lines (confirmed at lines 40–41 in the pre-Phase-B file):
```xml
<None Remove="Resources\97.sql" />
<EmbeddedResource Include="Resources\97.sql" />
```
Leave `<EmbeddedResource Include="Resources\SetupManagedIdentity.sql" />` untouched — it's an unrelated file.

- [ ] **Step 5: Remove the now-unnecessary `OnConfiguring` override**

In `FhirDbContext.cs`, remove the `ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))` call and its containing `OnConfiguring` override if that was its only content (check — if `OnConfiguring` has other unrelated configuration, keep the method and only remove this one line).

- [ ] **Step 6: Add the `SqlServer` appsettings section**

In `src/Application/Ignixa.Web/appsettings.json`, add (matching the existing `"_Comment"` convention, placed alphabetically or near other `DataLayer`-adjacent sections):
```json
"SqlServer": {
  "_Comment": "Controls whether the app auto-deploys the SSDT-built schema (.dacpac) to brand-new, empty tenant databases at startup. Never applies to existing/populated databases -- those are never auto-touched regardless of this setting.",
  "AutomaticSchemaDeploymentEnabled": false
}
```
In `src/Application/Ignixa.Web/appsettings.Development.json`, add (overriding only the one setting, matching this file's existing sparse-override style):
```json
"SqlServer": {
  "AutomaticSchemaDeploymentEnabled": true
}
```

- [ ] **Step 7: Full solution build and test**

```
dotnet build All.sln
```
Expected: 0 warnings, 0 errors — confirms nothing else in the solution still references the deleted `DatabaseInitializer`/`Migrations`/`97.sql` embedded resource.
```
dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"
```
Expected: all passing except the 2 known pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures.

- [ ] **Step 8: End-to-end confirmation with a real fresh database**

Set `TEST_SQL_CONNECTION_STRING`-style connection info for a brand-new LocalDB database the app has never seen, run the app (or the relevant startup integration test) with `SqlServer:AutomaticSchemaDeploymentEnabled=true`, and confirm it starts successfully and the database ends up with the expected tables — this is the actual proof that Task 5's wiring works end-to-end, not just that it compiles.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(datalayer-sqlserver): wire SchemaDeployer into app startup, retire EF migration bootstrap"
```

---

### Task 6: CI pipeline — automated `DeployReport` on every PR

**Files:**
- Modify: `.github/workflows/pr-build.yml`

**Interfaces:**
- Consumes: `Ignixa.DataLayer.SqlServer.Database`'s `.sqlproj` (Task 1) and the already-running `ignixa-test-sql` container (existing `e2e-tests-sql` job).
- Produces: a `deployreport.xml` build artifact on every PR that touches the `.sqlproj`, for human review. `/Action:Publish` never runs in CI.

- [ ] **Step 1: Add a build+DeployReport step to the existing `e2e-tests-sql` job**

In `.github/workflows/pr-build.yml`, after the existing "Verify SQL Server connection" step and before the E2E test steps, add:
```yaml
      - name: Install SqlPackage
        run: dotnet tool install -g Microsoft.SqlPackage

      - name: Build Ignixa.DataLayer.SqlServer.Database
        run: dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer.Database/Ignixa.DataLayer.SqlServer.Database.sqlproj --configuration Release

      - name: Generate schema DeployReport
        run: |
          sqlpackage /Action:DeployReport \
            /SourceFile:src/DataLayer/Ignixa.DataLayer.SqlServer.Database/bin/Release/Ignixa.DataLayer.SqlServer.Database.dacpac \
            /TargetConnectionString:"Server=localhost,1433;Database=FhirSchemaReview;User Id=sa;Password=${{ env.SQL_SA_PASSWORD }};TrustServerCertificate=true;Encrypt=false" \
            /OutputPath:${{ runner.temp }}/deployreport.xml
        env:
          SQL_SA_PASSWORD: ${{ env.SQL_SA_PASSWORD }}

      - name: Upload schema DeployReport
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: schema-deploy-report
          path: ${{ runner.temp }}/deployreport.xml
```
Note: `/TargetConnectionString` here points at a **new, empty** database name (`FhirSchemaReview`) on the same running container, not `FhirTest` (the E2E tests' own database) — this keeps the schema review target isolated from whatever state the E2E test run leaves behind.

This task ships the simpler of two possible signals, deliberately: `DeployReport` against a fresh, empty target database reports "everything will be created" (the full schema as one large diff) on every PR — a **build-verification** signal (does the `.sqlproj` still build and deploy cleanly to a real SQL Server instance), not a **change-detection** signal (what did this specific PR change in the schema). A true change-detection report would require deploying the base branch's dacpac into `FhirSchemaReview` first, then diffing the PR branch's dacpac against that — building the "before" state is real added complexity (checking out and building a second ref inside the same job) that this task does not attempt. Ship the build-verification version now; record "diff against base branch's schema" as an explicit, named follow-up in this task's commit message, not silently.

- [ ] **Step 2: Verify the workflow YAML is syntactically valid**

```
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/pr-build.yml'))" 2>/dev/null || echo "no python3 -- skip local YAML validation, rely on GitHub Actions' own parse on push"
```
(Or use any available YAML linter — the point is confirming no indentation/syntax errors before pushing, since a broken CI workflow file fails silently as "no such job" rather than a clear error.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/pr-build.yml
git commit -m "ci(datalayer-sqlserver): add schema DeployReport artifact to the SQL Server E2E job"
```

(This step cannot be locally verified end-to-end without pushing to a branch CI actually runs against — note this in the final report so the whole-branch reviewer knows to treat this task's correctness claim as "should work, unverified against real CI" rather than "confirmed.")

---

## Final steps (controller, not a task subagent)

After all 6 tasks are complete and reviewed clean:
1. Full solution build + test (`dotnet build All.sln`, `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`).
2. Generate the final whole-branch review package (`scripts/review-package` against the merge-base with `feature/fhir-to-sql-compiler` — Phase A's own merge-base, since this branch never merged there) and dispatch the final reviewer on the most capable available model, per this session's established pattern.
3. Report the full picture to the user; ask explicitly before merging (note: per the user's Phase A decision, this branch likely stays standalone rather than merging into `feature/fhir-to-sql-compiler` — confirm with the user rather than assuming the same choice applies) and again before pushing.
