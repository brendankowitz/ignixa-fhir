---
sidebar_position: 2
title: Configuration
description: Configure Ignixa FHIR Server for different environments
---

# Configuration

Ignixa uses standard ASP.NET Core configuration with `appsettings.json`. All settings can be overridden via environment variables using double-underscore notation (e.g., `Tenants__Mode`).

## Tenant Configuration (Required)

Ignixa requires at least two tenant configurations: Tenant 0 (system partition) and Tenant 1+ (your data).

```json
{
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 0,
        "DisplayName": "System Partition (Reserved)",
        "FhirVersion": "4.0",
        "IsActive": true,
        "IsSystemPartition": true,
        "Storage": {
          "Type": "SqlServer",
          "InheritConnectionStringFromTenant": true
        }
      },
      {
        "TenantId": 1,
        "DisplayName": "Production Database",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlServer",
          "ConnectionString": "Server=localhost;Database=FHIR_R4;Integrated Security=true;TrustServerCertificate=true"
        }
      }
    ]
  }
}
```

### Key Settings

| Setting | Description |
|---------|-------------|
| `Mode` | `Isolated` - each tenant has separate data. (`Distributed` planned but not yet implemented) |
| `TenantId` | Unique identifier. `0` is reserved for system operations |
| `FhirVersion` | `4.0` (R4), `4.3` (R4B), `5.0` (R5), or `6.0` (R6) |
| `Storage.Type` | `SqlServer` (recommended). `SqlEntityFramework` is accepted as a legacy alias for the same storage. |
| `InheritConnectionStringFromTenant` | System partition inherits from Tenant 1 |

### Hostname-based Tenant Resolution

Each tenant may declare a `Hostnames` array to enable resolution by request `Host` header in addition to numeric `/tenant/{id}/` path routing.

#### Configuration

```json
{
  "Tenants": {
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Production Database",
        "Hostnames": ["fhir1.example.org", "fhir1-backup.example.org"],
        "Storage": { "Type": "SqlServer", "ConnectionString": "..." }
      }
    ]
  }
}
```

#### How It Works

**Hostname Semantics:**

- **First hostname** is the **canonical base** for that tenant's absolute references. This hostname is used when the server emits absolute URLs (in `Location` headers, pagination links, `Bundle.entry.fullUrl`, etc.) and when stored internally.
- **Additional hostnames** (if any) are recognized as valid inbound hosts for the same tenant but are **not** used for outbound references.
- Hostnames must be **bare DNS names** (lowercase, no scheme, no port, no path). Example: `fhir1.example.org` (valid); `https://fhir1.example.org:8080/fhir` (invalid).
- Hostnames are **unique across all tenants**. A duplicate hostname is fatal: the server refuses to serve and the error is enforced when the host-index resolver is first used or during host-index build.

**Resolution Precedence:**

1. If the request's `Host` header matches a configured hostname, that tenant is selected.
2. If the URL path contains `/tenant/{id}/` (numeric), that tenant is selected by ID.
3. If **both** `Host` header and `/tenant/{id}/` path are present and resolve to **different** tenants, the server returns **400 Bad Request**.
4. If the `Host` header is not recognized and no `/tenant/{id}/` is in the path, resolution falls through to single-tenant auto-detect (if only one active tenant) or remains unresolved.

**Examples:**

```
Request: GET http://fhir1.example.org/metadata
Result: Selects tenant with Hostnames[0] = "fhir1.example.org"

Request: GET http://fhir1-backup.example.org/Patient/123
Result: Selects same tenant via Hostnames[1] (alternate hostname)

Request: GET http://fhir1.example.org/tenant/2/Patient/123
Result: 400 Bad Request (Host resolves to Tenant 1, path specifies Tenant 2 — conflict)

Request: GET http://localhost/tenant/1/Patient/123
Result: Selects Tenant 1 (by path; Host not recognized)

Request: GET http://unrecognized.example.org/Patient/123
Result: Falls through to auto-detect or single-tenant mode
```

#### TLS/Certificate Considerations

