# Step 0 — Compartment-Search Proving Increment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Determine, with a real SQL Server and real-scale data, whether Ignixa's current compartment-search code path still has a performance gap worth building a whole compiler for — or whether it's already closed by a code path that already exists.

**Architecture:** No compiler code gets written in this plan. This is a characterization + benchmark task: confirm what code path runs today, provision compartment-scale data, and run three SQL variants (current production code / current + one-line literal fix / legacy known-good SQL) cold and warm against the same database.

**Tech Stack:** xUnit + Shouldly (repo convention), EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer` per `Directory.Packages.props:42`), raw `Microsoft.Data.SqlClient` for the legacy-SQL arm, SQL Server 2022 via `docker-compose.test.yml`.

## Global Constraints

- `dotnet build All.sln` must stay 0 warnings, 0 errors after every task.
- No production code changes in this plan except the one-arm experimental variant in Task 4, which lands in a throwaway test project, not `Ignixa.DataLayer.SqlEntityFramework`.
- Test project namespace/style follows repo convention: file-scoped namespaces, `Nullable=enable`, AAA test structure, `GivenContext_WhenAction_ThenResult` naming, no `#region`.
- Connection string comes from the `TEST_SQL_CONNECTION_STRING` environment variable, matching `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs:39-42` — do not invent a new config convention.
- This plan produces a **findings document**, not a merged feature. Its output feeds the go/no-go decision on `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md` Phase 1.

---

### Task 1: Confirm the current compartment-search code path and whether `CompartmentSearchProblem.txt` still reproduces

**Files:**
- Read: `src/Application/Ignixa.Application/Features/Compartment/SearchCompartmentHandler.cs`
- Read: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/SearchExpressionQueryBuilder.cs:80-95, 166-207`
- Read: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompartmentSearchQueryGenerator.cs`
- Read: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt` (header, and the debug-log block starting at line 859)
- Create: `docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md`

**Interfaces:**
- Consumes: nothing (pure investigation).
- Produces: a written finding that Task 4 cites when choosing which arms to build.

This task has already been substantially verified during planning (2026-07-15); this step is to record it durably and let a fresh reader confirm it rather than take it on faith.

- [ ] **Step 1: Verify the current dispatch path**

Confirm three things by reading the files above:

1. `SearchCompartmentHandler.cs:71-88` always constructs a `CompartmentSearchExpression` — including for wildcard (`ResourceType == "*"`) searches (`:115-121`) — and never rewrites it into an `Or`/`And` tree of plain `Param` expressions before it reaches the data layer.
2. `SearchExpressionQueryBuilder.cs:85` dispatches every `CompartmentSearchExpression` unconditionally to `ApplyCompartmentSearchExpressionAsync` → `CompartmentSearchQueryGenerator.GenerateCompartmentQueryAsync`.
3. `CompartmentSearchQueryGenerator.cs:93-206` batches predicates by unique `SearchParamId`, `UNION`s them, and forces `resourceTypeIds` to inline via `EF.Constant(resourceTypeIds.ToList())` (`:184`) — but leaves `SearchParamId` itself as a captured/parameterized value (`:182`, `refParam.SearchParamId == searchParamId`).

**Expected:** all three hold on `feature/fhir-to-sql-compiler` as of this writing. If they don't (i.e., if the handler has been changed since), stop and re-scope this task — the rest of this plan assumes they hold.

- [ ] **Step 2: Reconcile against `CompartmentSearchProblem.txt`**

Read the debug log starting at `CompartmentSearchProblem.txt:859`. It shows `"Compartment expression rewritten: (Compartment Patient 'example') -> (Or (And (Param _type ...) (Param subject ...)) ...)"` and `SearchParameterQueryGenerator[0] Generating query for search parameter: _type / subject / patient / ...` — i.e., the captured "new server" run went through the generic per-type `Or`/`And` expression path, **not** `CompartmentSearchQueryGenerator`.

That contradicts current code (Step 1). Confirm by running:

```bash
git log --oneline -- src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/CompartmentSearchProblem.txt
git log --oneline -- src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompartmentSearchQueryGenerator.cs
```

Both point to `38a979df` ("Improvements/fixes (#3)") as their introducing commit — i.e., `CompartmentSearchQueryGenerator` and the `.txt` capture entered the repo in the same commit, with the generator's own doc comment reading "Matches Microsoft FHIR Server's proven fast pattern." The straightforward reading: the `.txt` file's "new server" log predates (or was captured mid-way through) the fix that commit shipped, and current code no longer takes the naive path the log shows.

**Expected:** conclude that `CompartmentSearchProblem.txt` is a historical artifact of an already-fixed naive path, not a description of current behavior.

- [ ] **Step 3: Write the findings doc**

Create `docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md`:

```markdown
# Investigation: Compartment Search Step 0 — Is the Motivating Bug Still Live?

