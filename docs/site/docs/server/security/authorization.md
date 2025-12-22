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
| `patient/Patient.read` | Read patient's own record |
| `patient/Observation.read` | Read patient's observations |
| `patient/*.read` | Read all in patient compartment |
| `user/Patient.write` | User can write patients |
| `system/*.*` | Full system access |

## Patient Compartment

When using `patient/` scopes, access is restricted to the patient compartment:

```json
{
  "context": {
    "patient": "Patient/123"
  },
  "scopes": ["patient/Observation.read"]
}
```

Only returns Observations where:
- `Observation.subject` references `Patient/123`

### Compartment Resources

Resources in the Patient compartment:

- Observation
- Condition
- Procedure
- MedicationRequest
- Encounter
- DiagnosticReport
- CarePlan
- ... (all clinical resources)

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

### Patient Compartment Violation

```json
{
  "resourceType": "OperationOutcome",
  "issue": [{
    "severity": "error",
    "code": "forbidden",
    "diagnostics": "Resource Patient/456 not in authorized patient compartment"
  }]
}
```

## Security Best Practices

1. **Principle of Least Privilege** - Grant minimum required scopes
2. **Use Patient Compartment** - Restrict clinical app access
3. **Enable Audit Logging** - Track all access
4. **Rotate API Keys** - Regular key rotation
5. **Validate Tokens** - Strict JWT validation

## Related Documentation

- [Authentication](/docs/server/security/authentication)
- [ADR: Authorization](https://github.com/brendankowitz/ignixa-fhir/blob/main/docs/adr/adr-2501-authorization.md)