- **Subdomains under one zone** (e.g., `fhir1.example.org`, `fhir2.example.org`) are covered by a single **wildcard certificate** (`*.example.org`). Wildcards match a single DNS level.
- **Apex/vanity domains** (different registrable domains like `org1.com`, `org2.com`) require **separate certificates**, each signed for its own domain.
- For development, self-signed certificates or local DNS overrides (`/etc/hosts` or Windows hosts file) are common.

#### Limitations

**Path-based vanity slugs are not yet supported.** The following forms are **NOT** available:

- `/tenant/{slug}/` (path-based slug routing)
- `/{slug}/` (bare slug routing)

Currently, only these forms work:

- `/tenant/{id}/` (numeric ID routing) ✅
- `Host` header routing with `Hostnames` ✅

Path-based slugs (`/tenant/{slug}/`) are planned for a future release and will require relaxing route constraints, slug indexing, and slug format validation across the API layer. Track progress in the project roadmap.

### SQL Server Connection String

For production SQL Server:

```json
{
  "Storage": {
    "Type": "SqlServer",
    "ConnectionString": "Server=your-server.database.windows.net;Database=FHIR_R4;Authentication=Active Directory Default;TrustServerCertificate=true"
  }
}
```

For local development with Windows Auth:

```json
{
  "Storage": {
    "Type": "SqlServer",
    "ConnectionString": "Server=(local);Database=FHIR_R4;Integrated Security=true;TrustServerCertificate=true"
  }
}
```

## SQL Server Schema Deployment

The SQL Server data layer ships its schema as an embedded dacpac built from the
`Ignixa.DataLayer.SqlServer.Database` SQL Database Project. The `SqlServer` section controls whether
the server is allowed to apply that schema itself.