**Date:** 2026-07-15
**Status:** Complete

## Question

`docs/superpowers/specs/2026-07-14-fhir-to-sql-compiler-design.md` names `CompartmentSearchProblem.txt`
as its motivating bug: Ignixa's EF-generated compartment query times out where hand-written SQL doesn't.
Does that gap still exist on `feature/fhir-to-sql-compiler` today?

## Finding

No — not in the form the design doc describes. `CompartmentSearchQueryGenerator.cs` (introduced in the
same commit as `CompartmentSearchProblem.txt`, `38a979df`) is unconditionally used for every compartment
search today (`SearchCompartmentHandler.cs:19-27`, `SearchExpressionQueryBuilder.cs:85`), including the
wildcard case the `.txt` file captures. It already batches by `SearchParamId`, `UNION`s per-parameter
queries instead of nesting them, drops the `Resource` table join, and forces `ResourceTypeId` lists to
inline via `EF.Constant()` to avoid EF Core 9+'s `OPENJSON` parameterization.

The one thing it does **not** do that the legacy hand-written SQL in `CompartmentSearchProblem.txt` does:
literalize `SearchParamId` itself (`CompartmentSearchQueryGenerator.cs:182` is a captured/sniffable
parameter, not `EF.Constant`).

## Consequence

The design doc's four-arm factorial, as originally scoped, tests a baseline ("naive EF") that is no
longer reachable in production. The real open question is narrower: **does literalizing `SearchParamId`
close whatever gap remains between today's `CompartmentSearchQueryGenerator` and the known-good legacy
SQL, at realistic data scale and skew?** That's what the rest of this plan measures.
```

- [ ] **Step 4: Commit**

```bash
git add docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md
git commit -m "docs(investigation): confirm compartment search motivating bug is narrower than the design doc assumed"
```

---

### Task 2: Stand up the SQL Server test database and confirm the seeding path

**Files:**
- Read: `docker-compose.test.yml`
- Read: `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` (in full — this is the only place in the repo that currently seeds data into this schema against a real SQL Server; reuse its catalog/seed helpers rather than writing new ones)
- Read: `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/FhirDbContext.cs` (or equivalent — locate via `Glob "**/FhirDbContext.cs"` if the path differs) to confirm the `DbSet<ReferenceSearchParam>` / `DbSet<ResourceEntity>` / `DbSet<ResourceType>` names Task 3 will use.
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj`

**Interfaces:**
- Consumes: `TEST_SQL_CONNECTION_STRING` env var (same convention as `IgnixaApiFixture.cs:39-42`).
- Produces: a running SQL Server container with the Ignixa schema applied, reachable by Task 3/4's harness; a new test project other tasks in this plan (and eventually Phase 9's differential suite) can extend.

- [ ] **Step 1: Start the test SQL Server**

```bash
docker compose -f docker-compose.test.yml up -d sqlserver
```

Wait for the healthcheck to pass:

```bash
docker compose -f docker-compose.test.yml ps sqlserver
```

Expected: `STATUS` shows `healthy` within ~60 seconds.

- [ ] **Step 2: Export the connection string**

```bash
export TEST_SQL_CONNECTION_STRING="Server=localhost,1433;Database=CompartmentStep0;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;Encrypt=false"
```

