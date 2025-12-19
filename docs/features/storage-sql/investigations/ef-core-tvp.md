# Investigation: EF Core TVP & Stored Procedure Support Analysis

**Feature**: sql-storage
**Status**: Complete
**Created**: 2025-10-22
**Original ADR**: N/A
**EF Core Version**: 9.0.0

## Executive Summary

**Can EF Core call a Merge stored procedure with TVPs as a one-off operation?**

**Answer**: ✅ **YES, but with limitations**

- ✅ EF Core 9.0 can call stored procedures via `FromSql()` / `ExecuteSql()`
- ✅ You can manually construct `SqlParameter` objects with TVPs
- ❌ EF Core does NOT provide built-in TVP marshaling (you still need row generators)
- ✅ **Hybrid approach works well**: EF for schema/migrations + raw SQL for bulk Merge

---

## 1. EF Core Stored Procedure Calling Patterns

### Pattern 1: Query-based (FromSql)

```csharp
// Returns IQueryable<TResult>
var results = await context.Resources
    .FromSql($"EXECUTE dbo.GetResourcesByType {resourceType}")
    .ToListAsync();
```

**Use case**: When stored procedure returns entity results that map to DbSet

**Pros**:
- Auto-maps to entity model
- Still works with EF tracking
- Can chain `.Where()`, `.OrderBy()` etc.

**Cons**:
- Not ideal for write operations
- TVP binding still requires manual setup

### Pattern 2: Non-query (ExecuteSql)

```csharp
// Returns number of affected rows
var affectedRows = await context.Database.ExecuteSqlAsync(
    $"EXECUTE dbo.MergeResources " +
    $"@TransactionId = {transactionId}, " +
    $"@Resources = {resourcesTvp}, " +
    $"@TokenSearchParams = {tokenParamsTvp}",
    cancellationToken);
```

**Use case**: Write operations (INSERT, UPDATE, DELETE, stored procedures)

**Pros**:
- Simple for parameter passing
- No entity mapping overhead
- Works with any stored procedure

**Cons**:
- You manage all parameter binding
- No auto-generated types

### Pattern 3: Raw DbCommand

```csharp
// Maximum control, manual everything
using (var command = context.Database.GetDbConnection().CreateCommand())
{
    command.CommandText = "dbo.MergeResources";
    command.CommandType = CommandType.StoredProcedure;

    // Create TVP parameter
    var resourcesTvp = new SqlParameter("@Resources", SqlDbType.Structured)
    {
        TypeName = "dbo.ResourceListTableType",
        Value = ConvertToDataTable(resources)
    };
    command.Parameters.Add(resourcesTvp);

    await context.Database.OpenConnectionAsync();
    await command.ExecuteNonQueryAsync();
}
```

**Use case**: Complete control needed, custom TVP handling

**Pros**:
- Full control over SQL execution
- Best for complex TVPs
- No EF constraints

**Cons**:
- Most code to write
- Manually manage connection
- No EF integration

---

## 2. TVP Support in EF Core 9.0

### The Reality

EF Core 9.0 **does NOT have built-in TVP support**. You must:

1. **Create TVP types in SQL** (via migrations or raw SQL)
2. **Manually construct DataTable** from your C# objects
3. **Create SqlParameter** with `SqlDbType.Structured`
4. **Pass to stored procedure** via `ExecuteSql` or `DbCommand`

### What This Means for Your Use Case

```csharp
// ❌ EF Core DOESN'T do this automatically:
var resources = new[] { resource1, resource2, resource3 };
await context.Database.ExecuteSqlAsync(
    "EXEC dbo.MergeResources @Resources",
    resources);  // ❌ EF doesn't know how to convert to TVP

// ✅ YOU must do this manually:
var resourcesTvp = new SqlParameter("@Resources", SqlDbType.Structured)
{
    TypeName = "dbo.ResourceListTableType",
    Value = ConvertCsharpsToDataTable(resources)  // ← Manual conversion
};

await context.Database.ExecuteSqlAsync(
    $"EXECUTE dbo.MergeResources @Resources = {resourcesTvp}",
    resourcesTvp);  // ← Pass parameter explicitly
```

