---
sidebar_position: 2
title: Authorization
description: Access control and permission management
---

# Authorization

:::caution Under Development
Authorization features are under active development. Configuration options and APIs may change.
:::

Ignixa provides fine-grained authorization based on SMART on FHIR scopes and custom policies.

## Access Control Model

```
┌─────────────────────────────────────────────────────────────┐
│                      Request                                 │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                  Authentication                              │
│           (JWT, API Key, SMART Token)                       │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                    Scope Extraction                          │
│              (patient/*.read, system/*.*)                   │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                   Policy Evaluation                          │
│        (Resource type, Operation, Context)                  │
└──────────────────────────┬──────────────────────────────────┘
                           │
                    ┌──────┴──────┐
                    ▼             ▼
               Allowed        Denied (403)
```

## SMART Scopes

### Scope Format

```
<context>/<resource-type>.<permission>
```

| Component | Values |
|-----------|--------|
| Context | `patient`, `user`, `system` |
| Resource Type | `Patient`, `Observation`, `*` |
| Permission | `read`, `write`, `*` |

### Examples

| Scope | Description |
|-------|-------------|
| `patient/Patient.read` | Read patient's own record (refused for now; see below) |
| `patient/Observation.read` | Read patient's observations (refused for now) |
| `patient/*.read` | Read all in patient compartment (refused for now) |
| `user/Patient.write` | User can write patients |
| `system/*.*` | Full system access |

## Compartment and Constrained Scopes

The server does not yet filter results by compartment or by a scope's search constraints. A request whose grant
depends on one is refused with `403 Forbidden` rather than answered with unrestricted data. That covers:

- `patient/` scopes, and `practitioner/` scopes;
- any scope that carries search parameters, such as `system/Observation.rs?category=laboratory`.

Use a `user/` or `system/` scope without search parameters until compartment enforcement is available.

When a token holds both, the scope without restrictions is used, whatever their order in the token.

## Operations and Bundles

SMART has no permission for operations, so each operation route declares the interaction it is authorized as. A
route that declares none is refused.

| Routes | Authorized as |
|---|---|
| `$validate` on a type or instance | Read on that type |
| `/$validate` (system) | Read on `*` |
| `$everything`, `$summary`, `$member-match`, `$graphql`, `$includes` | Search on `*` |
| `$de-identify`, `$export` (system, tenant and Group), export status and cancel | Read on `*` |
| `$import`, import status and cancel | Update on `*` |
| `$bulk-delete` on a type | Delete on that type, then on every type the job reaches |
| `/$bulk-delete` (system) | Delete on `*`, then on every type the job reaches |
| `ValueSet/$expand`, `ConceptMap/$translate`, `CodeSystem/$subsumes`, `StructureMap/$transform` | Read on `ValueSet`, `ConceptMap`, `CodeSystem` or `StructureMap` |
| `/tenant/{id}/admin/packages` (list) | Read on `*` |
| `/tenant/{id}/admin/packages` (load, unload) | Update on `*` |

GraphQL mutations (`PatientCreate`, `PatientUpdate`, `PatientDelete` and so on) are refused with `403 Forbidden`
while authorization is enabled, because they write without a per-resource permission check. Use the REST create,
update and delete interactions instead. The GraphQL IDE (`Experimental:Features:GraphQl:EnableGraphQlIde`) is not mapped while
authorization is enabled, because its executor bypasses these checks.

Package load and unload change search behaviour for every tenant. While more than one tenant is active they also
require the `SystemAdmin` role.

A `batch` or `transaction` bundle is authorized in two steps. The bundle request itself needs a grant on `*` (any
SMART scope on `*`, or an RBAC grant of `transaction` on `*`). Each entry then runs through its own
endpoint's authorization as the interaction it performs, so a `PUT` entry also needs update on its type. One
refused entry fails a transaction; a batch reports it as that entry's response.

A caller that is authenticated but holds neither a SMART resource scope nor a role is refused on every protected
route.

## Configuration

### Basic Authorization

Enable authorization and control behavior:

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

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Whether authorization is enforced | `true` |
| `RequireAuthentication` | Whether authentication required for all endpoints (except `/metadata`) | `true` |
| `EnforceTenantIsolation` | Whether to enforce tenant boundaries in authorization | `true` |
| `EnforceCapabilities` | Whether to enforce CapabilityStatement compliance (reject unsupported operations) | `true` |

### Default Roles

Configure default role permissions:

```json
{
  "Authorization": {
    "DefaultRoles": {
      "Admin": {
        "Permissions": [
          { "ResourceType": "*", "Interaction": "*" }
        ],
        "McpAccess": true
      },
      "Clinician": {
        "Permissions": [
          { "ResourceType": "Patient", "Interaction": "read" },
          { "ResourceType": "Observation", "Interaction": "*" },
          { "ResourceType": "Condition", "Interaction": "*" }
        ],
        "McpAccess": false
      }
    },
    "McpEnabledRoles": ["Admin", "SystemAdmin", "Mcp"]
  }
}
```

Roles are assigned via JWT claims:
```json
{
  "sub": "user123",
  "roles": ["Clinician"],
  "tenant_id": "1"
}
```

## Tenant-Based Authorization

In multi-tenant deployments, authorization is tenant-scoped. Users can only access resources within their authorized tenants.

The tenant ID is extracted from the request path (`/tenant/{tenantId}/...`) and enforced at the authorization layer. If `EnforceTenantIsolation` is enabled, cross-tenant access is blocked.

## Audit Logging

All authorization decisions are logged:

```json
{
  "timestamp": "2024-01-15T10:30:00Z",
  "action": "read",
  "resource": "Patient/123",
  "principal": "user@example.org",
  "scopes": ["patient/Patient.read"],
  "decision": "allow",
  "tenantId": "1"
}
```

### Configuration

```json
{
  "AuditLog": {
    "Enabled": true,
    "LogSuccessfulAccess": true,
    "LogDeniedAccess": true,
    "RetentionDays": 2190  // 6 years for HIPAA
  }
}
```

## Error Handling

### Insufficient Scope

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "forbidden",
    "diagnostics": "Access denied: requires scope patient/Patient.write, has patient/Patient.read"
  }]
}
```

### Constrained Scope

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "forbidden",
    "diagnostics": "Grants limited to a compartment or search constraint (patient, practitioner or encounter scopes, or scopes with search parameters) are not supported yet; use an unrestricted grant."
  }]
}
```

## Security Best Practices

1. **Principle of Least Privilege** - Grant minimum required scopes
2. **Scope per resource type** - Grant the types an app needs rather than `*`
3. **Enable Audit Logging** - Track all access
4. **Rotate API Keys** - Regular key rotation
5. **Validate Tokens** - Strict JWT validation

## Related Documentation

- [Authentication](/docs/server/security/authentication)
- [ADR: Authorization](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2501-authorization.md)
