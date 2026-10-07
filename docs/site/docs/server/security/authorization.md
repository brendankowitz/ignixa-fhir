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

### Custom Audit Headers

Callers can attach their own fields to audit records with `X-IGNIXA-AUDIT-*` request headers:

```http
GET /tenant/1/Patient/123
X-IGNIXA-AUDIT-OPERATIONID: 6f1c2d4e
X-IGNIXA-AUDIT-BUNDLEID: ingest-42
```

For compatibility with existing Azure Health Data Services (AHDS) callers, the AHDS prefix
`X-MS-AZUREFHIR-AUDIT-*` is accepted too and treated the same way. The limits and errors match AHDS.

| Rule | Behavior |
|------|----------|
| Prefix | `X-IGNIXA-AUDIT-`, or `X-MS-AZUREFHIR-AUDIT-` for AHDS compatibility. Case-insensitive. Header names are recorded as received. |
| Count | At most **10** headers per request, counted across both prefixes. |
| Value length | At most **2048** characters per header. Repeated headers are comma-joined before the check. |
| Limit exceeded | **431 Request Header Fields Too Large** with an `OperationOutcome` (`severity: error`, `code: invalid`). The handler does not run. The rejection is audited without the headers. |

The headers are written to the audit channel and nowhere else in logs or error messages:

- **Structured audit log** (default): `CustomHeaders=name=value;name=value`, ordered by name, with control characters replaced.
- **Audit sidecar**: the `custom_headers` map on `AuditEventRequest` (`ignixa_audit.proto`), with values unchanged.

For `$export` and `$import`, the values are also persisted with the job record for its lifetime (see below).

Validation and capture run on every audited FHIR interaction, including `$export`/`$import` kick-off, status, and cancel requests.
Unaudited endpoints such as `/metadata` ignore these headers, as AHDS does. The MCP `start_export_job` and `start_import_job`
tools also validate the headers. A violation fails the tool call with the same message, but the rejection itself is not audited,
because the MCP route is outside the audit filter.

**Bundles.** The headers on a `batch` or `transaction` request are copied onto each entry's audit
record as well as the bundle request's own record. AHDS records them only on the outer request.

**Bulk jobs.** For `$export` and `$import`, whether started over HTTP or through the MCP job tools, the kick-off request's
user, correlation ID, and custom audit headers are stored with the job. When the job reaches `Completed`, `Failed`,
or `Cancelled`, one background-job audit event is emitted with those values. It is usually emitted when the job
finishes or is cancelled. If the runtime fails before the job records its own outcome, it is emitted on the next status check.
In the sidecar contract this is `custom_properties["event"] = "background-job-completed"`.
This event is an Ignixa extension; AHDS does not carry custom headers into background jobs.
Jobs created before this feature are audited as user `unknown`, without headers.

:::caution Audit delivery is best-effort
Audit events are emitted fire-and-forget. If the sidecar is unavailable, the event is logged as lost
and the request still succeeds. There is no durable outbox or retry.
:::

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