```json
{
  "SqlServer": {
    "AutomaticSchemaDeploymentEnabled": false
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `AutomaticSchemaDeploymentEnabled` | `false` | Whether the app may apply schema changes itself. A deployment opts in rather than out |

### When `AutomaticSchemaDeploymentEnabled` is `true`

- **Brand-new tenant databases** are provisioned from the embedded dacpac and stamped with the
  current schema version.
- **Tenants behind the current schema version** are upgraded — but only when the pending diff
  classifies as auto-safe.

Both paths run with `BlockOnPossibleDataLoss` set explicitly, and every upgrade is gated by the
deploy-report classifier described below. This setting grants permission to apply a change that has
already been judged safe; it never bypasses that judgement.

:::note
This is **not** a startup action. The schema check runs on **first repository access for a given
tenant**, inside the per-tenant factory the data layer caches. A tenant that is never addressed is
never touched, and a schema failure surfaces as a failure of the first request against that tenant
rather than as a failed startup.
:::

### When `AutomaticSchemaDeploymentEnabled` is `false` (default)

Both cases throw instead, and the exception names the remedy:

- An **uninitialized** tenant database reports that it is not initialized and directs you to publish
  the dacpac (`sqlpackage /Action:Publish`) before starting the app, or to enable automatic
  deployment.
- A tenant **behind the current version** reports that it is behind and directs you to the
  [schema-upgrade CLI](/docs/server/schema-upgrade-cli), or to enable automatic deployment.

A tenant that is already at the current schema version is unaffected either way — the version check
returns before the setting is consulted.

### The auto-safe gate

Before applying any upgrade, the server generates a DacFx deploy report for the pending diff and
classifies it. Only a diff classified **auto-safe** is applied automatically. A diff DacFx flags as a
data issue is classified **unsafe**; a report whose shape the classifier cannot read is classified
**unclassifiable**. Both refuse, and both fail closed — an unreadable report is never treated as
safe.

Purely additive changes (a new nullable column, a canonicalization-only default rewrite) carry no
data-issue marker and classify as auto-safe. Anything DacFx flags — a dropped column, a table
rebuild that could lose rows — does not, and requires an operator to review and apply it explicitly
with the CLI.

### Database creation

Automatic deployment applies *schema*; it does not create the database except in `Development`,
where the server creates an empty database if it cannot connect to one. In every other environment
the tenant database must already exist before the first request for that tenant arrives.

### Environment variable override

```bash
export SqlServer__AutomaticSchemaDeploymentEnabled=true
```

## Terminology Import Timeout

Terminology packages (CodeSystem, ValueSet, ConceptMap) import through a handful of SQL Server commands
that can carry a whole CodeSystem's or ValueSet's worth of rows in one call. The `SqlServer` section also
controls how long those commands are allowed to run before ADO.NET gives up on them.

```json
{
  "SqlServer": {
    "TerminologyImportCommandTimeoutSeconds": 120
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `TerminologyImportCommandTimeoutSeconds` | `120` | `SqlCommand.CommandTimeout`, in seconds, for the terminology import procedures and the ValueSet compose-resolution reads that can run before them |

Left unset, `SqlCommand` defaults to ADO.NET's 30-second timeout. A command that overruns it is classified
as a transient SQL failure and retried up to three more times before the import is marked `Failed` — and
`Failed` is not a terminal status, so the same package is re-offered and re-fails on every subsequent
startup. This setting covers:

- `dbo.ImportTermCodeSystem`, `dbo.ImportTermValueSet` and `dbo.ImportTermConceptMap` — the three
  procedures that insert a whole CodeSystem, ValueSet or ConceptMap as a table-valued parameter and
  resolve its hierarchy server-side in one transaction.
- The reads `SqlServerValueSetComposer` runs to resolve a ValueSet's `compose` element *before*
  `dbo.ImportTermValueSet` runs. These can be just as large: an `include` naming a whole CodeSystem with
  no `concept` or `filter` array reads every concept in that system, and an `include` naming a previously
  expanded ValueSet reads every one of its rows.

:::note
Measured against a local, otherwise-idle SQL Server: importing 100,000 flat concepts took under 2 seconds,
a 350,000-concept import (SNOMED CT's rough scale) took under 6 seconds, and re-importing 100,000 concepts
(a cascade delete of the previous import plus a full re-insert) took about 3 seconds. Real CodeSystems
carry per-concept `property` and `designation` payloads that benchmark did not, and a production database
adds network latency, a lower-throughput SKU, and lock contention from concurrent terminology activity on
top of that baseline — the default of 120 seconds is set well above the measured numbers, not at them.
Raise it further for Azure SQL deployments seeing terminology import failures under real package sizes or
concurrent load; a genuinely stuck command still fails eventually rather than hanging forever.
:::

### Environment variable override

```bash
export SqlServer__TerminologyImportCommandTimeoutSeconds=180
```

## Blob Storage

Configure blob storage for bulk import/export operations:

```json
{
  "BlobStorage": {
    "Provider": "Azure",
    "ContainerName": "fhirstorage",
    "UseManagedIdentity": true,
    "StorageAccountUri": "https://youraccount.blob.core.windows.net"
  },
  "AzureBlobStorage": {
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=youraccount;AccountKey=...;EndpointSuffix=core.windows.net",
    "ContainerName": "fhirstorage",
    "UseManagedIdentity": true,
    "StorageAccountUri": "https://youraccount.blob.core.windows.net"
  }
}
```

### Provider Options

| Provider | Use Case | Configuration Section |
|----------|----------|----------------------|
| `Local` | Development - stores in `RootDirectory` on filesystem | `LocalFileBlobStorage` |
| `Azure` | Production - Azure Blob Storage with Managed Identity or connection string | `AzureBlobStorage` |

For local development with filesystem:

```json
{
  "BlobStorage": {
    "Provider": "Local"
  },
  "LocalFileBlobStorage": {
    "RootDirectory": "fhir-exports"
  }
}
```

For Azurite (Azure Storage emulator):

```json
{
  "BlobStorage": {
    "Provider": "Azure",
    "UseManagedIdentity": false
  },
  "AzureBlobStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "ContainerName": "fhirstorage"
  }
}
```

## DurableTask (Bulk Operations)

Bulk import/export uses DurableTask for orchestration. SQL Server backend is recommended:

```json
{
  "DurableTask": {
    "Provider": "SqlServer",
    "SqlServer": {
      "TaskHubName": "ignixa"
    }
  }
}
```

The SQL Server provider uses the same database as Tenant 0 (system partition), eliminating additional infrastructure dependencies. Schema is created automatically on startup.

### Alternative Providers

```json
{
  "DurableTask": {
    "Provider": "AzureStorage",
    "AzureStorage": {
      "UseManagedIdentity": true,
      "StorageAccountName": "youraccount",
      "TaskHubName": "ignixa"
    }
  }
}
```

## Service Base URI

`Fhir:BaseUri` is this deployment's public FHIR service root. Set it in every environment that runs
`$reindex` or `$import`.

```json
{
  "Fhir": {
    "BaseUri": "https://fhir.example.org"
  }
}
```

It is used to recognise a reference written as an absolute URL that points back at this server, so it
reconciles with the equivalent relative reference. `Patient/p1`, `https://fhir.example.org/Patient/p1` and
`https://fhir.example.org/tenant/1/Patient/p1` all name the same resource, and all three are stored — and
searched — the same way. Both the root and each tenant's `/tenant/{id}/` base are recognised, so it does
not matter which route form a client used to write or to search.

Two things depend on setting it:

- **Background indexing.** `$reindex` and `$import` have no HTTP request to derive a base from. With
  `Fhir:BaseUri` unset they recognise nothing, so reindexed rows file self-references as external while the
  rows they replace filed them as internal, and those resources drop out of absolute searches. The server
  logs a warning at startup when the setting is missing. Recognising the tenant-scoped base also depends on
  the background activity establishing which tenant it is running for — `$import` does this via
  `FhirRequestContextFactory.CreateBackgroundContext`, restored on exit so it cannot leak to the next job on
  a pooled thread. Any future background path that indexes resources (a `$reindex` implementation, for
  example) must do the same or it will silently reintroduce this gap even with `Fhir:BaseUri` set.
- **Host header trust.** With `Fhir:BaseUri` unset, the base is derived from the request's `Host` header,
  which a client controls — a forged `Host` decides whether an inbound reference is stored as internal or
  external. When it is set, the `Host` header is ignored for this purpose. Independently, set `AllowedHosts`
  to your real hostnames rather than leaving it at `*`.

:::note
Only rows written after the setting is in place are affected. References already stored against a
self-referencing absolute base keep it until a `$reindex`.
:::

## Authentication

Configure OIDC authentication with any compliant provider (Entra ID, Okta, etc.):

```json
{
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/{tenant}/v2.0",
    "Audience": "api://your-app-id"
  }
}
```

The server discovers endpoints automatically from `/.well-known/openid-configuration`.

## Authorization

Enable RBAC-based authorization:

```json
{
  "Authorization": {
    "Enabled": true,
    "RequireAuthentication": true,
    "EnforceTenantIsolation": true,
    "EnforceCapabilities": true
  }
}
```

### Default Roles

| Role | Description |
|------|-------------|
| `Admin` | Full access to all resources |
| `SystemAdmin` | Cross-tenant administrative access |
| `Clinician` | Access to clinical resources (Patient, Observation, etc.) |
| `ReadOnly` | Read-only access to all resources |

### SMART on FHIR

```json
{
  "Authorization": {
    "SmartOnFhir": {
      "EnableSmartConfiguration": true,
      "AuthorizeUrl": "https://your-idp.com/authorize",
      "TokenUrl": "https://your-idp.com/token"
    }
  }
}
```

## Experimental Features

Enable or disable experimental features:

```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": {
        "Enabled": true,
        "Transport": "http"
      },
      "Transform": {
        "Enabled": true,
        "TimeoutSeconds": 30
      },
      "Terminology": {
        "Enabled": true,
        "EnableAutoImport": true
      },
      "Summary": {
        "Enabled": true,
        "MaxResources": 1000
      }
    }
  }
}
```

| Feature | Description |
|---------|-------------|
| `Mcp` | Model Context Protocol for AI integration |
| `Transform` | FHIR Mapping Language `$transform` operation |
| `Terminology` | `$expand`, `$translate`, `$subsumes` operations |
| `Summary` | Patient `$summary` (IPS) operation |

## Bulk Import Tuning

Configure import performance for high-volume ingestion:

```json
{
  "Import": {
    "MaxConcurrentFiles": 1,
    "ConsumerCount": 1,
    "BatchSize": 100,
    "ChannelCapacity": 1000
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxConcurrentFiles` | 1 | Files processed in parallel (default 1, increase for higher throughput) |
| `ConsumerCount` | 1 | Writer threads per file (default 1, increase to 4-8 for parallel processing) |
| `BatchSize` | 100 | Resources per database write |
| `ChannelCapacity` | 1000 | Backpressure buffer size |

:::note
Higher concurrency values improve throughput but use more system resources and threads. Start with defaults and increase conservatively based on monitoring. Each concurrent file spawn 1 producer + ConsumerCount worker threads, so total threads = MaxConcurrentFiles * (1 + ConsumerCount).
:::

## Transaction Watcher

Automatically commits stalled transactions:

```json
{
  "TransactionWatcher": {
    "Enabled": true,
    "ScanInterval": "00:01:00",
    "StallThreshold": "00:05:00"
  }
}
```

## Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning",
      "Ignixa": "Debug"
    }
  }
}
```

For troubleshooting SQL queries, set EF Core command logging to `Debug`:

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Debug"
    }
  }
}
```

