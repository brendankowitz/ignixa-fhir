# ADR 2501: RBAC Authorization with Capability Statement Enforcement

## Status
Accepted

## Context
FHIR Server needs comprehensive authorization that enforces what the CapabilityStatement advertises. Legacy implementations often have OAuth/SMART authorization but **don't enforce CapabilityStatement** - if the server says "AuditEvent doesn't support update" but accepts `PUT /AuditEvent/123` anyway, that breaks FHIR conformance.

**Key Insight**: The CapabilityStatement is not just documentation - it's a **contract** the server must enforce.

## Decision
Implement a **5-layer authorization pipeline**:

| Layer | Checks | Performance |
|-------|--------|-------------|
| 1. Authentication | Is token valid? (JWT signature, expiration) | <0.5ms (cached) |
| 2. RBAC | Does role allow this? (Admin can delete users) | <0.1ms (in-memory) |
| 3. SMART Scopes | Do scopes allow this? (patient/Observation.read) | <0.5ms (parsed once) |
| 4. Capability Enforcement | Does server support this interaction? | <0.5ms (cached) |
| 5. Data Filtering | Filter by patient/compartment | Varies (query-level) |

**Core Abstractions:**
- `FhirAuthorizationContext` - Per-request context with user, tenant, roles, SMART scopes
- `IFhirAuthorizationService` - Main authorization service interface
- `SmartAuthorizationContext` - OAuth token claims and parsed scopes

**Critical Design Decision**: Authorization and auditing implemented as **endpoint filters**, NOT middleware. This ensures:
- Route-level context available (resource type, operation)
- Proper error responses with OperationOutcome
- Consistent behavior across all endpoints

## Consequences

**Positive:**
- Server behavior matches CapabilityStatement contract
- Layered checks enable early rejection (fail fast)
- Total authorization overhead ~1.5ms (acceptable)
- Multi-tenant isolation enforced at authorization layer

**Negative:**
- Capability enforcement requires keeping CapabilityStatement in sync with implementation
- Five layers add complexity vs single authorization check
- SMART scope parsing adds per-request overhead

## References
- Investigation: `docs/features/authorization/investigations/rbac-capabilities.md`
- Implementation: PR #110 (commit 5f81bd8)
