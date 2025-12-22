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
          "Type": "SqlEntityFramework",
          "InheritConnectionStringFromTenant": true
        }
      },
      {
        "TenantId": 1,
        "DisplayName": "Production Database",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlEntityFramework",
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
| `Storage.Type` | `SqlEntityFramework` (recommended) |
| `InheritConnectionStringFromTenant` | System partition inherits from Tenant 1 |

### SQL Server Connection String

For production SQL Server:

```json
{
  "Storage": {
    "Type": "SqlEntityFramework",
    "ConnectionString": "Server=your-server.database.windows.net;Database=FHIR_R4;Authentication=Active Directory Default;TrustServerCertificate=true"
  }
}
```

For local development with Windows Auth:

```json
{
  "Storage": {
    "Type": "SqlEntityFramework",
    "ConnectionString": "Server=(local);Database=FHIR_R4;Integrated Security=true;TrustServerCertificate=true"
  }
}
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

# Enable authorization
export Authorization__Enabled=true
export Authorization__RequireAuthentication=true

# Blob storage
export BlobStorage__Provider=Azure
export BlobStorage__UseManagedIdentity=true
export BlobStorage__StorageAccountUri="https://account.blob.core.windows.net"

# DurableTask
export DurableTask__Provider=SqlServer
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
          "Type": "SqlEntityFramework",
          "InheritConnectionStringFromTenant": true
        }
      },
      {
        "TenantId": 1,
        "DisplayName": "Production",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlEntityFramework",
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
- [Security Configuration](/docs/server/security/authentication) - Set up authentication
- [Multi-Tenancy](/docs/server/multi-tenancy) - Configure tenant isolation