## Environment Variables

Override any setting with environment variables:

```bash
# Tenant connection string
export Tenants__Configurations__1__Storage__ConnectionString="Server=..."

# Public FHIR service root (see "Service Base URI")
export Fhir__BaseUri="https://fhir.example.org"

# Enable authorization
export Authorization__Enabled=true
export Authorization__RequireAuthentication=true

# Blob storage
export BlobStorage__Provider=Azure
export BlobStorage__UseManagedIdentity=true
export BlobStorage__StorageAccountUri="https://account.blob.core.windows.net"

# DurableTask
export DurableTask__Provider=SqlServer

# Automatic SQL Server schema deployment (see "SQL Server Schema Deployment")
export SqlServer__AutomaticSchemaDeploymentEnabled=false
```

## Docker/Container Deployment

When running in containers, use environment variables:

```bash
docker run -p 8080:8080 \
  -e Tenants__Configurations__1__Storage__ConnectionString="Server=host.docker.internal;Database=FHIR_R4;..." \
  -e BlobStorage__Provider=Azure \
  -e BlobStorage__ConnectionString="DefaultEndpointsProtocol=https;..." \
  -e ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
  ghcr.io/brendankowitz/ignixa-fhir:release
```

:::tip
Set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` when behind a reverse proxy (App Service, AKS ingress) to correctly handle `X-Forwarded-*` headers.
:::

## Complete Production Example

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Ignixa": "Information"
    }
  },
  "Authentication": {
    "Authority": "https://login.microsoftonline.com/{tenant}/v2.0",
    "Audience": "api://ignixa-fhir"
  },
  "Authorization": {
    "Enabled": true,
    "RequireAuthentication": true,
    "EnforceTenantIsolation": true
  },
  "BlobStorage": {
    "Provider": "Azure",
    "UseManagedIdentity": true,
    "StorageAccountUri": "https://youraccount.blob.core.windows.net",
    "ContainerName": "fhirstorage"
  },
  "DurableTask": {
    "Provider": "SqlServer",
    "SqlServer": {
      "TaskHubName": "ignixa"
    }
  },
  "SqlServer": {
    "AutomaticSchemaDeploymentEnabled": false
  },
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 0,
        "DisplayName": "System Partition",
        "FhirVersion": "4.0",
        "IsActive": true,
        "IsSystemPartition": true,
        "Storage": {
          "Type": "SqlServer",
          "InheritConnectionStringFromTenant": true
        }
      },
      {
        "TenantId": 1,
        "DisplayName": "Production",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlServer",
          "ConnectionString": "Server=sql.example.com;Database=FHIR_R4;Authentication=Active Directory Default"
        }
      }
    ]
  },
  "Experimental": {
    "Enabled": false
  }
}
```

## Next Steps

- [Server Architecture](/docs/server/architecture) - Understand the internal design
- [Schema Upgrade CLI](/docs/server/schema-upgrade-cli) - Apply schema changes the server refuses to auto-apply
- [Security Configuration](/docs/server/security/authentication) - Set up authentication
- [Multi-Tenancy](/docs/server/multi-tenancy) - Configure tenant isolation