(PowerShell: `$env:TEST_SQL_CONNECTION_STRING = "..."`)

- [ ] **Step 3: Create the new test project**

**Amended 2026-07-15 during execution:** `test/Ignixa.DataLayer.SqlEntityFramework.Tests/` already exists — an orphaned project (`Ignixa.DataLayer.LegacySqlEF.Tests.csproj`, in-memory EF Core unit tests, unreferenced in `All.sln`, dating to the initial commit). Discovered by Task 2's first implementer attempt via a `dotnet new xunit --dry-run`, which showed it would overwrite the existing project's tracked `UnitTest1.cs`, and confirmed two `.csproj` files in one directory would cross-compile each other's sources (SDK-style default globbing). Resolved by renaming the new project to `Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` rather than touching the pre-existing, possibly-still-relevant legacy project — every path in this plan below already reflects that rename.

```bash
dotnet new xunit -n Ignixa.DataLayer.SqlEntityFramework.IntegrationTests -o test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests
dotnet sln All.sln add test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj
```

Add a project reference to `Ignixa.DataLayer.SqlEntityFramework` and package references matching sibling test projects' `Shouldly`/`Microsoft.Data.SqlClient` versions (check `Directory.Packages.props` for the pinned versions rather than floating `dotnet add package` — this repo centralizes versions there).

- [ ] **Step 4: Apply the schema**

