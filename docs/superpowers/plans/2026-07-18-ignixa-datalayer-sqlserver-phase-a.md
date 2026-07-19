# Ignixa.DataLayer.SqlServer Phase A: Project Skeleton + Connection/Execution Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up a new `Ignixa.DataLayer.SqlServer` project — raw ADO.NET, no EF Core, no ORM — with a tenant-scoped SQL execution service (`ISqlExecutionService`), proven against a real SQL Server instance. Zero production wiring; nothing else depends on this project existing yet.

**Architecture:** `ISqlExecutionService` mirrors real fhir-server's `ISqlRetryService` shape (verified against the actual interface, not a summary) but adapted for Ignixa's database-per-tenant multi-tenancy: every call is scoped by `tenantId`, and the service resolves that tenant's connection string via the existing `ITenantConfigurationStore` before opening a connection — fhir-server's version never needed this, since it's single-database-per-deployment. Retry policy uses Polly (already a repo-wide dependency, not a new one). Two tasks split the concern cleanly: connection resolution (testable with a fake `ITenantConfigurationStore`, no real database needed) from execution-with-retry (needs a real SQL Server instance to prove against genuinely).

**Tech Stack:** C# / .NET 10 (single-target, matching `Ignixa.DataLayer.SqlEntityFramework`'s convention, not Core-tier's net9.0;net10.0 multi-target), `Microsoft.Data.SqlClient` 6.1.4, Polly 8.6.6, xUnit + Shouldly, the existing `docker-compose.test.yml` SQL Server container for integration tests.

