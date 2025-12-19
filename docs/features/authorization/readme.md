# Feature: Authorization

Comprehensive authorization system with RBAC and SMART on FHIR scopes.

## Investigations

| Investigation | Status | Created | Description |
|--------------|--------|---------|-------------|
| [rbac-capabilities](investigations/rbac-capabilities.md) | Merged | 2025-01-08 | RBAC with CapabilityStatement enforcement architecture |

## Overview

This feature implements a layered authorization architecture for FHIR Server v2:

### Authorization Layers

1. **Authentication** - Token validation and identity verification
2. **RBAC** - Role-based access control for admin operations
3. **SMART Scopes** - OAuth 2.0 scope-based authorization (patient/*.read, user/*.write)
4. **Capability Enforcement** - Server enforces its own CapabilityStatement
5. **Data Filtering** - Patient compartment and search parameter filtering

### Key Components

- **FhirAuthorizationService** - Main authorization pipeline
- **Authorization Handlers** - Pluggable authorization checks
- **SmartScopeAuthorizationHandler** - SMART on FHIR scope validation
- **CapabilityEnforcementHandler** - Validates operations against CapabilityStatement
- **TenantIsolationHandler** - Multi-tenant access control

### Performance Target

Total authorization overhead: ~1.5ms per request

### Critical Insight

The CapabilityStatement is not just documentation - it's a **contract** the server must enforce. If the server advertises that AuditEvent doesn't support update, then PUT /AuditEvent/123 must be rejected even with valid auth.

## Decision

See [ADR-2501: RBAC Authorization with Capability Statement Enforcement](../../adr/adr-2501-authorization.md)
