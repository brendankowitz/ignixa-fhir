---
sidebar_position: 3
title: Multi-Tenancy
description: Physical data isolation and tenant routing
---

# Multi-Tenancy

Ignixa supports multi-tenant deployments with physical data isolation between tenants.

## Overview

Multi-tenancy enables a single Ignixa deployment to serve multiple isolated healthcare organizations, each with their own:

- Data partition
- Configuration
- Access controls

## Tenant Routing

In multi-tenant mode, resources are accessed via tenant-prefixed URLs:

```
/tenant/{tenantId}/{resourceType}/{id}
```

### Single Tenant Mode

```bash
# Both work
GET /Patient/123
GET /tenant/1/Patient/123
```

### Multi-Tenant Mode

```bash
# Only tenant-prefixed routes work
GET /tenant/1/Patient/123    ✅
GET /tenant/2/Patient/456    ✅
GET /Patient/123             ❌ 400 Bad Request
```

## Configuration

Enable multi-tenancy in `appsettings.json`:

```json
{
  "Tenancy": {
    "Mode": "MultiTenant",
    "DefaultTenantId": 1
  }
}
```

| Setting | Description |
|---------|-------------|
| `Mode` | `SingleTenant` or `MultiTenant` |
| `DefaultTenantId` | Fallback tenant for ambiguous requests |

## Reserved Tenant

:::warning Tenant 0 is Reserved
Tenant ID `0` is reserved for system operations and cannot be accessed via the API.

```bash
GET /tenant/0/Patient/123    ❌ 400 Bad Request
```
:::

The system tenant stores:
- Transaction ID sequences
- System-level metadata
- Internal state

## Data Isolation

Each tenant has physically separate data:

```
┌─────────────────────────────────────────┐
│              SQL Server                  │
├─────────────┬─────────────┬─────────────┤
│  Tenant 1   │  Tenant 2   │  Tenant 3   │
│             │             │             │
│ ┌─────────┐ │ ┌─────────┐ │ ┌─────────┐ │
│ │ Patient │ │ │ Patient │ │ │ Patient │ │
│ │ Observ. │ │ │ Observ. │ │ │ Observ. │ │
│ │  ...    │ │ │  ...    │ │ │  ...    │ │
│ └─────────┘ │ └─────────┘ │ └─────────┘ │
└─────────────┴─────────────┴─────────────┘
```

### Isolation Guarantees

- ✅ Queries cannot cross tenant boundaries
- ✅ References are validated within tenant
- ✅ Search results are tenant-scoped
- ✅ Bundles process within single tenant

## Middleware Flow

```csharp
// Request: GET /tenant/2/Patient/123

1. TenantResolutionMiddleware
   - Extract tenantId = 2 from route
   - Validate: tenantId != 0
   - Set HttpContext.Items["TenantId"] = 2

2. Handler Execution
   - Repository uses scoped tenant context
   - All operations filtered by tenantId

3. Response
   - Links include tenant prefix
   - CapabilityStatement reflects tenant config
```

## Azure Deployment

For Azure deployments, each tenant can have dedicated resources:

```json
{
  "Tenancy": {
    "Mode": "MultiTenant",
    "TenantConfiguration": {
      "1": {
        "ConnectionString": "Server=tenant1-sql.database.windows.net;..."
      },
      "2": {
        "ConnectionString": "Server=tenant2-sql.database.windows.net;..."
      }
    }
  }
}
```

## Provisioning Tenants

Tenants are provisioned via the admin API or configuration:

### Via Configuration

```json
{
  "Tenancy": {
    "Tenants": [
      { "Id": 1, "Name": "Hospital A" },
      { "Id": 2, "Name": "Clinic B" }
    ]
  }
}
```

### Via Deployment

Using the Azure Bicep templates:

```bash
az deployment group create \
  --template-file deploy/azure/main.bicep \
  --parameters tenantCount=10
```

## Related Documentation

- [ADR: Multi-Tenancy](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2510-multi-tenancy.md)
- [Azure Deployment](/docs/server/deployment/azure)