**Full design:** `docs/superpowers/specs/2026-07-18-ignixa-datalayer-sqlserver-design.md` — read this first for the *why*; this plan covers only Phase A (§3). Phases B–F (SQL Database Projects, schema-version compatibility, write-path migration, read cutover, retiring `SqlEntityFramework`) are explicitly out of scope for this plan and get their own plans later.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures are known and out of scope, per every prior increment on this branch.
- **Zero production-facing change**: this plan must not touch `Ignixa.Api`, `Ignixa.Application` (including `DataLayerRegistration.cs`), or `Ignixa.DataLayer.SqlEntityFramework`'s existing code. The new project is buildable and testable in complete isolation — nothing registers it in any DI container yet (that happens in a later phase, when something actually consumes it).
- **No EF Core, no ORM reference of any kind** in `Ignixa.DataLayer.SqlServer.csproj` — raw `Microsoft.Data.SqlClient` only. This is a hard architectural constraint (design doc §4), not a style preference; a task reviewer should treat any EF/ORM package reference in this project as an automatic Important finding.
- **`ISqlExecutionService`'s real fhir-server counterpart** (`Microsoft.Health.Fhir.SqlServer.Features.Storage.ISqlRetryService`, confirmed directly against the local `C:\src\fhir-server` checkout): `Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)`; `Task ExecuteSql(Func<SqlConnection, CancellationToken, SqlException, Task> action, ILogger logger, CancellationToken cancellationToken, bool isReadOnly = false)`; `Task ExecuteSql(SqlCommand sqlCommand, Func<SqlCommand, CancellationToken, Task> action, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false, bool disableRetries = false, string applicationName = null)`; `Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(SqlCommand sqlCommand, Func<SqlDataReader, TResult> readerToResult, ILogger logger, string logMessage, CancellationToken cancellationToken, bool isReadOnly = false)`; `Task<IReadOnlyList<IReadOnlyList<TResult>>> ExecuteMultiResultReaderAsync<TResult>(...)`. **Ignixa's version deliberately diverges**: fhir-server's shape assumes ONE database (its connection factory is bound once, at startup); Ignixa needs a `tenantId` on every call, since one running instance serves N independent tenant databases (design doc §1/§6). No `isReadOnly` parameter (read-replica routing explicitly deferred, design doc §4/§7) — do not add it, even as an unused parameter.
- **`ITenantConfigurationStore`'s real current shape** (`src/Application/Ignixa.Domain/Abstractions/ITenantConfigurationStore.cs`): `ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)`. `TenantConfiguration.Storage.ConnectionString` (`src/Application/Ignixa.Domain/Models/TenantConfiguration.cs`) is the per-tenant SQL Server connection string; `TenantConfiguration.Storage.Type` is a string (`"FileSystem"`, `"SqlServer"`, `"CosmosDb"`) — the execution service must check `Type == "SqlServer"` before attempting to use `ConnectionString`, and throw a clear, loud error (not a null-ref or silent no-op) if asked to execute SQL against a tenant not configured for SQL storage.
- **Retry policy uses Polly** (`PackageVersion Include="Polly" Version="8.6.6"` already in `Directory.Packages.props:96`) — do not add a new retry library; this repo already depends on Polly.
- **`Microsoft.Data.SqlClient` version**: `6.1.4`, already centrally pinned in `Directory.Packages.props:49` — reference it via `<PackageReference Include="Microsoft.Data.SqlClient" />` (no version attribute, matching this repo's central-package-management convention used by every other project).
- **`EmittedSql`'s real shape** (`src/Core/Ignixa.Search.Sql/Ast/EmittedSql.cs`, for Task 3's execution-helper method signatures to cleanly accept later, in a future phase — Phase A does NOT wire this up, only shapes its methods so a later phase can bind cleanly): `public sealed record EmittedSql(string Sql, IReadOnlyList<EmittedSqlParameter> Parameters)`, `public sealed record EmittedSqlParameter(string Name, object Value)`.
- **The real integration-test SQL Server pattern** (confirmed against `docker-compose.test.yml` and `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/DatabaseSchemaInitializationTests.cs`): tests read a required `TEST_SQL_CONNECTION_STRING` environment variable (throwing `InvalidOperationException` with a clear message if unset, pointing at `docker-compose.test.yml`) — never a hardcoded fallback connection string. CI (`.github/workflows/pr-build.yml:110`) sets this to `"Server=localhost,1433;Database=FhirTest;User Id=sa;Password=${{ env.SQL_SA_PASSWORD }};TrustServerCertificate=true;Encrypt=false"`. This plan's own integration tests must use the identical `Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")` pattern, not a new mechanism.
- **Solution registration via `dotnet sln add`, not hand-edited GUIDs.** `All.sln` uses a solution-folder structure (existing `DataLayer` folder, GUID `{3AB9C3B3-CB6C-414B-87A7-CAFB3AC9FAAA}`) that `dotnet sln add` handles automatically when pointed at a path under `src\DataLayer\`; do not hand-craft `Project(...)`/`GlobalSection(ProjectConfigurationPlatforms)`/`NestedProjects` entries in the plan or its tasks — always use the CLI.
- This plan runs directly in the git worktree already created for this whole initiative: `C:\src\ignixa-fhir\.claude\worktrees\ignixa-datalayer-sqlserver` (branch `worktree-ignixa-datalayer-sqlserver`, based on `feature/fhir-to-sql-compiler`). Do not create a nested worktree.
- **Testing discipline**: exact assertions (real SQL text, real parameter values, real row counts/values read back), never loose non-null checks — matching this project's established discipline throughout the `fhir-to-sql-compiler` work.

---

### Task 1: Project scaffolding — `Ignixa.DataLayer.SqlServer`

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/AssemblyMarker.cs` (a trivial, real class — proves the project builds and produces a real assembly; deleted/replaced once Task 2 adds real content, per Step 5 below)
- Modify: `All.sln` (via `dotnet sln add`, not hand-edited)

**Interfaces:**
- Consumes: nothing (first task).
- Produces: a building, empty (except the marker) `Ignixa.DataLayer.SqlServer` project registered in `All.sln` under the `DataLayer` solution folder. Task 2 adds real content to this project.

- [ ] **Step 1: Create the project directory and csproj**

```bash
mkdir -p src/DataLayer/Ignixa.DataLayer.SqlServer
```

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <!-- Raw ADO.NET only -- no EF Core, no ORM of any kind (design doc §4: hard architectural constraint) -->
    <PackageReference Include="Microsoft.Data.SqlClient" />
    <PackageReference Include="Polly" />

    <!-- Logging -->
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <!-- Project references -->
    <ProjectReference Include="..\..\Application\Ignixa.Domain\Ignixa.Domain.csproj" />
  </ItemGroup>