---

## 3. Hybrid EF Core + Stored Procedure Pattern

### Recommended Approach for Ignixa

**Leverage EF Core for**:
- Schema management (migrations)
- Entity definitions
- Connection management
- Transaction coordination

**Use raw SQL/stored procedures for**:
- Merge bulk operations
- Complex multi-step transactions
- Performance-critical operations

### Implementation Pattern

```csharp
public class SqlMergeRepository
{
    private readonly FhirDbContext _context;
    private readonly GzipResourceCompressor _compressor;
    private readonly ILogger<SqlMergeRepository> _logger;

    public SqlMergeRepository(
        FhirDbContext context,
        GzipResourceCompressor compressor,
        ILogger<SqlMergeRepository> logger)
    {
        _context = context;
        _compressor = compressor;
        _logger = logger;
    }

    /// <summary>
    /// Merge resources using stored procedure with TVPs.
    /// Leverages EF Core for schema/connection but uses raw SQL for bulk operation.
    /// </summary>
    public async Task<int> MergeResourcesAsync(
        long transactionId,
        List<ResourceWrapper> resources,
        bool singleTransaction,
        CancellationToken cancellationToken)
    {
        // 1. Build TVP parameters from resources (your row generators)
        var resourcesTvp = new SqlParameter("@Resources", SqlDbType.Structured)
        {
            TypeName = "dbo.ResourceListTableType",
            Value = BuildResourceDataTable(resources)
        };

        var tokenParamsTvp = new SqlParameter("@TokenSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenSearchParamListTableType",
            Value = BuildTokenSearchParamDataTable(resources)
        };

        var referenceParamsTvp = new SqlParameter("@ReferenceSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.ReferenceSearchParamListTableType",
            Value = BuildReferenceSearchParamDataTable(resources)
        };

        // ... 14 more TVP parameters ...

        // 2. Call stored procedure via EF Core
        var affectedRows = await _context.Database.ExecuteSqlAsync(
            $"""
            EXECUTE dbo.MergeResources
                @TransactionId = {transactionId},
                @SingleTransaction = {singleTransaction},
                @IsResourceChangeCaptureEnabled = {false},
                @Resources = {resourcesTvp},
                @TokenSearchParams = {tokenParamsTvp},
                @ReferenceSearchParams = {referenceParamsTvp}
                -- ... 14 more parameters ...
            """,
            new[] { resourcesTvp, tokenParamsTvp, referenceParamsTvp /* ... */ },
            cancellationToken);

        _logger.LogInformation(
            "Merge completed: TransactionId={TransactionId}, AffectedRows={AffectedRows}",
            transactionId, affectedRows);

        return affectedRows;
    }

    // Row generator helpers
    private DataTable BuildResourceDataTable(List<ResourceWrapper> resources)
    {
        var table = new DataTable("dbo.ResourceListTableType");
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceId", typeof(string));
        table.Columns.Add("Version", typeof(int));
        table.Columns.Add("IsHistory", typeof(bool));
        table.Columns.Add("IsDeleted", typeof(bool));
        table.Columns.Add("RawResource", typeof(byte[]));
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        // ... more columns ...

        foreach (var resource in resources)
        {
            table.Rows.Add(
                GetResourceTypeId(resource.ResourceType),
                resource.ResourceId,
                int.Parse(resource.Version),
                resource.IsHistory,
                resource.IsDeleted,
                _compressor.CompressRawResource(resource.RawResource),
                resource.ResourceSurrogateId);
        }

        return table;
    }

    private DataTable BuildTokenSearchParamDataTable(List<ResourceWrapper> resources)
    {
        var table = new DataTable("dbo.TokenSearchParamListTableType");
        table.Columns.Add("ResourceSurrogateId", typeof(long));
        table.Columns.Add("SearchParamId", typeof(short));
        table.Columns.Add("SystemId", typeof(short));
        table.Columns.Add("Code", typeof(string));
        // ... more columns ...

        foreach (var resource in resources)
        {
            foreach (var tokenParam in ExtractTokenSearchParams(resource))
            {
                table.Rows.Add(
                    resource.ResourceSurrogateId,
                    tokenParam.SearchParamId,
                    tokenParam.SystemId,
                    tokenParam.Code);
            }
        }

        return table;
    }

    // ... similar helpers for other TVP types ...
}
```

