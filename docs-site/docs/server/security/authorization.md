---
sidebar_position: 2
title: Authorization
description: Access control and permission management
---

# Authorization

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

```json
{
  "Authorization": {
    "Enabled": true,
    "DefaultPolicy": "authenticated",
    "EnforceScopesOnRead": true,
    "EnforceScopesOnWrite": true
  }
}
```

### Custom Policies

```json
{
  "Authorization": {
    "Policies": {
      "AdminOnly": {
        "RequiredRoles": ["admin"],
        "AllowedOperations": ["*"]
      },
      "ReadOnly": {
        "RequiredScopes": ["*.read"],
        "AllowedOperations": ["read", "search"]
      },
      "PatientAccess": {
        "RequiredScopes": ["patient/*"],
        "CompartmentRestriction": true
      }
    }
  }
}
```

### Resource-Level Policies

```json
{
  "Authorization": {
    "ResourcePolicies": {
      "Patient": {
        "Read": "authenticated",
        "Write": "AdminOnly",
        "Delete": "AdminOnly"
      },
      "Observation": {
        "Read": "PatientAccess",
        "Write": "PatientAccess"
      }
    }
  }
}
```

## Tenant-Based Authorization

In multi-tenant deployments, authorization is tenant-scoped:

```json
{
  "tenantId": "1",
  "scopes": ["patient/*.read"]
}
```

Users can only access resources within their authorized tenants.

### Tenant Claim Configuration

```json
{
  "Authorization": {
    "TenantClaim": "tenant_id",
    "CrossTenantAccess": false
  }
}
```

## Role-Based Access Control

### Define Roles

```json
{
  "Authorization": {
    "Roles": {
      "clinician": {
        "Scopes": ["patient/*.read", "patient/*.write"],
        "AllowedResourceTypes": ["Patient", "Observation", "Condition"]
      },
      "admin": {
        "Scopes": ["system/*.*"],
        "AllowedResourceTypes": ["*"]
      },
      "researcher": {
        "Scopes": ["system/*.read"],
        "ExcludedResourceTypes": ["Patient"]
      }
    }
  }
}
```

### Role Assignment

Roles are assigned via claims in the JWT:

```json
{
  "sub": "user123",
  "roles": ["clinician"],
  "tenant_id": "1"
}
```

## Operation-Level Authorization

Control access to specific operations:

```json
{
  "Authorization": {
    "Operations": {
      "$export": {
        "RequiredRoles": ["admin", "data-export"],
        "RequiredScopes": ["system/*.*"]
      },
      "$validate": {
        "RequiredScopes": ["*.read"]
      }
    }
  }
}
```

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
- [ADR: Authorization](/docs/adr/adr-2501-authorization)
