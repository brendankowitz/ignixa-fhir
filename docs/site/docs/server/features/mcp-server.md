---
sidebar_position: 4
title: MCP Server
description: Model Context Protocol server for AI integration
---

# MCP Server

Ignixa includes an experimental [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) server that enables AI assistants like Claude to interact with FHIR data directly.

:::caution Experimental Feature
MCP support is experimental and may change in future releases.
:::

## Overview

The MCP server exposes FHIR operations as tools that AI assistants can invoke. This enables natural language interactions like:

- "Find all patients named Smith"
- "Show me the latest observations for patient 123"
- "Update the patient's phone number"
- "Install the US Core implementation guide"

## Configuration

Enable MCP in `appsettings.json`:

```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": {
        "Enabled": true,
        "Transport": "http"
      }
    }
  }
}
```

Default: Enabled (when Experimental:Enabled is true)

## Endpoint

The MCP server is accessible at:

```
POST /mcp
```

This endpoint uses MCP Streamable HTTP transport for bidirectional streaming with AI clients like Claude.

## Available Tools

### FHIR Operations

#### search_fhir_resources

Search for FHIR resources with LLM-optimized response sizes.

```json
{
  "name": "search_fhir_resources",
  "arguments": {
    "resourceType": "Patient",
    "searchParams": {"name": "Smith", "birthdate": "gt2000"},
    "count": 10,
    "elements": "id,name,birthDate",
    "summary": "true"
  }
}
```

| Parameter | Description |
|-----------|-------------|
| `resourceType` | Resource type (Patient, Observation, etc.) |
| `searchParams` | Search parameters as key-value pairs |
| `count` | Max results (default: 10, max: 50) |
| `elements` | Comma-separated fields to include |
| `summary` | Summary mode: `true`, `data`, `text`, `count` |
| `total` | Total count: `accurate`, `estimate`, `none` |
| `tenantId` | Optional tenant ID |

#### get_fhir_resource

Retrieve a single resource by ID.

```json
{
  "name": "get_fhir_resource",
  "arguments": {
    "resourceType": "Patient",
    "id": "123"
  }
}
```

#### patch_resource_field

Update a single field in a resource. Simple interface for common updates.

```json
{
  "name": "patch_resource_field",
  "arguments": {
    "resourceType": "Patient",
    "resourceId": "123",
    "fieldPath": "active",
    "value": true,
    "operation": "set"
  }
}
```

| Parameter | Description |
|-----------|-------------|
| `fieldPath` | FHIRPath to field (e.g., `active`, `name[0].given`) |
| `value` | New value (string, number, boolean, or JSON) |
| `operation` | `set`, `delete`, `add`, or `remove` |

#### patch_fhir_resource

Apply multiple FHIRPath Patch operations to a resource.

```json
{
  "name": "patch_fhir_resource",
  "arguments": {
    "resourceType": "Patient",
    "resourceId": "123",
    "operationsJson": "[{\"type\":\"replace\",\"path\":\"Patient.active\",\"value\":true}]"
  }
}
```

Operations format:
```json
[
  {"type": "replace", "path": "Patient.active", "value": true},
  {"type": "add", "path": "Patient.telecom", "value": {"system": "phone", "value": "555-1234"}},
  {"type": "delete", "path": "Patient.photo"}
]
```

#### get_resource_history

Get version history for a resource.

```json
{
  "name": "get_resource_history",
  "arguments": {
    "resourceType": "Patient",
    "id": "123",
    "count": 10
  }
}
```

### Package Management

#### search_fhir_packages

Search for FHIR packages in the NPM registry with fuzzy matching.

```json
{
  "name": "search_fhir_packages",
  "arguments": {
    "query": "us core",
    "maxResults": 10
  }
}
```

#### get_fhir_package_details

Get detailed information about a package including all versions.

```json
{
  "name": "get_fhir_package_details",
  "arguments": {
    "packageId": "hl7.fhir.us.core"
  }
}
```

#### install_fhir_package

Install a FHIR package from the NPM registry.

```json
{
  "name": "install_fhir_package",
  "arguments": {
    "packageId": "hl7.fhir.us.core",
    "version": "6.1.0"
  }
}
```

#### list_fhir_packages

List installed packages for a tenant.

```json
{
  "name": "list_fhir_packages",
  "arguments": {
    "tenantId": 1
  }
}
```

#### uninstall_fhir_package

Uninstall a package from a tenant.

```json
{
  "name": "uninstall_fhir_package",
  "arguments": {
    "packageId": "hl7.fhir.us.core",
    "version": "6.1.0"
  }
}
```

### Job Management

#### start_export_job

Start a bulk export job.

```json
{
  "name": "start_export_job",
  "arguments": {
    "resourceTypes": ["Patient", "Observation"],
    "since": "2024-01-01T00:00:00Z"
  }
}
```

#### start_import_job

Start a bulk import job.

```json
{
  "name": "start_import_job",
  "arguments": {
    "sourceUrl": "https://storage.example.com/import/",
    "resourceTypes": ["Patient", "Observation"]
  }
}
```

#### get_job_status

Check the status of an import/export job.

```json
{
  "name": "get_job_status",
  "arguments": {
    "jobId": "abc123",
    "jobType": "Export"
  }
}
```

#### list_jobs

List recent import/export jobs.

```json
{
  "name": "list_jobs",
  "arguments": {
    "jobType": "Export",
    "maxResults": 10
  }
}
```

### Tenant Management

#### list_tenants_info

List all available tenants with their configuration.

```json
{
  "name": "list_tenants_info",
  "arguments": {}
}
```

Returns tenant IDs, names, FHIR versions, and validation settings.

## Authorization

MCP tools respect the same authorization rules as the REST API. Configure MCP-enabled roles:

```json
{
  "Authorization": {
    "McpEnabledRoles": ["Admin", "SystemAdmin", "Mcp", "Contributor"]
  }
}
```

| Role | MCP Access | Description |
|------|------------|-------------|
| `Admin` | Full | All operations |
| `SystemAdmin` | Full | Cross-tenant access |
| `Mcp` | Limited | Read, create, update, delete, search |
| `Contributor` | Full | All FHIR and MCP operations |
| `Clinician` | None | No MCP access by default |
| `ReadOnly` | None | No MCP access |

## Multi-Tenancy

MCP tools are tenant-aware. When multiple tenants exist:

1. Use `list_tenants_info` to discover available tenants
2. Pass `tenantId` parameter to tools
3. If omitted and only one tenant exists, it's auto-selected

## Response Size Optimization

MCP responses are optimized for LLM context windows:

- **Default count**: 10 results (max 50)
- **Use `elements`**: Request only needed fields
- **Use `summary`**: Core fields only (`true`) or exclude narratives (`data`)
- **Use `total=none`**: Skip expensive count queries

Example optimized search:
```json
{
  "resourceType": "Observation",
  "searchParams": {"patient": "123", "code": "http://loinc.org|85354-9"},
  "count": 5,
  "elements": "id,code,valueQuantity,effectiveDateTime",
  "summary": "data"
}
```

## Connecting Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "ignixa-fhir": {
      "url": "http://localhost:5027/mcp",
      "transport": "sse"
    }
  }
}
```

Note: Replace `localhost:5000` with your server's actual URL. The MCP server uses HTTP streaming (Streamable HTTP) for bidirectional communication with Claude.

## Related Documentation

- [Configuration](/docs/server/configuration) - Enable experimental features
- [Authorization](/docs/server/security/authorization) - Configure MCP roles
- [Bulk Operations](/docs/server/features/bulk-operations) - Export/import details
