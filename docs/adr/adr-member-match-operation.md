# ADR: $member-match Operation Implementation

## Metadata

- **ADR Number**: N/A (Feature ADR)
- **Title**: $member-match Operation Implementation
- **Status**: Proposed
- **Date**: 2025-12-17
- **Related Documents**:
  - [Da Vinci HRex $member-match OperationDefinition](https://build.fhir.org/ig/HL7/davinci-ehrx/OperationDefinition-member-match.html)
  - [LinuxForHealth FHIR member-match reference implementation](https://github.com/LinuxForHealth/FHIR/tree/main/operation/fhir-operation-member-match)

## Context

### Background

The `$member-match` operation is a critical FHIR operation defined by the HL7 Da Vinci Health Record Exchange (HRex) Implementation Guide. It enables payers and health systems to identify a member (patient) in a target health plan using demographic and coverage information. This operation is essential for:

1. **Payer-to-Payer Data Exchange**: Required by CMS regulations for health plan interoperability
2. **Member Identification**: Allows a requesting payer to resolve a member's unique identifier in another payer's system
3. **Coverage Linking**: Enables linking of coverage records across payer systems

### Operation Overview

The `$member-match` operation is invoked as:
```
POST [base]/Patient/$member-match
```

### Input Parameters (HRex Specification)

The operation accepts a `Parameters` resource with the following parameters:

| Parameter | Cardinality | Type | Description |
|-----------|-------------|------|-------------|
| MemberPatient | 1..1 | Patient | Demographics of the member to match (HRex Patient Demographics profile) |
| CoverageToMatch | 1..1 | Coverage | Member's prior coverage information (from old insurance card) |
| CoverageToLink | 0..1 | Coverage | Member's new coverage (from requesting payer's enrollment) |
| Consent | 0..1 | Consent | Member's authorization for information sharing |

### Output Parameters (HRex Specification)

On successful match, the operation returns a `Parameters` resource containing:

| Parameter | Cardinality | Type | Description |
|-----------|-------------|------|-------------|
| MemberIdentifier | 1..1 | Identifier | The unique member identifier from the target payer |
| Patient | 0..1 | Reference(Patient) | RESTful reference to the matched Patient on the target system |

### Error Conditions

| HTTP Status | Condition |
|-------------|-----------|
| 422 Unprocessable Entity | No match found |
| 422 Unprocessable Entity | Multiple matches found |
| 400 Bad Request | Invalid input parameters |

## Decision

### Implementation Approach

We will implement the `$member-match` operation following the established patterns in the Ignixa FHIR Server:

1. **Command/Handler Pattern**: Use Medino for command/handler implementation
2. **Minimal API Endpoints**: Register endpoints in `OperationEndpoints.cs`
3. **Extensible Strategy Pattern**: Allow custom matching strategies via dependency injection
4. **Multi-Tenant Support**: Support both tenant-explicit and tenant-agnostic routes

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                 HTTP POST Request                                │
│  POST /Patient/$member-match (Parameters resource in body)       │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│               OperationEndpoints.cs (Minimal API)                │
│  - Parse Parameters resource from body                           │
│  - Extract MemberPatient, CoverageToMatch, CoverageToLink,       │
│    Consent parameters                                            │
│  - Create MemberMatchCommand                                     │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                   IMediator (Medino)                             │
│  SendAsync(MemberMatchCommand, ct)                               │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│             MemberMatchHandler (Application)                     │
│  1. Validate input parameters                                    │
│  2. Delegate to IMemberMatchStrategy for matching               │
│  3. Return MemberMatchResult                                     │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│            IMemberMatchStrategy (Pluggable)                      │
│  - DefaultMemberMatchStrategy: Identifier-based matching         │
│  - Custom strategies can be registered via DI                    │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│     IFhirRepository (Search for matching Patient/Coverage)       │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│              HTTP Response (Parameters resource)                 │
│  - MemberIdentifier: Matched member's identifier                 │
│  - Patient: Reference to matched patient (optional)             │
└─────────────────────────────────────────────────────────────────┘
```

### File Structure

**Application Layer** (`src/Application/Ignixa.Application.Operations/Features/MemberMatch/`):
```
MemberMatch/
├── MemberMatchCommand.cs           # Medino command
├── MemberMatchHandler.cs           # Command handler
├── MemberMatchResult.cs            # Result model
├── IMemberMatchStrategy.cs         # Strategy interface
└── DefaultMemberMatchStrategy.cs   # Default identifier-based matching
```

**API Layer** (endpoint registration in existing `OperationEndpoints.cs`):
- Add `$member-match` endpoint handlers

**Tests** (`test/Ignixa.Application.Tests/Features/MemberMatch/`):
```
MemberMatch/
├── MemberMatchHandlerTests.cs      # Unit tests for handler
└── MemberMatchStrategyTests.cs     # Unit tests for strategy
```

### Key Design Decisions

#### Decision 1: Strategy Pattern for Matching Logic

**Rationale**: Different implementations may require different matching algorithms:
- Deterministic matching (identifier-based)
- Probabilistic matching (demographics-based)
- Custom organizational logic

The strategy pattern allows:
- Default implementation for basic identifier matching
- Custom strategies via DI registration
- Testing with mock strategies

#### Decision 2: No Automatic Patient Creation

**Rationale**: Per HRex specification, `$member-match` is a read-only operation:
- Returns existing member identifier
- Does NOT create or modify Patient resources
- Returns 422 if no match found

#### Decision 3: Multi-Tenant Support

Routes will support both:
- Tenant-explicit: `POST /tenant/{tenantId}/Patient/$member-match`
- Tenant-agnostic: `POST /Patient/$member-match` (single-tenant auto-detect)

#### Decision 4: Minimal Profile Validation

Initial implementation will:
- Accept any valid Patient and Coverage resources
- Log warnings for non-conformant resources
- NOT require strict HRex profile conformance

Future enhancement can add profile validation via Tier 2 validation.

## Implementation

### MemberMatchCommand

```csharp
namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Command for $member-match operation.
/// Matches a member across payer systems using demographics and coverage.
/// </summary>
/// <param name="MemberPatient">Patient resource with demographics for matching</param>
/// <param name="CoverageToMatch">Coverage resource with prior plan information</param>
/// <param name="CoverageToLink">Optional coverage resource with new plan information</param>
/// <param name="Consent">Optional consent for information sharing</param>
public record MemberMatchCommand(
    ResourceJsonNode MemberPatient,
    ResourceJsonNode CoverageToMatch,
    ResourceJsonNode? CoverageToLink = null,
    ResourceJsonNode? Consent = null) : IRequest<MemberMatchResult>;
```

### MemberMatchResult

```csharp
namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Result of $member-match operation.
/// </summary>
/// <param name="MemberIdentifier">Matched member's unique identifier</param>
/// <param name="PatientReference">Optional reference to matched Patient resource</param>
/// <param name="Success">Whether a unique match was found</param>
/// <param name="ErrorMessage">Error message if matching failed</param>
public record MemberMatchResult(
    IdentifierJsonNode? MemberIdentifier,
    string? PatientReference,
    bool Success,
    string? ErrorMessage = null);
```

### IMemberMatchStrategy

```csharp
namespace Ignixa.Application.Operations.Features.MemberMatch;

/// <summary>
/// Strategy interface for member matching logic.
/// Allows custom matching implementations.
/// </summary>
public interface IMemberMatchStrategy
{
    /// <summary>
    /// Attempt to match a member using provided demographics and coverage.
    /// </summary>
    /// <param name="memberPatient">Patient demographics for matching</param>
    /// <param name="coverageToMatch">Prior coverage information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Match result with member identifier if found</returns>
    Task<MemberMatchResult> MatchAsync(
        ResourceJsonNode memberPatient,
        ResourceJsonNode coverageToMatch,
        CancellationToken cancellationToken);
}
```

### DefaultMemberMatchStrategy

The default strategy performs identifier-based matching:

1. Extract identifiers from `MemberPatient` (subscriber ID, member ID)
2. Extract identifiers from `CoverageToMatch` (subscriberId, beneficiary reference)
3. Search for Patient with matching identifier
4. Verify Coverage linkage
5. Return unique match or error

### Endpoint Registration

```csharp
// In OperationEndpoints.cs - tenant-explicit routes
tenantGroup.MapPost("/Patient/$member-match", HandleMemberMatch)
    .WithName("MemberMatch")
    .Accepts<object>(KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(StatusCodes.Status400BadRequest, KnownContentTypes.ApplicationFhirJson)
    .Produces<object>(StatusCodes.Status422UnprocessableEntity, KnownContentTypes.ApplicationFhirJson);

// Tenant-agnostic route
endpoints.MapPost("/Patient/$member-match", HandleMemberMatchAgnostic)
    .WithName("MemberMatchAgnostic")
    // ... same produces
```

## Consequences

### Positive

1. **Standards Compliance**: Implements HRex $member-match as specified
2. **Extensibility**: Strategy pattern allows custom matching logic
3. **Consistency**: Follows existing Ignixa patterns (Medino, Minimal API)
4. **Multi-Tenant**: Works with existing tenant infrastructure
5. **Testability**: Strategy pattern enables easy unit testing

### Negative

1. **Basic Matching**: Default strategy is simple identifier-based; sophisticated matching requires custom strategy
2. **No Profile Validation**: Initial implementation doesn't validate HRex profiles
3. **Single-System Match**: Designed for single-server matching; federated matching requires additional work

### Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Complex matching requirements | Strategy pattern allows custom implementations |
| Performance with large datasets | Use indexed search parameters |
| Multiple matches found | Return 422 per specification |
| No matches found | Return 422 per specification |

## Testing Strategy

### Unit Tests

1. **MemberMatchHandlerTests**: Test handler orchestration
   - Valid parameters → successful match
   - Missing MemberPatient → validation error
   - Missing CoverageToMatch → validation error
   - Strategy returns no match → 422 response
   - Strategy returns multiple matches → 422 response

2. **DefaultMemberMatchStrategyTests**: Test matching logic
   - Match by subscriber ID
   - Match by member ID
   - No identifier match → no match
   - Multiple identifier matches → multiple matches error

### Integration Tests

1. **MemberMatchEndpointTests**: E2E tests
   - POST /Patient/$member-match with valid parameters
   - POST /Patient/$member-match with missing parameters
   - Tenant-explicit route
   - Tenant-agnostic route (single-tenant)

## References

- [HRex $member-match OperationDefinition](https://build.fhir.org/ig/HL7/davinci-ehrx/OperationDefinition-member-match.html)
- [HRex Parameters Member Match Request](https://build.fhir.org/ig/HL7/davinci-ehrx/StructureDefinition-hrex-parameters-member-match-in.html)
- [LinuxForHealth FHIR member-match](https://github.com/LinuxForHealth/FHIR/tree/main/operation/fhir-operation-member-match)
- [CMS Interoperability and Prior Authorization Final Rule](https://www.cms.gov/regulations-and-guidance/guidance/interoperability/index)

## Approval

- **Status**: Proposed
- **Date**: 2025-12-17