---

## 4. Step-by-Step: How to Call Merge SP with EF Core

### Step 1: Create TVP Types in SQL

```sql
-- Via EF Core migration or raw SQL
CREATE TYPE dbo.ResourceListTableType AS TABLE
(
    ResourceTypeId SMALLINT NOT NULL,
    ResourceId VARCHAR(64) NOT NULL,
    Version INT NOT NULL,
    IsHistory BIT NOT NULL,
    IsDeleted BIT NOT NULL,
    RawResource VARBINARY(MAX) NOT NULL,
    ResourceSurrogateId BIGINT NOT NULL PRIMARY KEY,
    -- ... more columns ...
);

CREATE TYPE dbo.TokenSearchParamListTableType AS TABLE
(
    ResourceSurrogateId BIGINT NOT NULL,
    SearchParamId SMALLINT NOT NULL,
    SystemId SMALLINT,
    Code VARCHAR(256) NOT NULL,
    -- ... more columns ...
);

-- ... 15 more TVP type definitions ...
```

**EF Core Migration**:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        CREATE TYPE dbo.ResourceListTableType AS TABLE (
            ResourceTypeId SMALLINT NOT NULL,
            ResourceId VARCHAR(64) NOT NULL,
            -- ...
        )");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP TYPE dbo.ResourceListTableType");
}
```

### Step 2: Create Stored Procedure

```sql
CREATE PROCEDURE dbo.MergeResources
    @TransactionId BIGINT,
    @SingleTransaction BIT,
    @IsResourceChangeCaptureEnabled BIT,
    @Resources dbo.ResourceListTableType READONLY,
    @TokenSearchParams dbo.TokenSearchParamListTableType READONLY,
    @ReferenceSearchParams dbo.ReferenceSearchParamListTableType READONLY,
    -- ... 14 more TVP parameters ...
AS
BEGIN
    -- MERGE logic (copied from Microsoft FHIR Server)
    MERGE INTO [dbo].[Resource] AS target
    USING @Resources AS source
    ON target.[ResourceTypeId] = source.[ResourceTypeId]
        AND target.[ResourceId] = source.[ResourceId]
    WHEN NOT MATCHED THEN
        INSERT ([ResourceTypeId], [ResourceId], [Version], [RawResource], ...)
        VALUES (source.[ResourceTypeId], source.[ResourceId], ...)
    WHEN MATCHED THEN
        UPDATE SET
            [Version] = source.[Version],
            [RawResource] = source.[RawResource],
            -- ...
    WHEN NOT MATCHED BY SOURCE THEN
        DELETE;

    -- Insert search parameters
    INSERT INTO [dbo].[TokenSearchParam]
    SELECT * FROM @TokenSearchParams;

    INSERT INTO [dbo].[ReferenceSearchParam]
    SELECT * FROM @ReferenceSearchParams;

    -- ... insert other search param types ...