</Project>
```

(`Ignixa.Domain` is referenced for `ITenantConfigurationStore`/`TenantConfiguration`, which Task 2 needs — confirm this project reference resolves cleanly; if `Ignixa.Domain`'s own csproj has a different relative path than shown, correct it, but the `../../Application/Ignixa.Domain/Ignixa.Domain.csproj` path matches this repo's established `src/DataLayer/*` → `src/Application/*` reference pattern, e.g. `Ignixa.DataLayer.SqlEntityFramework.csproj`'s own reference to the same project.)

- [ ] **Step 2: Add a real, minimal marker class**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/AssemblyMarker.cs`:

```csharp
namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Marks this assembly as existing and buildable before any real functionality lands (Task 1 of
/// Phase A). Removed once Task 2 adds ISqlExecutionService -- a project with zero types is not a
/// meaningful "it builds" proof; this gives the build something concrete to compile and this task's
/// test something concrete to assert against.
/// </summary>
public static class AssemblyMarker
{
    public const string ProjectName = "Ignixa.DataLayer.SqlServer";
}
```

- [ ] **Step 3: Register the project in the solution**

```bash
dotnet sln All.sln add src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj
```

- [ ] **Step 4: Verify the build**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`
Expected: 0 warnings, 0 errors.

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors — confirms the new project doesn't break anything else in the solution and is correctly wired in.

- [ ] **Step 5: Write a trivial test proving the project is real and registered**

Create `test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj` (matches the real, confirmed shape of `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj`, read directly during this plan's writing — note the global `Using Include="Xunit"`, which means test files below use bare `[Fact]` with no `using Xunit;` line):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj" />
  </ItemGroup>

</Project>
```

Create `test/Ignixa.DataLayer.SqlServer.Tests/AssemblyMarkerTests.cs`:

```csharp
namespace Ignixa.DataLayer.SqlServer.Tests;

public class AssemblyMarkerTests
{
    [Fact]
    public void GivenTheProject_WhenBuilt_ThenAssemblyMarkerExposesTheProjectName()
    {
        // Arrange & Act
        var name = AssemblyMarker.ProjectName;

        // Assert
        name.ShouldBe("Ignixa.DataLayer.SqlServer");
    }
}
```

```bash
dotnet sln All.sln add test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj
```

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj`
Expected: 1 test, PASS.

- [ ] **Step 6: Confirm no EF Core / ORM reference exists**

Run: `grep -i "EntityFramework\|Dapper\|NHibernate" src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj`
Expected: no output (zero matches). This is the concrete, mechanical proof of the "no ORM" constraint — record this check's clean output in the task's own report, since a future reviewer will re-run it.

- [ ] **Step 7: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/ test/Ignixa.DataLayer.SqlServer.Tests/ All.sln
git commit -m "feat(datalayer-sqlserver): scaffold Ignixa.DataLayer.SqlServer project"
```

---

### Task 2: Tenant-scoped connection resolution

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlExecutionService.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SqlExecutionServiceConnectionTests.cs`

**Interfaces:**
- Consumes: `ITenantConfigurationStore`/`TenantConfiguration` (existing, `Ignixa.Domain`).
- Produces: `ISqlExecutionService` (the interface, full shape defined here even though Task 3 implements the execute methods), `SqlExecutionService`'s connection-resolution logic. Task 3 adds the execute-with-retry methods to this same class.

- [ ] **Step 1: Define `ISqlExecutionService`'s full interface shape**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlExecutionService.cs`:

```csharp
using Microsoft.Data.SqlClient;

namespace Ignixa.DataLayer.SqlServer;

/// <summary>
/// Executes SQL against a specific tenant's database, with retry and structured logging. Mirrors
/// real fhir-server's ISqlRetryService shape (Microsoft.Health.Fhir.SqlServer.Features.Storage),
/// adapted for Ignixa's database-per-tenant multi-tenancy: fhir-server's version is bound to one
/// connection factory at startup (single-database-per-deployment); every method here takes a
/// tenantId, since one running instance serves N independent tenant databases (design doc §1/§6).
/// No isReadOnly parameter -- read-replica routing is explicitly deferred (design doc §4/§7).
/// </summary>
public interface ISqlExecutionService
{
    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database and reads
    /// every result row via <paramref name="readRow"/>. Opens and disposes its own connection.
    /// </summary>
    Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken);

    /// <summary>
    /// Executes <paramref name="command"/> against <paramref name="tenantId"/>'s database as a
    /// non-query (INSERT/UPDATE/DELETE/DDL) and returns the affected row count.
    /// </summary>
    Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement connection resolution in `SqlExecutionService`**

Create `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`:

```csharp
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer;

public sealed class SqlExecutionService : ISqlExecutionService
{
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly ILogger<SqlExecutionService> _logger;

    public SqlExecutionService(ITenantConfigurationStore tenantConfigurationStore, ILogger<SqlExecutionService> logger)
    {
        ArgumentNullException.ThrowIfNull(tenantConfigurationStore);
        ArgumentNullException.ThrowIfNull(logger);
        _tenantConfigurationStore = tenantConfigurationStore;
        _logger = logger;
    }

    private async Task<SqlConnection> OpenConnectionAsync(int tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenantConfigurationStore.GetTenantConfigurationAsync(tenantId, cancellationToken);
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

        var connection = new SqlConnection(tenant.Storage.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Added in Task 3.");

    public Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("Added in Task 3.");
}
```

(The two interface methods throw `NotImplementedException` deliberately in this task — Task 3 replaces both bodies. This task's own tests below only exercise `OpenConnectionAsync` indirectly, via a thin internal-visibility test seam described in Step 3; do not test the `NotImplementedException` methods here, that's Task 3's job.)

To make `OpenConnectionAsync` testable from this task's own test project without exposing it as public API, add to `Ignixa.DataLayer.SqlServer.csproj`'s `<ItemGroup>` (the one with `<PackageReference>`s, or a new one):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Ignixa.DataLayer.SqlServer.Tests" />
</ItemGroup>
```

and change `OpenConnectionAsync`'s access modifier from `private` to `internal`.

- [ ] **Step 3: Write the connection-resolution tests**

Create `test/Ignixa.DataLayer.SqlServer.Tests/SqlExecutionServiceConnectionTests.cs`. These tests use a fake `ITenantConfigurationStore` (no real database needed — connection resolution is pure logic up to the point of actually opening a socket, and `OpenAsync` against a genuinely bad connection string will throw, which is exactly what the "inactive tenant"/"wrong storage type"/"missing connection string" tests below assert on, without needing a real server):

```csharp
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.Tests;

public class SqlExecutionServiceConnectionTests
{
    private sealed class FakeTenantConfigurationStore : ITenantConfigurationStore
    {
        public Dictionary<int, TenantConfiguration> Tenants { get; } = new();

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(Tenants.TryGetValue(tenantId, out var config) ? config : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)Tenants.Values.ToList());
    }

    [Fact]
    public async Task GivenATenantThatDoesNotExist_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(999, CancellationToken.None));
        ex.Message.ShouldContain("999");
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("FileSystem");
        ex.Message.ShouldContain("SqlServer");
    }

    [Fact]
    public async Task GivenATenantConfiguredForSqlServerWithNoConnectionString_WhenOpeningAConnection_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var store = new FakeTenantConfigurationStore();
        store.Tenants[1] = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = null },
        };
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);

        // Act & Assert
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            service.OpenConnectionAsync(1, CancellationToken.None));
        ex.Message.ShouldContain("ConnectionString");
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet build src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj` — expect 0 warnings, 0 errors.
Run: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj` — expect 4 tests (the 1 from Task 1 plus these 3), all passing.

- [ ] **Step 5: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/ISqlExecutionService.cs src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj test/Ignixa.DataLayer.SqlServer.Tests/SqlExecutionServiceConnectionTests.cs
git commit -m "feat(datalayer-sqlserver): add ISqlExecutionService with tenant-scoped connection resolution"
```

---

### Task 3: Execution helpers with retry, against a real SQL Server instance

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`
- Test: `test/Ignixa.DataLayer.SqlServer.Tests/SqlExecutionServiceExecutionTests.cs` (a NEW project, `Ignixa.DataLayer.SqlServer.IntegrationTests`, per Step 1 below — this task's tests need a real SQL Server instance, unlike Task 1-2's unit tests)

**Interfaces:**
- Consumes: `ISqlExecutionService`, `SqlExecutionService.OpenConnectionAsync` (Task 2).
- Produces: `ISqlExecutionService.ExecuteReaderAsync<TResult>`/`ExecuteNonQueryAsync`'s real implementations. Task 4's combined-proof tests are the primary consumer of the fully-working service.

- [ ] **Step 1: Create the integration test project**

Real SQL Server-dependent tests belong in their own project (matching this repo's existing split between `test/Ignixa.DataLayer.SqlEntityFramework.Tests` [unit] and `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests` [real database] — confirm this split exists for that project before mirroring it, since this plan's Task 1-2 tests deliberately did NOT need a real database and lived in the plain `.Tests` project).

```bash
mkdir -p test/Ignixa.DataLayer.SqlServer.IntegrationTests
```

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj` (matches the real, confirmed shape of `test/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests/Ignixa.DataLayer.SqlEntityFramework.IntegrationTests.csproj` — note the global `Using Include="Xunit"` and the already-present `Microsoft.Data.SqlClient` reference, since this project's own tests construct `SqlCommand` directly):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.Data.SqlClient" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/DataLayer/Ignixa.DataLayer.SqlServer/Ignixa.DataLayer.SqlServer.csproj" />
  </ItemGroup>

</Project>
```

```bash
dotnet sln All.sln add test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj
```

- [ ] **Step 2: Implement `ExecuteReaderAsync`/`ExecuteNonQueryAsync` with Polly retry**

In `src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs`, replace both `NotImplementedException`-throwing method bodies:

```csharp
    private static readonly ResiliencePipeline TransientFaultPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqlException>(IsTransient),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(200),
            BackoffType = DelayBackoffType.Exponential,
        })
        .Build();

    private static bool IsTransient(SqlException ex) => IsTransient(ex.Number);

    // Transient SQL Server error numbers: -2 (timeout), 4060 (cannot open database, may be
    // transient during failover), 40197/40501/40613 (Azure SQL throttling/failover), 10928/10929
    // (Azure SQL resource limits), 1205 (deadlock victim). Internal (not private) so Task 4's test
    // can assert on it directly without needing to construct a real SqlException, which has no
    // public constructor with a settable Number.
    internal static bool IsTransient(int sqlErrorNumber)
        => sqlErrorNumber is -2 or 1205 or 4060 or 10928 or 10929 or 40197 or 40501 or 40613;

    public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
        int tenantId,
        SqlCommand command,
        Func<SqlDataReader, TResult> readRow,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(readRow);

        return await TransientFaultPipeline.ExecuteAsync(async ct =>
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var results = new List<TResult>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                results.Add(readRow(reader));
            }

            _logger.LogDebug("Executed reader for tenant {TenantId}, {RowCount} row(s)", tenantId, results.Count);
            return (IReadOnlyList<TResult>)results;
        }, cancellationToken);
    }

    public async Task<int> ExecuteNonQueryAsync(
        int tenantId,
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await TransientFaultPipeline.ExecuteAsync(async ct =>
        {
            await using var connection = await OpenConnectionAsync(tenantId, ct);
            command.Connection = connection;

            var affected = await command.ExecuteNonQueryAsync(ct);
            _logger.LogDebug("Executed non-query for tenant {TenantId}, {AffectedRows} row(s) affected", tenantId, affected);
            return affected;
        }, cancellationToken);
    }
```

Add `using Polly;` and `using Polly.Retry;` to the file's usings.

- [ ] **Step 3: Write the execution tests against the real SQL Server container**

Create `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlExecutionServiceExecutionTests.cs`:

```csharp
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

public class SqlExecutionServiceExecutionTests
{
    private sealed class SingleTenantStore : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;

        public SingleTenantStore(string connectionString)
        {
            _tenant = new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0",
                Storage = new TenantStorageConfiguration { Type = "SqlServer", ConnectionString = connectionString },
            };
        }

        public TenantMode Mode => TenantMode.Isolated;

        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == 1 ? _tenant : null);

        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });
    }

    private static string GetConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "TEST_SQL_CONNECTION_STRING must be set to run this test (see docker-compose.test.yml).");
        }

        return connectionString;
    }

    private static SqlExecutionService CreateService()
        => new(new SingleTenantStore(GetConnectionString()), NullLogger<SqlExecutionService>.Instance);

    [Fact]
    public async Task GivenASimpleSelectQuery_WhenExecutedViaExecuteReaderAsync_ThenReturnsTheExpectedRow()
    {
        // Arrange
        var service = CreateService();
        var command = new SqlCommand("SELECT 1 AS Value, 'hello' AS Text");

        // Act
        var results = await service.ExecuteReaderAsync(
            tenantId: 1,
            command,
            reader => (Value: reader.GetInt32(0), Text: reader.GetString(1)),
            CancellationToken.None);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Value.ShouldBe(1);
        results[0].Text.ShouldBe("hello");
    }

    [Fact]
    public async Task GivenAQueryWithNoRows_WhenExecutedViaExecuteReaderAsync_ThenReturnsAnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var command = new SqlCommand("SELECT 1 AS Value WHERE 1 = 0");

        // Act
        var results = await service.ExecuteReaderAsync(
            tenantId: 1,
            command,
            reader => reader.GetInt32(0),
            CancellationToken.None);

        // Assert
        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAParameterizedCreateAndInsert_WhenExecutedViaExecuteNonQueryAsync_ThenAffectsOneRowAndIsQueryable()
    {
        // Arrange
        var service = CreateService();
        var tableName = $"##ExecTest_{Guid.NewGuid():N}"; // global temp table, visible across the pooled connections this test opens
        var create = new SqlCommand($"CREATE TABLE {tableName} (Id INT NOT NULL, Name NVARCHAR(50) NOT NULL)");
        var insert = new SqlCommand($"INSERT INTO {tableName} (Id, Name) VALUES (@id, @name)");
        insert.Parameters.AddWithValue("@id", 42);
        insert.Parameters.AddWithValue("@name", "test-row");

        // Act
        await service.ExecuteNonQueryAsync(tenantId: 1, create, CancellationToken.None);
        var affected = await service.ExecuteNonQueryAsync(tenantId: 1, insert, CancellationToken.None);

        var select = new SqlCommand($"SELECT Id, Name FROM {tableName}");
        var rows = await service.ExecuteReaderAsync(
            tenantId: 1,
            select,
            reader => (Id: reader.GetInt32(0), Name: reader.GetString(1)),
            CancellationToken.None);

        // Assert
        affected.ShouldBe(1);
        rows.Count.ShouldBe(1);
        rows[0].Id.ShouldBe(42);
        rows[0].Name.ShouldBe("test-row");

        // Cleanup -- global temp tables persist until the last referencing session closes; drop
        // explicitly so a failed run doesn't leak state into the next.
        var drop = new SqlCommand($"IF OBJECT_ID('tempdb..{tableName}') IS NOT NULL DROP TABLE {tableName}");
        await service.ExecuteNonQueryAsync(tenantId: 1, drop, CancellationToken.None);
    }

    [Fact]
    public async Task GivenATenantConfiguredForFileSystemStorage_WhenExecutingAQuery_ThenThrowsInvalidOperationExceptionWithoutRetrying()
    {
        // Arrange -- confirms the Task 2 validation guard still fires correctly once wrapped in the
        // Task 3 retry pipeline (a non-transient InvalidOperationException must not be retried).
        var fileSystemTenant = new TenantConfiguration
        {
            TenantId = 2,
            DisplayName = "FileSystem Tenant",
            FhirVersion = "4.0",
            Storage = new TenantStorageConfiguration { Type = "FileSystem" },
        };
        var store = new FakeStoreWithOneTenant(fileSystemTenant);
        var service = new SqlExecutionService(store, NullLogger<SqlExecutionService>.Instance);
        var command = new SqlCommand("SELECT 1");

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            service.ExecuteReaderAsync(2, command, reader => reader.GetInt32(0), CancellationToken.None));
    }

    private sealed class FakeStoreWithOneTenant : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant;
        public FakeStoreWithOneTenant(TenantConfiguration tenant) => _tenant = tenant;
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default)
            => new(tenantId == _tenant.TenantId ? _tenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default)
            => new((IReadOnlyList<TenantConfiguration>)new List<TenantConfiguration> { _tenant });
    }
}
```

- [ ] **Step 4: Run the tests against the real SQL Server container**

Start the container (if not already running for this session): `docker compose -f docker-compose.test.yml up -d sqlserver`

Set the connection string and run (adjust the shell syntax to match your environment — PowerShell `$env:TEST_SQL_CONNECTION_STRING = "..."` or bash `export TEST_SQL_CONNECTION_STRING="..."`), matching CI's exact value from `.github/workflows/pr-build.yml:110`:

```
Server=localhost,1433;Database=FhirTest;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true;Encrypt=false
```

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests/Ignixa.DataLayer.SqlServer.IntegrationTests.csproj`
Expected: 4 tests, all PASS.

Also re-run the Task 1-2 unit tests to confirm nothing broke: `dotnet test test/Ignixa.DataLayer.SqlServer.Tests/Ignixa.DataLayer.SqlServer.Tests.csproj` — expect all 4 still passing.

- [ ] **Step 5: Commit**

```bash
git add src/DataLayer/Ignixa.DataLayer.SqlServer/SqlExecutionService.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/ All.sln
git commit -m "feat(datalayer-sqlserver): implement ExecuteReaderAsync/ExecuteNonQueryAsync with Polly retry"
```

---

### Task 4: Combined proof + final regression + review prep

**Files:** none (verification only, plus a small proof test), plus a roadmap-style note in the design doc.

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: a clean `dotnet build All.sln`/`dotnet test All.sln` baseline and a review package for the final whole-branch review.

- [ ] **Step 1: Write one combined-proof test showing retry configuration is genuinely wired, not just present**

Add to `test/Ignixa.DataLayer.SqlServer.IntegrationTests/SqlExecutionServiceExecutionTests.cs`:

```csharp
    [Fact]
    public async Task GivenASqlErrorNumber_WhenClassifiedByIsTransient_ThenTheDocumentedTransientNumbersAreTrueAndOthersAreFalse()
    {
        // This does not simulate a real transient failure against the live container (SQL Server
        // doesn't offer a clean way to inject one on demand); it directly proves
        // SqlExecutionService.IsTransient(int) classifies the documented transient error numbers
        // correctly, which is the actual decision the retry pipeline's ShouldHandle predicate depends
        // on (Task 3 Step 2: IsTransient(SqlException) delegates to this same int overload).

        // Act & Assert -- deadlock victim, connection timeout, and Azure SQL throttling are all transient.
        SqlExecutionService.IsTransient(1205).ShouldBeTrue();
        SqlExecutionService.IsTransient(-2).ShouldBeTrue();
        SqlExecutionService.IsTransient(40197).ShouldBeTrue();

        // A generic syntax error or constraint violation is not.
        SqlExecutionService.IsTransient(547).ShouldBeFalse();
    }
```

(`IsTransient(int)` is already `internal` per Task 3 Step 2 and already covered by Task 2's `InternalsVisibleTo` entry — no new test seam needed for this task.)

- [ ] **Step 2: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Full solution test**

Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`
Expected: all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures. `Ignixa.DataLayer.SqlServer.Tests` and `Ignixa.DataLayer.SqlServer.IntegrationTests` (the SQL Server container must be running for the latter, per Task 3 Step 4) should both be fully green.

- [ ] **Step 4: Confirm zero production-facing change**

Run: `grep -rln "Ignixa.DataLayer.SqlServer" src/Application/ src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/ 2>/dev/null`
Expected: no output — confirms nothing outside the new project itself references it yet (no DI registration, no consumption anywhere), matching the "zero production-facing change" Global Constraint.

- [ ] **Step 5: Prepare the final whole-branch review package**

Follow `superpowers:subagent-driven-development`'s final-review step: run `scripts/review-package MERGE_BASE HEAD` (`MERGE_BASE` = `git merge-base feature/fhir-to-sql-compiler HEAD`) and dispatch the final whole-branch reviewer on the most capable available model. Ask the reviewer to independently confirm: (a) no EF/ORM reference exists anywhere in the new project (re-run Task 1 Step 6's grep); (b) the retry pipeline's transient-error classification is sound (cross-check the error-number list against Microsoft's own documented transient-fault guidance for Azure SQL/SQL Server, not just this plan's own list); (c) connection resolution correctly fails loudly (not silently) for every invalid-tenant-state case in Task 2's tests.

- [ ] **Step 6: Report to the user before merging or pushing**

Summarize what shipped (the project skeleton, `ISqlExecutionService` with tenant-scoped connection resolution and retry, proven against a real SQL Server instance) and that this is Phase A of six — Phase B (SQL Database Projects) is next, per the design doc. Ask before merging into `feature/fhir-to-sql-compiler` and again before pushing.
