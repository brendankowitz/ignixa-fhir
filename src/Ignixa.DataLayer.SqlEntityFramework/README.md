# Ignixa.DataLayer.LegacySqlEF

Entity Framework Core data layer for compatibility with Microsoft FHIR Server SQL schema (legacy schema).

## Overview

This data layer provides:
- **Schema Compatibility**: Works with existing Microsoft FHIR Server databases (schema v60-v96)
- **Migration Path**: Allows migration from microsoft/fhir-server to Ignixa without data migration
- **Multi-Tenancy**: Supports isolation mode (one database per tenant)
- **Search Support**: Full search parameter indexing and querying
- **Performance**: Optimized with partitioning, compression, and stored procedures

## Schema Version Compatibility

| Component | Version |
|-----------|---------|
| **Base Schema** | v60 (initial) |
| **Current Schema** | v96 (latest) |
| **Migration Scripts** | 61.diff.sql through 96.diff.sql |
| **Source** | ThirdParty/Microsoft.Health.Fhir.SqlServer |

### Schema Source

The schema is based on the **Microsoft FHIR Server** open-source project:
- Repository: https://github.com/microsoft/fhir-server
- Path: `src/Microsoft.Health.Fhir.SqlServer/Features/Schema/`
- License: MIT (same as Ignixa)

Schema files are copied to `ThirdParty/Microsoft.Health.Fhir.SqlServer` for reference.

## Core Tables

### Resource Storage

| Table | Purpose | Key Columns |
|-------|---------|-------------|
| **Resource** | Main resource storage | ResourceSurrogateId (PK), ResourceTypeId, ResourceId, Version, RawResource (varbinary) |
| **ResourceType** | Resource type lookup | ResourceTypeId (PK), Name (varchar) - "Patient", "Observation", etc. |
| **Transactions** | Transaction tracking | TransactionId (PK), VisibleDateTime, SurrogateIdRangeFirstValue |

### Search Parameter Tables

| Table | Search Type | Description |
|-------|-------------|-------------|
| **StringSearchParam** | String | Text-based search (name, address, etc.) |
| **TokenSearchParam** | Token | Code/identifier search (system\|code) |
| **NumberSearchParam** | Number | Numeric values (age, quantity) |
| **DateTimeSearchParam** | Date | Date/time ranges |
| **QuantitySearchParam** | Quantity | Values with units |
| **ReferenceSearchParam** | Reference | Resource references |
| **UriSearchParam** | URI | URL-based search |

### Composite Search Tables

- **TokenStringCompositeSearchParam** - Token + String combinations
- **TokenDateTimeCompositeSearchParam** - Token + DateTime combinations
- **TokenNumberNumberCompositeSearchParam** - Token + Number + Number
- **TokenQuantityCompositeSearchParam** - Token + Quantity
- **TokenTokenCompositeSearchParam** - Token + Token
- **ReferenceTokenCompositeSearchParam** - Reference + Token

### Lookup Tables

| Table | Purpose |
|-------|---------|
| **SearchParam** | Search parameter definitions (Uri, Status) |
| **CompartmentType** | Compartment type definitions (Patient, Practitioner, etc.) |
| **CompartmentAssignment** | Resource-to-compartment assignments |
| **QuantityCode** | Quantity unit codes |
| **System** | Token system URLs |
| **TokenText** | Token display text |

### Excluded Tables (Not Implemented)

The following tables from the legacy schema are **NOT** implemented in Ignixa:
- **TaskInfo** - We have a better async processing plan (ADR-2502+)
- **JobQueue** - Replaced with our async architecture
- **ReindexJob** - Handled differently in Ignixa
- **EventLog** - Using different logging approach
- **WatchdogLeases** - Not needed in Ignixa architecture

## Key Features

### 1. Compressed Storage

- **RawResource** column stores compressed JSON (varbinary)
- **Compression**: Gzip (CompressionLevel.Optimal)
- **Savings**: ~70% storage reduction
- **Transparent**: Automatically compressed on write, decompressed on read

### 2. Partitioning

The schema uses table partitioning for performance:

```sql
-- Partition by ResourceTypeId (150 partitions for 150 resource types)
CREATE PARTITION FUNCTION PartitionFunction_ResourceTypeId(SMALLINT)
    AS RANGE RIGHT
    FOR VALUES (1, 2, 3, ..., 150);
```

Benefits:
- **Parallel queries** across partitions
- **Efficient purging** of old data
- **Improved index performance**

### 3. Resource Versioning

Every resource update creates a new version:
- New row with incremented `Version`
- Old version marked with `IsHistory = true`
- History tracking via `HistoryTransactionId`

Example:
```
ResourceId | Version | IsHistory | IsDeleted
-----------|---------|-----------|----------
patient-1  | 1       | true      | false     (old version)
patient-1  | 2       | false     | false     (current version)
```

### 4. Transaction Support

- **TransactionId**: Unique ID for each batch of operations
- **SurrogateIdRangeFirstValue**: Range of surrogate IDs allocated
- **VisibleDateTime**: When transaction becomes visible to queries
- **InvisibleHistoryRemovedDateTime**: When old history becomes invisible

### 5. Search Parameter Indexing

Search parameters are extracted from resources and stored in dedicated tables:

**Example: Patient name search**
```
Resource JSON: { "name": [{ "family": "Smith", "given": ["John"] }] }

StringSearchParam rows:
- SearchParamId: 42 (Patient.name)
- Text: "Smith"
- Text: "John"
```

**Example: Observation code search**
```
Resource JSON: { "code": { "coding": [{ "system": "http://loinc.org", "code": "12345-6" }] } }

TokenSearchParam row:
- SearchParamId: 15 (Observation.code)
- SystemId: 123 (http://loinc.org)
- Code: "12345-6"
```