END;
```

### Step 3: Call from C# via EF Core

```csharp
public async Task<int> MergeAsync(
    long transactionId,
    List<ResourceWrapper> resources,
    CancellationToken cancellationToken)
{
    // Build TVP parameters
    var resourcesTvp = CreateResourceTvp(resources);
    var tokenParamsTvp = CreateTokenSearchParamTvp(resources);
    var referenceParamsTvp = CreateReferenceSearchParamTvp(resources);

    // Call stored procedure
    var rowsAffected = await _context.Database.ExecuteSqlAsync(
        $"""
        EXEC dbo.MergeResources
            @TransactionId = {transactionId},
            @SingleTransaction = {true},
            @IsResourceChangeCaptureEnabled = {false},
            @Resources = {resourcesTvp},
            @TokenSearchParams = {tokenParamsTvp},
            @ReferenceSearchParams = {referenceParamsTvp}
            -- ... 14 more ...
        """,
        new[] { resourcesTvp, tokenParamsTvp, referenceParamsTvp /* ... */ },
        cancellationToken);

    return rowsAffected;
}

private SqlParameter CreateResourceTvp(List<ResourceWrapper> resources)
{
    var table = new DataTable();
    table.Columns.Add("ResourceTypeId", typeof(short));
    table.Columns.Add("ResourceId", typeof(string));
    table.Columns.Add("Version", typeof(int));
    // ... add all columns ...

    foreach (var resource in resources)
    {
        table.Rows.Add(
            GetResourceTypeId(resource.ResourceType),
            resource.ResourceId,
            int.Parse(resource.Version)
            // ... add all values ...
        );
    }

    return new SqlParameter
    {
        ParameterName = "@Resources",
        SqlDbType = SqlDbType.Structured,
        TypeName = "dbo.ResourceListTableType",
        Value = table
    };
}
```

---

## 5. Comparison: Pure ADO.NET vs. EF Core Hybrid

### Pure ADO.NET (Microsoft Pattern)

```csharp
using var cmd = new SqlCommand("dbo.MergeResources", sqlConnection)
{
    CommandType = CommandType.StoredProcedure,
    CommandTimeout = 300
};

var resourcesTvp = new SqlParameter("@Resources", SqlDbType.Structured)
{
    TypeName = "dbo.ResourceListTableType",
    Value = CreateResourceDataTable(resources)
};
cmd.Parameters.Add(resourcesTvp);

await cmd.ExecuteNonQueryAsync();
```

| Aspect | Pure ADO.NET | EF Core Hybrid |
|--------|------------|----------|
| **Connection Management** | Manual | EF Core handles |
| **Transaction Control** | Manual | EF Core handles |
| **Migration Management** | Manual SQL scripts | EF Core migrations |
| **TVP Parameter Creation** | Manual DataTable | Manual DataTable |
| **Row Generators** | Required | Required |
| **Performance** | Slightly faster (no EF overhead) | Minimal difference |
| **Learning Curve** | Steep | Moderate |
| **Code Maintenance** | More code | Less boilerplate |

---

## 6. Why EF Core Doesn't Auto-Generate TVPs

### Technical Reasons

1. **TVPs are SQL Server-specific**
   - Oracle uses nested tables
   - PostgreSQL uses ARRAY types
   - MySQL doesn't have TVPs
   - EF Core aims for cross-database compatibility

2. **Complex type mapping**
   - TVPs require DataTable construction
   - Multiple entity types → multiple TVP columns
   - Generic solution is difficult

3. **Performance implications**
   - Reflection overhead for generic TVP generation
   - Materializing large lists into memory
   - EF prefers explicit control for bulk operations

### What EF Core DOES Support

- ✅ Calling stored procedures
- ✅ Parameter binding (scalar types)
- ✅ Result mapping (back to entities)
- ✅ Native SQL execution
- ❌ TVP marshaling (YOU provide DataTable)

---

## 7. Recommended Ignixa Approach

### Hybrid EF Core + SP Pattern

**Architecture**:

```
FhirDbContext (EF Core)
    ├─ Entity definitions
    ├─ DbSet<Resource>
    ├─ DbSet<TokenSearchParam>
    ├─ Migrations (TVP types, SPs)
    └─ Database connection management

SqlMergeRepository
    ├─ Uses FhirDbContext
    ├─ Builds TVP DataTables from resources
    ├─ Calls MergeResources SP via ExecuteSql
    └─ Inherits transaction/connection from EF
