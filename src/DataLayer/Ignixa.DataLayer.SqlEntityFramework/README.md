# Ignixa.DataLayer.SqlEntityFramework

Entity Framework Core data layer for SQL Server, compatible with Microsoft FHIR Server schema (v60-v96). Enables migration from `microsoft/fhir-server` without data migration.

## Why Use This Package?

- **Schema compatibility**: Works with existing Microsoft FHIR Server databases
- **Zero data migration**: Point to your existing database and start using Ignixa
- **Multi-tenancy**: Supports one database per tenant (isolation mode)
- **High-performance**: Bulk operations via stored procedures with Table-Valued Parameters (TVPs)

## Installation

```bash
dotnet add package Ignixa.DataLayer.SqlEntityFramework
```

## Quick Start

### 1. Configure Connection

Add your tenant configuration in `appsettings.json`:

```json
{
  "Tenants": {
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "My Clinic",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlEntityFramework",
          "ConnectionString": "Server=(local);Database=FHIR;Integrated Security=true;TrustServerCertificate=true"
        }
      }
    ]
  }
}
```

### 2. Register Services

```csharp
// Register DbContext
builder.Services.AddDbContext<FhirDbContext>(options =>
    options.UseSqlServer(connectionString));

// Register repository factory
containerBuilder.RegisterType<LegacySqlEfRepositoryFactory>()
    .As<IFhirRepositoryFactory>()
    .SingleInstance();
```

### 3. Start the API

```bash
dotnet run --project src/Ignixa.Api
```

The `DatabaseInitializer` automatically validates TVP types on startup and creates any missing types.

## Database Requirements

Your SQL Server database needs the Microsoft FHIR Server schema (v60-v96):

- **Using existing database**: Point your connection string to your `microsoft/fhir-server` database
- **New database**: Apply schema scripts from [microsoft/fhir-server](https://github.com/microsoft/fhir-server)

### Verify Schema

```sql
-- Should return 17 TVP types
SELECT COUNT(*) FROM sys.types WHERE is_table_type = 1;
```

## Features

| Feature | Description |
|---------|-------------|
| **Compressed storage** | Gzip compression (~70% storage reduction) |
| **Resource versioning** | Full history tracking |
| **Search indexing** | All FHIR search parameter types supported |
| **Bulk operations** | 10-100x faster via TVP-based stored procedures |

## Related Packages

- **Ignixa.Abstractions**: Core interfaces (`IFhirRepository`)
- **Ignixa.Search**: Search parameter extraction
- **Ignixa.Api**: FHIR REST API endpoints

## License

MIT License - see LICENSE file in repository root