## Architecture Integration

### Multi-Tenancy Support

**Isolation Mode** (current implementation):
- Each tenant has its own SQL database
- Connection string per tenant: `Server=...;Database=Fhir_Tenant{tenantId};...`
- Complete data isolation
- Independent schema versions

**Repository Factory**:
```csharp
public class LegacySqlEfRepositoryFactory : IFhirRepositoryFactory
{
    public IFhirRepository GetRepository(int tenantId)
    {
        // Creates and caches DbContext per tenant
    }
}
```

### Integration with Ignixa Architecture

```
┌─────────────────────────────────────────────────┐
│ Ignixa.Api (ASP.NET Core)                      │
│ - TenantResolutionMiddleware                    │
│ - FhirEndpoints (minimal API)                   │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ Ignixa.Application (Medino Handlers)           │
│ - GetResourceHandler                            │
│ - CreateOrUpdateResourceHandler                 │
│ - SearchResourcesHandler                         │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ Ignixa.DataLayer.LegacySqlEF (This Project)    │
│ - LegacySqlEfRepository (IFhirRepository)       │
│ - FhirDbContext (EF Core)                       │
│ - SearchIndexer, SearchQueryBuilder             │
└────────────────┬────────────────────────────────┘
                 │
┌────────────────▼────────────────────────────────┐
│ SQL Server Database (microsoft/fhir-server      │
│ schema v96)                                      │
└─────────────────────────────────────────────────┘
```

## Usage

### 1. Configuration (appsettings.json)

```json
{
  "ConnectionStrings": {
    "FhirDatabase": "Server=localhost;Database=Fhir;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Mayo Clinic",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "LegacySqlEF",
          "ConnectionString": "Server=localhost;Database=Fhir_Tenant1;..."
        }
      }
    ]
  }
}
```

### 2. DI Registration (Program.cs)

```csharp
// Register DbContext
builder.Services.AddDbContext<FhirDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("FhirDatabase"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
            sqlOptions.CommandTimeout(30);
        });
});

// Register repository factory
containerBuilder.RegisterType<LegacySqlEfRepositoryFactory>()
    .As<IFhirRepositoryFactory>()
    .SingleInstance();

// Register compressor
containerBuilder.RegisterType<GzipResourceCompressor>()
    .As<IResourceCompressor>()
    .SingleInstance();
```

### 3. Database Initialization

```bash
# Option 1: Use existing microsoft/fhir-server database
# (No migration needed - schema already compatible)

# Option 2: Create new database with schema v96
dotnet run --project tools/DatabaseInitializer -- --connection "Server=..." --schema-version 96
```

## Performance Tuning

### Connection Pooling

Default EF Core connection pooling is enabled. Tune with:
```csharp
builder.Services.AddDbContextPool<FhirDbContext>(options => { ... }, poolSize: 128);
```

### Query Performance

1. **Indexes**: All indexes from microsoft/fhir-server schema are preserved
2. **Partitioning**: Queries automatically use partition elimination
3. **Compiled Queries**: EF Core automatically compiles frequently-used queries
4. **Read Replicas**: Configure read-only DbContext for searches

### Bulk Operations

For batch inserts/updates, use stored procedures:
```csharp
await _context.Database.ExecuteSqlRawAsync(
    "EXEC dbo.MergeResources @resources, @transactionId",
    new SqlParameter("@resources", tvpData),
    new SqlParameter("@transactionId", txId));
```

## Migration from FileBasedFhirRepository

1. **Export** resources from file-based storage:
   ```bash
   dotnet run --project tools/MigrationTool -- export --source file --output ./export
   ```

2. **Initialize** SQL database:
   ```bash
   dotnet run --project tools/DatabaseInitializer -- --connection "..." --schema-version 96
   ```

3. **Import** resources to SQL:
   ```bash
   dotnet run --project tools/MigrationTool -- import --source ./export --target sql
   ```

4. **Reindex** search parameters:
   ```bash
   dotnet run --project tools/ReindexTool -- --connection "..."
   ```

## Troubleshooting

### Issue: "Cannot open database" error

**Solution**: Ensure database exists and connection string is correct.

```bash
# Create database
sqlcmd -S localhost -Q "CREATE DATABASE Fhir"
```

### Issue: Slow search queries

**Solution**:
1. Check query execution plan
2. Verify indexes are being used
3. Update statistics: `EXEC sp_updatestats`

### Issue: High storage usage

**Solution**:
1. Verify compression is enabled
2. Purge old history: `EXEC dbo.DeleteHistory @resourceType, @cutoffDate`
3. Rebuild indexes with compression: `ALTER INDEX ... REBUILD WITH (DATA_COMPRESSION = PAGE)`

## Development

### Running Tests

```bash
# Integration tests (requires SQL Server LocalDB or Docker)
dotnet test test/Ignixa.DataLayer.LegacySqlEF.Tests/ --filter Category=Integration

# Unit tests (no database required)
dotnet test test/Ignixa.DataLayer.LegacySqlEF.Tests/ --filter Category=Unit
```

### Docker SQL Server for Testing

```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong!Passw0rd" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

## References

- **Microsoft FHIR Server**: https://github.com/microsoft/fhir-server
- **EF Core Documentation**: https://learn.microsoft.com/en-us/ef/core/
- **FHIR Specification**: https://hl7.org/fhir/R4/
- **ADR-2523**: Multi-Tenancy Data Partitioning
- **Ignixa Architecture**: ../docs/architecture/overview.md

## License

MIT License - Same as Microsoft FHIR Server and Ignixa project