```

**Benefits**:
- ✅ Leverage EF Core migrations for schema management
- ✅ Single connection context (no manual connection management)
- ✅ Transaction coordination through EF scope
- ✅ Cleaner code (less boilerplate than pure ADO.NET)
- ✅ Still get 10-100x performance improvement from TVPs
- ✅ Row generators still needed (unavoidable)

**Tradeoffs**:
- ❌ Slight EF Core overhead (negligible for bulk operations)
- ❌ Still must implement row generators (17 types)
- ❌ Still must create TVP type definitions in SQL

---

## 8. Implementation Roadmap for Ignixa

### Phase 1: Foundation (2 weeks)

1. Create EF Core migrations for:
   - TVP type definitions (17 types)
   - Stored procedures (MergeResources + transaction SPs)
   - Transaction heartbeat SP

2. Create `SqlMergeRepository` class
   - `MergeAsync(transactionId, resources)`
   - Calls `dbo.MergeResources` via `ExecuteSql`

3. Create basic row generators:
   - `ResourceListRowGenerator`
   - Builds Resource DataTable

### Phase 2: Search Parameters (3 weeks)

4. Create row generators for search parameters:
   - `TokenSearchParamListRowGenerator`
   - `ReferenceSearchParamListRowGenerator`
   - `StringSearchParamListRowGenerator`
   - ... (13 more)

5. Extend `SqlMergeRepository`:
   - `CreateTokenSearchParamTvp()`
   - `CreateReferenceSearchParamTvp()`
   - ... (13 more)

6. Update `MergeAsync()` to include all TVPs

### Phase 3: Integration (2 weeks)

7. Integrate with existing `CreateOrUpdateAsync()`
   - Replace single-resource inserts with batched merge
   - Add retry logic + conflict handling
   - Add heartbeat monitoring

8. Add tests
   - Bulk merge scenarios
   - Conflict handling
   - Transaction rollback

### Phase 4: Optimization (1 week)

9. Performance tuning
   - Parallel TVP construction
   - Batch size optimization
   - Timeout configuration

---

## 9. Code Example: Minimal Merge Implementation

```csharp
// Step 1: Migration
public class AddMergeStoredProcedure : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        // Create TVP types
        mb.Sql(@"
            CREATE TYPE dbo.ResourceListTableType AS TABLE (
                ResourceTypeId SMALLINT NOT NULL,
                ResourceId VARCHAR(64) NOT NULL,
                Version INT NOT NULL,
                RawResource VARBINARY(MAX) NOT NULL,
                ResourceSurrogateId BIGINT NOT NULL PRIMARY KEY
            )");

        mb.Sql(@"
            CREATE TYPE dbo.TokenSearchParamListTableType AS TABLE (
                ResourceSurrogateId BIGINT NOT NULL,
                SearchParamId SMALLINT NOT NULL,
                Code VARCHAR(256) NOT NULL
            )");

        // Create stored procedure
        mb.Sql(@"
            CREATE PROCEDURE dbo.MergeResources
                @TransactionId BIGINT,
                @SingleTransaction BIT,
                @Resources dbo.ResourceListTableType READONLY,
                @TokenSearchParams dbo.TokenSearchParamListTableType READONLY
            AS
            BEGIN
                MERGE INTO [dbo].[Resource] AS target
                USING @Resources AS source
                ON target.ResourceTypeId = source.ResourceTypeId
                    AND target.ResourceId = source.ResourceId
                WHEN NOT MATCHED THEN
                    INSERT (ResourceTypeId, ResourceId, Version, RawResource, ResourceSurrogateId)
                    VALUES (source.ResourceTypeId, source.ResourceId, source.Version, source.RawResource, source.ResourceSurrogateId)
                WHEN MATCHED THEN
                    UPDATE SET Version = source.Version, RawResource = source.RawResource;

                INSERT INTO [dbo].[TokenSearchParam] (ResourceSurrogateId, SearchParamId, Code)
                SELECT ResourceSurrogateId, SearchParamId, Code FROM @TokenSearchParams;
            END");
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.Sql("DROP PROCEDURE dbo.MergeResources");
        mb.Sql("DROP TYPE dbo.ResourceListTableType");
        mb.Sql("DROP TYPE dbo.TokenSearchParamListTableType");
    }
}

// Step 2: Row Generator
public class ResourceListRowGenerator
{
    public DataTable Generate(List<ResourceWrapper> resources)
    {
        var table = new DataTable("dbo.ResourceListTableType");
        table.Columns.Add("ResourceTypeId", typeof(short));
        table.Columns.Add("ResourceId", typeof(string));
        table.Columns.Add("Version", typeof(int));
        table.Columns.Add("RawResource", typeof(byte[]));
        table.Columns.Add("ResourceSurrogateId", typeof(long));

        foreach (var resource in resources)
        {
            table.Rows.Add(
                GetResourceTypeId(resource.ResourceType),
                resource.ResourceId,
                int.Parse(resource.Version),
                _compressor.CompressRawResource(resource.RawResource),
                resource.ResourceSurrogateId);
        }

        return table;
    }
}

// Step 3: Repository Method
public class SqlMergeRepository
{
    public async Task MergeAsync(List<ResourceWrapper> resources, CancellationToken ct)
    {
        // Build TVPs
        var resourcesTvp = new SqlParameter("@Resources", SqlDbType.Structured)
        {
            TypeName = "dbo.ResourceListTableType",
            Value = new ResourceListRowGenerator().Generate(resources)
        };

        var tokenParamsTvp = new SqlParameter("@TokenSearchParams", SqlDbType.Structured)
        {
            TypeName = "dbo.TokenSearchParamListTableType",
            Value = new TokenSearchParamListRowGenerator().Generate(resources)
        };

        // Call stored procedure via EF Core
        await _context.Database.ExecuteSqlAsync(
            $"""
            EXEC dbo.MergeResources
                @TransactionId = {0},
                @SingleTransaction = {true},
                @Resources = {resourcesTvp},
                @TokenSearchParams = {tokenParamsTvp}
            """,
            new[] { resourcesTvp, tokenParamsTvp },
            ct);
    }
}
```

---

## 10. Final Recommendation

### ✅ Use Hybrid EF Core + Stored Procedure Approach

**Why**:
1. **Best of both worlds**
   - EF Core handles schema/migrations (no raw SQL management)
   - Raw SQL handles performance-critical bulk ops (stored procedures)

2. **Lower risk than pure ADO.NET**
   - Connection management via EF (less boilerplate)
   - Transaction coordination automatic
   - Less custom infrastructure code

3. **Faster to implement than pure ADO.NET**
   - Migrations instead of manual SQL scripts
   - EF handles connection pooling
   - Only need row generators (17 classes, not 50+)

4. **Maintains architectural cleanliness**
   - Separation of concerns (EF for schema, SP for performance)
   - No EF Core abuse (it's not trying to do everything)
   - Clear intent: this is a high-performance bulk operation

### Implementation Effort

**Estimated 4-5 weeks**:
- Week 1: Migrations + basic Merge SP
- Weeks 2-3: 17 row generators
- Week 4: Integration + transaction coordination
- Week 5: Testing + optimization

---

## References

- [EF Core Querying - Raw SQL](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)
- [EF Core 9.0 Release Notes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0)
- [SQL Server TVPs](https://learn.microsoft.com/en-us/sql/relational-databases/tables/use-table-valued-parameters-database-engine)
- [SqlParameter with Structured Type](https://learn.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlparameter.sqldbtype)

---

**Conclusion**: Yes, EF Core can call stored procedures with TVPs, but you still need to manually create row generators. A hybrid approach (EF Core for schema + raw SQL for bulk Merge) is the most practical for Ignixa.