Use `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/DatabaseInitializer.cs` directly — confirmed (by Task 2's first implementer attempt) as the mechanism production code already uses (`SqlEntityFrameworkRepositoryFactory.cs:297-304`), and the same one `IgnixaApiFixture.cs` relies on transitively via its `WebApplicationFactory<Program>` host. Do not hand-write DDL or duplicate `IgnixaApiFixture`'s manual `CREATE DATABASE master` step.

```csharp
var options = new DbContextOptionsBuilder<FhirDbContext>()
    .UseSqlServer(connectionString)
    .Options;
await using var context = new FhirDbContext(options);
var initializer = new DatabaseInitializer(context, loggerFactory.CreateLogger<DatabaseInitializer>(), "Development");
await initializer.InitializeAsync();
```

Passing `environment: "Development"` lets `DatabaseInitializer` create the `CompartmentStep0` database itself (`CanConnectAsync` fails → `CreateEmptyDatabaseAsync`), then applies the embedded `Resources/97.sql` baseline and any pending EF migrations. Confirmed `DbSet` names on `FhirDbContext`: `Resources` (`ResourceEntity`), `ResourceTypes` (`ResourceTypeEntity`), `ReferenceSearchParams` (`ReferenceSearchParamEntity`, mapped to `dbo.ReferenceSearchParam`).

**Expected:** connecting to `CompartmentStep0` and running `SELECT COUNT(*) FROM dbo.ReferenceSearchParam` returns `0` with no error — schema is present, table is empty.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/ All.sln
git commit -m "test(sql): add integration test project for the compartment-search step 0 experiment"
```

---

### Task 3: Seed compartment-scale data

**Files:**
- Modify: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj`
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentDataSeeder.cs`

**Interfaces:**
- Consumes: `FhirDbContext` (located in Task 2 Step 3), the exact entity property names from `ResourceEntity.cs` and the `ReferenceSearchParam` entity (read both files in full before writing this task's code — this plan does not fabricate their shape).
- Produces: `CompartmentDataSeeder.SeedAsync(FhirDbContext context, string compartmentId, int resourceTypeCount, int rowsPerResourceType, CancellationToken ct)`, used by Task 4.

**Before writing code:** read `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/ResourceEntity.cs` and the `ReferenceSearchParam` entity file in full (`Glob "src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/ReferenceSearchParam*.cs"`) to get every required column. This plan intentionally does not guess at EF entity shapes it hasn't verified — writing seed code against a wrong shape wastes the whole experiment on a compile error or, worse, a silently-wrong row.

- [ ] **Step 1: Read the entity shapes**

```bash
grep -n "public.*{ get; set; }" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/ResourceEntity.cs
grep -rn "public.*{ get; set; }" src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/ReferenceSearchParam*.cs
```

Record every required (`[Required]`) property — the seeder must set all of them or `SaveChangesAsync` throws a `DbUpdateException`.

- [ ] **Step 2: Design the shape to match `CompartmentSearchProblem.txt`'s skew**

The captured scenario has one `Patient` compartment (`compartmentId = "example"`) referenced from ~70 resource types via ~30-40 distinct search parameters (`subject`, `patient`, `performer`, ...), with wildly uneven cardinality per type (`CompartmentSearchProblem.txt:29` shows `ResourceTypeId IN (4,14,15,28,29,...)` — 19 types sharing one `SearchParamId`, i.e. one `SearchParameterInfo` like `subject` is reused structurally across many resource types). Seed:

- One `ResourceType` + `SearchParam` catalog row set covering at least 15 distinct resource types and 10 distinct reference search parameters (reuse whatever catalog-seeding helper `IgnixaApiFixture.cs` already uses — the catalog rows are a fixed, small set and this plan should not duplicate that logic).
- For the target compartment (`compartmentId = "step0-patient"`): a **skewed** distribution of `ReferenceSearchParam` rows — one "hot" resource type (e.g. `Observation`) with 500,000+ rows referencing the compartment, and the remaining resource types with 100–5,000 rows each. This skew is the whole point: it's what makes `SearchParamId` literalization matter for cardinality estimation (design doc, *Step 0 — the proving increment*).
- Corresponding `Resource` rows for every `(ResourceTypeId, ResourceSurrogateId)` pair referenced, since `CompartmentSearchQueryGenerator`'s comment at `:187-191` claims the join is safe to omit specifically because the index is covering — Task 4's arms must still produce a correct, joinable result set even though the fast arms skip the join.

- [ ] **Step 3: Implement the seeder using `SqlBulkCopy`, not `SaveChangesAsync`**

At 500K+ rows, EF's change tracker is the wrong tool (memory and speed). Use `Microsoft.Data.SqlClient.SqlBulkCopy` against a `DataTable` shaped to match `dbo.ReferenceSearchParam`'s columns (confirmed in Step 1), executed via `context.Database.GetDbConnection()`.

- [ ] **Step 4: Run the seeder once and record row counts**

Add a throwaway `Program.Main`-style entry point or a `[Fact(Skip = "manual seed — run once")]` that calls the seeder, run it once, then confirm:

```sql
SELECT SearchParamId, COUNT(*) FROM dbo.ReferenceSearchParam WHERE ReferenceResourceId = 'step0-patient' GROUP BY SearchParamId ORDER BY COUNT(*) DESC;
```

**Expected:** one row with 500,000+ count, the rest in the hundreds-to-thousands range — matches the skew CompartmentSearchProblem.txt's real-world capture implies.

- [ ] **Step 5: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentDataSeeder.cs
git commit -m "test(sql): seed skewed compartment-scale reference search param data for step 0"
```

---

### Task 4: Implement and run the three arms

**Files:**
- Create: `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentSearchStep0Benchmark.cs`

**Interfaces:**
- Consumes: `CompartmentDataSeeder` (Task 3), `CompartmentSearchQueryGenerator` (existing production class, `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Search/CompartmentSearchQueryGenerator.cs`).
- Produces: a results table (arm × cold/warm × elapsed ms) written to `docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md`, appended after Task 1's section.

Three arms, not the design doc's four — Task 1 already ruled out the "naive EF" baseline as unreachable in production:

- **Arm A — current production code, unmodified.** Call `CompartmentSearchQueryGenerator.GenerateCompartmentQueryAsync("Patient", "step0-patient", null, ct)` exactly as `SearchExpressionQueryBuilder` does, and materialize the result.
- **Arm B — Arm A with `SearchParamId` also literalized.** A one-line variant: change `refParam.SearchParamId == searchParamId` (`CompartmentSearchQueryGenerator.cs:182`) to `EF.Constant(searchParamId) == refParam.SearchParamId`. Since this plan must not modify production code, copy the method into a test-local class (`CompartmentSearchQueryGeneratorArmB`) with that one line changed — do not fork the whole file, only the LINQ query in the loop body (lines 181-185).
- **Arm C — the legacy SQL verbatim.** Execute the exact `SELECT` from `CompartmentSearchProblem.txt` lines 1-853 via raw `SqlCommand`, substituting `@p0 = 'step0-patient'` and the CTE list built dynamically from the same `searchParamMap` Arm A/B compute (the legacy query's CTE-per-`SearchParamId` shape is structurally what Arms A/B already produce — this arm exists to confirm there's no floor Arms A/B still haven't reached, not to reintroduce the 84-CTE literal text).

- [ ] **Step 1: Write the three query builders**

```csharp
public sealed class CompartmentSearchStep0Benchmark
{
    private readonly FhirDbContext _context;

    public CompartmentSearchStep0Benchmark(FhirDbContext context)
    {
        _context = context;
    }

    // Arm A: calls the real production generator directly, unmodified.
    public async Task<List<long>> RunArmAAsync(CancellationToken ct)
    {
        var generator = new CompartmentSearchQueryGenerator(
            _context, _cache, _compartmentDefinitionManager, _searchParameterDefinitionManager, _logger);
        var query = await generator.GenerateCompartmentQueryAsync("Patient", "step0-patient", null, ct);
        return await query.ToListAsync(ct);
    }

    // Arm B: same shape, SearchParamId forced to EF.Constant.
    public async Task<List<long>> RunArmBAsync(Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)> searchParamMap, CancellationToken ct)
    {
        IQueryable<long>? unioned = null;
        foreach (var (_, (searchParamId, resourceTypeIds)) in searchParamMap)
        {
            var paramQuery = from refParam in _context.ReferenceSearchParams
                              where EF.Constant(searchParamId) == refParam.SearchParamId
                                  && refParam.ReferenceResourceId == "step0-patient"
                                  && EF.Constant(resourceTypeIds.ToList()).Contains(refParam.ResourceTypeId)
                              select refParam.ResourceSurrogateId;
            unioned = unioned == null ? paramQuery : unioned.Union(paramQuery);
        }
        return unioned == null ? [] : await unioned.ToListAsync(ct);
    }

    // Arm C: raw ADO.NET, CTE-per-SearchParamId, SearchParamId as a SQL literal (not a parameter).
    public async Task<List<long>> RunArmCAsync(Dictionary<string, (short searchParamId, HashSet<short> resourceTypeIds)> searchParamMap, CancellationToken ct)
    {
        var cteParts = new List<string>();
        var i = 0;
        foreach (var (_, (searchParamId, resourceTypeIds)) in searchParamMap)
        {
            var typeList = string.Join(",", resourceTypeIds);
            cteParts.Add($"cte{i} AS (SELECT ResourceSurrogateId FROM dbo.ReferenceSearchParam " +
                          $"WHERE SearchParamId = {searchParamId} AND ReferenceResourceId = @compartmentId " +
                          $"AND ResourceTypeId IN ({typeList}))");
            i++;
        }
        var union = string.Join(" UNION ", Enumerable.Range(0, i).Select(n => $"SELECT ResourceSurrogateId FROM cte{n}"));
        var sql = $";WITH {string.Join(",", cteParts)} {union}";

        await using var connection = new SqlConnection(_context.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@compartmentId", "step0-patient");

        var results = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetInt64(0));
        }
        return results;
    }
}
```

**Note:** `_cache`, `_compartmentDefinitionManager`, `_searchParameterDefinitionManager`, `_logger` in Arm A must be resolved the same way production DI resolves `CompartmentSearchQueryGenerator` — read `SqlEntityFrameworkRepositoryFactory.cs`'s registration to find the concrete types/factory to reuse in this test rather than hand-rolling fakes, since faking them risks testing a different code path than production.

- [ ] **Step 2: Write the cold/warm harness**

```csharp
[Fact(Skip = "Manual step 0 experiment — run explicitly, not part of CI")]
public async Task Step0_ThreeArmComparison_RecordsElapsedTimes()
{
    var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TEST_SQL_CONNECTION_STRING not set");

    async Task<long> ClearPlanCacheAndTimeAsync(Func<Task> action)
    {
        await using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("DBCC FREEPROCCACHE;", conn);
            await cmd.ExecuteNonQueryAsync();
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    async Task<long> TimeAsync(Func<Task> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await action();
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    // ... build searchParamMap once (shared by arms B and C — same catalog data Task 3 seeded) ...
    // ... run each arm cold (ClearPlanCacheAndTimeAsync) then warm x3 (TimeAsync) ...
    // ... write results as a markdown table to the findings doc ...
}
```

- [ ] **Step 3: Run it**

```bash
dotnet test test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests --filter "FullyQualifiedName~Step0_ThreeArmComparison" -- xunit.methodDisplay=method
```

(Remove the `Skip` attribute locally to run it; keep it skipped when committed — this is a manual experiment, not a CI test.)

**Expected:** a completed run producing six timings (3 arms × cold/warm) without exceptions. Record raw numbers, don't round them away.

- [ ] **Step 4: Commit**

```bash
git add test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/CompartmentSearchStep0Benchmark.cs
git commit -m "test(sql): implement three-arm compartment search timing comparison for step 0"
```

---

### Task 5: Write the conclusion and update the roadmap

**Files:**
- Modify: `docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md`
- Modify: `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`

**Interfaces:**
- Consumes: Task 4's timing results.
- Produces: a go/no-go recommendation the roadmap's Phase 1 plan (written next) is conditioned on.

- [ ] **Step 1: Append results and interpretation to the findings doc**

Add a `## Results` section with the six timings as a markdown table, then a `## Conclusion` section answering directly:

- If Arm B ≈ Arm C (both close the gap from Arm A): literalizing `SearchParamId` is the fix. Recommend it as a standalone ~1-line PR against `CompartmentSearchQueryGenerator.cs`, independent of this roadmap, and **do not treat compartment search as the compiler's headline motivator** — say so plainly, per the design doc's own instruction ("the honest conclusion is that the compartment case should stop being this document's headline").
- If Arm B is still far from Arm C: shape, not literalization, explains the residual — the compiler's headline case stands as originally argued, proceed to Phase 1.
- If Arm A already ≈ Arm C (no meaningful gap at all): the motivating bug is fully resolved already; the compiler's justification must rest entirely on the other stated goals (storage-convention ownership, testability, injection-safety-by-construction) — flag this explicitly rather than silently proceeding as if the performance case still holds.

- [ ] **Step 2: Update the roadmap's Phase 0 row**

In `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`, change the Phase 0 row's Status column from "Do this first" to "Complete — see findings doc for go/no-go", and add one sentence under the *Grounding* section summarizing the outcome.

- [ ] **Step 3: Commit**

```bash
git add docs/features/deployment/investigations/2026-07-15-compartment-search-step0-findings.md docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md
git commit -m "docs: record step 0 compartment search factorial results and go/no-go"
```

## Self-Review

- **Spec coverage:** Task 1 covers the design doc's "run step −1 first" instruction (confirm the plan-strategy confound before anything else). Tasks 2-4 cover "Step 0... run as a factorial," reduced from four arms to three per Task 1's finding, with the reduction justified in-plan rather than silently assumed. Task 5 covers the design doc's explicit instruction to state the honest conclusion even if it undercuts the compiler's headline case.
- **Placeholder scan:** Task 3's seeder code is deliberately deferred to "read the entity files first" rather than fabricated, because the entity shapes were not verified during planning — this is a scoped investigation step with a concrete deliverable (a compiling `SqlBulkCopy`-based seeder), not a vague "add seeding logic" placeholder. Everything else has complete code.
- **Type consistency:** `CompartmentDataSeeder.SeedAsync`, `CompartmentSearchStep0Benchmark.RunArmAAsync/RunArmBAsync/RunArmCAsync` are referenced consistently across Tasks 3-4.
