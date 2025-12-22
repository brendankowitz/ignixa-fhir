---
sidebar_position: 3
title: Configuration
description: Configure Ignixa FHIR Server for different environments
---

# Configuration

Ignixa uses standard ASP.NET Core configuration with `appsettings.json`. This guide covers essential configuration options.

## Storage Provider

By default, Ignixa uses file system storage for development. For production, configure SQL Server.

### File System (Development)

```json
{
  "Storage": {
    "Provider": "FileSystem",
    "DataPath": "./fhir-data"
  }
}
```

### SQL Server (Production)

```json
{
  "Storage": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=localhost;Database=IgnixaFhir;User Id=sa;Password=YourPassword;TrustServerCertificate=true"
  }
}
```

### Azure Blob Storage

```json
{
  "Storage": {
    "Provider": "BlobStorage",
    "BlobStorageConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  }
}
```

## Multi-Tenancy

Enable multi-tenant mode for isolated data partitions:

```json
{
  "Tenancy": {
    "Mode": "MultiTenant",
    "DefaultTenantId": 1
  }
}
```

With multi-tenancy enabled:
- Access tenants via `/tenant/{id}/Patient/123`
- Each tenant has isolated storage
- Tenant 0 is reserved for system operations

See [Multi-Tenancy](/docs/server/multi-tenancy) for details.

## FHIR Versions

Configure supported FHIR versions:

```json
{
  "Fhir": {
    "DefaultVersion": "R4",
    "SupportedVersions": ["R4", "R4B", "R5"]
  }
}
```

## Validation

Configure the three-tier validation engine:

```json
{
  "Validation": {
    "Level": "Spec",
    "EnableProfileValidation": true,
    "ValidateOnCreate": true,
    "ValidateOnUpdate": true
  }
}
```

Validation levels:
- **Fast** - Structural validation only (fastest)
- **Spec** - FHIR specification compliance
- **Profile** - Full profile validation (slowest)

## Search Configuration

Tune search behavior:

```json
{
  "Search": {
    "DefaultPageSize": 20,
    "MaxPageSize": 100,
    "MaxIncludeIterations": 3,
    "MaxTotalResults": 10000
  }
}
```

## Environment Variables

All settings can be overridden with environment variables using double underscore notation:

```bash
# Storage provider
export Storage__Provider=SqlServer
export Storage__ConnectionString="Server=..."

# Multi-tenancy
export Tenancy__Mode=MultiTenant

# Validation level
export Validation__Level=Profile
```

## Docker Environment

When running with Docker, pass environment variables:

```bash
docker run -p 8080:8080 \
  -e Storage__Provider=SqlServer \
  -e Storage__ConnectionString="Server=host.docker.internal;..." \
  ghcr.io/brendankowitz/ignixa-fhir:release
```

Or use Docker Compose with an `.env` file:

```yaml
# docker-compose.yml
services:
  ignixa:
    image: ghcr.io/brendankowitz/ignixa-fhir:release
    environment:
      - Storage__Provider=SqlServer
      - Storage__ConnectionString=${SQL_CONNECTION_STRING}
```

## Azure Deployment

For Azure deployments, use Managed Identity and Key Vault:

```json
{
  "Azure": {
    "UseManagedIdentity": true,
    "KeyVaultUri": "https://your-keyvault.vault.azure.net/"
  }
}
```

See [Azure Deployment](/docs/server/deployment/azure) for complete setup.

## Logging

Configure logging levels:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Ignixa": "Debug",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

## Complete Example

Full `appsettings.Production.json`:

```json
{
  "Storage": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=sql.example.com;Database=IgnixaFhir;User Id=app;Password=..."
  },
  "Tenancy": {
    "Mode": "MultiTenant",
    "DefaultTenantId": 1
  },
  "Fhir": {
    "DefaultVersion": "R4",
    "SupportedVersions": ["R4", "R4B", "R5"]
  },
  "Validation": {
    "Level": "Spec",
    "EnableProfileValidation": true
  },
  "Search": {
    "DefaultPageSize": 50,
    "MaxPageSize": 100
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Ignixa": "Information"
    }
  }
}
```

## Next Steps

- [Server Architecture](/docs/server/architecture) - Understand the internal design
- [Security Configuration](/docs/server/security/authentication) - Set up authentication
