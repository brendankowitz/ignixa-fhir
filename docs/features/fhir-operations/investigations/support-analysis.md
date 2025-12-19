# Investigation: FHIR Operations Support Analysis

**Feature**: fhir-operations
**Status**: Investigation Complete
**Created**: 2025-11-18

**Date**: 2025-11-18
**Status**: Investigation Complete
**Purpose**: Comprehensive analysis of FHIR operations used in US Core and other common IGs vs. current Ignixa support

---

## Executive Summary

This investigation analyzes FHIR operations required and commonly used across major Implementation Guides (IGs) to identify gaps in Ignixa's current operation support. We surveyed US Core, IPA, Da Vinci family (HRex, PDex, CDex, DTR, CRD), CARIN Blue Button, mCode, and the core FHIR specification.

**Current Support**: 2 operations ($validate, $export)
**Total Operations Identified**: 40+ standard operations
**Critical Gaps**: 10 high-priority operations for US healthcare interoperability

---

## Current Operation Support in Ignixa

### Implemented Operations ✅

| Operation | Type | Resources | Status | Implementation |
|-----------|------|-----------|--------|----------------|
| `$validate` | System/Type/Instance | All resources | ✅ Implemented | `OperationEndpoints.cs` (lines 20-316) |
| `$export` | System/Type/Instance | System, Patient, Group | ✅ Implemented | `ExportEndpoints.cs` + DurableTask orchestration |

### Operation Registration Pattern

Operations are registered via the `IPackageFeature` interface:

```csharp
public interface IPackageFeature {
    string PackageId { get; }
    IReadOnlyList<string> SystemOperations { get; }
    IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations { get; }
    IReadOnlyList<string>? SupportedFhirVersions { get; }
}
```

**Example**: `BulkDataExportFeature` (lines 1-33) declares:
- System operations: `["export"]`
- Resource operations: `Patient => ["export"]`, `Group => ["export"]`
- Package: `"hl7.fhir.uv.bulkdata"`

---

## FHIR Operations by Implementation Guide

### 1. US Core (STU 6.1/7.0) - 2024

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$docref` | Instance | DocumentReference | **SHALL** | Retrieve patient documents (CDA, FHIR) |
| `$expand` | Instance | ValueSet | Listed | Expand value sets for terminology binding |

**Notes**:
- US Core focuses primarily on RESTful search interactions rather than operations
- `$docref` is the only explicitly required operation
- `$expand` is implied for terminology services

**References**:
- [US Core Server CapabilityStatement](https://build.fhir.org/ig/HL7/US-Core/CapabilityStatement-us-core-server.html)
- [US Core Search Parameters and Operations](https://hl7.org/fhir/us/core/search-parameters-and-operations.html)

---

### 2. International Patient Access (IPA) - STU 1.0

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$docref` | Type | DocumentReference | **SHALL** | Return references to patient documents |

**Implementation Details**:
- **Invocation**: `GET [base]/DocumentReference/$docref?patient=[id]`
- **Response**: Bundle with references to CDA or FHIR documents
- **Context date range**: Servers should return documents within supplied range, or from last/current encounter

**References**:
- [IPA Server CapabilityStatement](https://hl7.org/fhir/uv/ipa/CapabilityStatement-ipa-server.html)

---

### 3. Da Vinci Project IGs

#### 3.1 Health Record Exchange (HRex) - STU 1.0

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$member-match` | Type | Patient | **SHALL** (for payers) | Retrieve unique member identifier from another health plan |

**Input Parameters**:
- `MemberPatient` (required): US Core Patient with demographics
- `CoverageToMatch` (required): Coverage from member's health plan card
- `CoverageToLink` (optional): Requesting payer's coverage info
- `Consent` (optional): Member authorization

**Output**:
- `MemberIdentifier` (required): Target payer's unique member ID
- `MemberId` (optional): RESTful reference to patient record

**References**:
- [HRex $member-match OperationDefinition](https://www.hl7.org/fhir/us/davinci-hrex/OperationDefinition-member-match.html)

#### 3.2 Payer Data Exchange (PDex) - STU 2.0

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$patient-everything` | Instance | Patient | **SHALL** | Retrieve all patient-related data |
| `$group-everything` | Instance | Group | **SHOULD** | Bulk data retrieval for patient cohorts |

**Notes**:
- Enhanced $everything supports Practitioners and Organizations beyond just Patient
- Used for payer-to-payer data exchange

**References**:
- [Da Vinci PDex Tutorial](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/davinci-pdex-tutorial)

#### 3.3 Clinical Data Exchange (CDex) - STU 2.1

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$submit-attachment` | Type | Claim | **SHALL** | Submit clinical/administrative attachments for claims or prior auth |

**Use Cases**:
- **Solicited attachments**: Response to payer request for additional info
- **Unsolicited attachments**: Proactive submission before/with claim

**Input**:
- Attachments (clinical documents, forms, images)
- Association data (claim ID, prior auth ID)

**Output**:
- HTTP response confirming receipt

**References**:
- [CDex $submit-attachment OperationDefinition](https://build.fhir.org/ig/HL7/davinci-ecdx/OperationDefinition-submit-attachment.html)

#### 3.4 Documentation Templates and Rules (DTR) - STU 2.1

**Required Operations**:

| Operation | Type | Resource | Requirement | Purpose |
|-----------|------|----------|-------------|---------|
| `$questionnaire-package` | Type | Questionnaire | **SHALL** | Retrieve questionnaires with dependencies for prior auth documentation |

**Input**:
- `Coverage`: Member and coverage type(s)
- `canonical` (optional): Questionnaire URL(s) and versions

**Output**:
- Bundle containing:
  - Questionnaire resource(s)
  - QuestionnaireResponse resource(s)
  - Dependency Library and ValueSet instances

**Integration**:
- Works with CRD (alerts provider), DTR (captures docs), PAS (submits prior auth)

**References**:
- [DTR $questionnaire-package OperationDefinition](https://www.hl7.org/fhir/us/davinci-dtr/OperationDefinition-questionnaire-package.html)

#### 3.5 Coverage Requirements Discovery (CRD) - STU 2.1

**No custom operations** - uses CDS Hooks and SMART App Launch

---

### 4. CARIN Blue Button - STU 2.1

**No custom operations** - relies on standard RESTful interactions:
- `read` (SHALL)
- `vread` (SHOULD)
- `search-type` (MAY)
- `_include` searches

**References**:
- [CARIN BB CapabilityStatement](https://hl7.org/fhir/us/carin-bb/CapabilityStatement-c4bb.html)

---

### 5. mCode (Cancer Data) - STU 4.0

**No custom operations** - focuses on profiles and extensions for oncology data

**References**:
- [mCode Implementation Guide](https://build.fhir.org/ig/HL7/fhir-mCODE-ig/)

---

## Core FHIR Operations (R4/R5 Specification)

### Base Operations (All Resources)

| Operation | Level | Purpose | Priority |
|-----------|-------|---------|----------|
| `$validate` | System/Type/Instance | Validate resource structure | ✅ Implemented |
| `$meta` | System/Type/Instance | Access profiles, tags, security labels | Medium |
| `$meta-add` | Instance | Add metadata to resource | Low |
| `$meta-delete` | Instance | Remove metadata from resource | Low |
| `$convert` | System/Type | Transform resource formats | Low |
| `$graphql` | System | Execute GraphQL statement | Medium |
| `$graph` | Instance | Retrieve connected resource networks | Low |

### Clinical Operations

| Operation | Resource | Purpose | Priority | IG Usage |
|-----------|----------|---------|----------|----------|
| `$everything` | Patient | Return all patient data | **HIGH** | US Core, IPA, PDex |
| `$everything` | Encounter | Return all encounter data | Medium | FHIR Core |
| `$everything` | EpisodeOfCare | Return all episode data | Low | FHIR Core |
| `$match` | Patient | Master Patient Index matching | Medium | FHIR Core |
| `$merge` | Patient | Merge patient records | Medium | FHIR Core |
| `$stats` | Observation | Statistical analysis of observations | Low | FHIR Core |
| `$lastn` | Observation | Retrieve last N observations | Medium | FHIR Core |

### Terminology Operations

| Operation | Resource | Purpose | Priority | IG Usage |
|-----------|----------|---------|----------|----------|
| `$expand` | ValueSet | Expand value set to codes | **HIGH** | US Core, IPA |
| `$validate-code` | CodeSystem/ValueSet | Validate code membership | **HIGH** | Terminology servers |
| `$lookup` | CodeSystem | Retrieve concept details | **HIGH** | Terminology servers |
| `$translate` | ConceptMap | Translate codes between systems | Medium | Terminology servers |
| `$subsumes` | CodeSystem | Test subsumption relationship | Medium | Terminology servers |
| `$find-matches` | CodeSystem | Find matching concepts | Low | FHIR Core |
| `$closure` | ConceptMap | Maintain closure table | Low | FHIR Core |

### Knowledge/Quality Operations

| Operation | Resource | Purpose | Priority |
|-----------|----------|---------|----------|
| `$apply` | ActivityDefinition/PlanDefinition | Apply definition to context | Low |
| `$data-requirements` | Library/Measure | Analyze data requirements | Low |
| `$evaluate-measure` | Measure | Evaluate quality measure | Medium |
| `$submit-data` | Measure | Submit quality measure data | Low |
| `$collect-data` | Measure | Collect measure data | Low |
| `$care-gaps` | Measure | Identify care gaps | Low |

### Document/Form Operations

| Operation | Resource | Purpose | Priority | IG Usage |
|-----------|----------|---------|----------|----------|
| `$document` | Composition | Generate FHIR document bundle | Medium | Clinical documents |
| `$populate` | Questionnaire | Pre-populate questionnaire | Medium | SDC, DTR |
| `$docref` | DocumentReference | Retrieve patient documents | **HIGH** | US Core, IPA |

### Administrative Operations

| Operation | Resource | Purpose | Priority |
|-----------|----------|---------|----------|
| `$submit` | Claim | Submit claim for adjudication | Low |
| `$process-message` | MessageHeader | Process FHIR message | Low |

### Subscription Operations (R5)

| Operation | Resource | Purpose | Priority |
|-----------|----------|---------|----------|
| `$events` | Subscription | Retrieve past subscription events | Medium |
| `$status` | Subscription | Get subscription status | Medium |

---

## Gap Analysis

### High Priority Gaps (Required for US Healthcare Interoperability)

| Operation | Required By | Current Status | Implementation Effort | Notes |
|-----------|-------------|----------------|----------------------|-------|
| `$docref` | US Core, IPA | ❌ Missing | Medium (20-30 hrs) | Core requirement for document access |
| `$expand` | US Core, Terminology | ❌ Missing | Medium (20-30 hrs) | Terminology service essential |
| `$validate-code` | Terminology | ❌ Missing | Medium (15-25 hrs) | Terminology validation |
| `$lookup` | Terminology | ❌ Missing | Medium (15-25 hrs) | Code system lookup |
| `$patient-everything` | PDex, Blue Button | ❌ Missing | High (40-60 hrs) | Complex - retrieve all patient data |
| `$member-match` | Da Vinci HRex | ❌ Missing | High (30-40 hrs) | Payer interoperability |
| `$submit-attachment` | Da Vinci CDex | ❌ Missing | High (40-50 hrs) | Prior auth attachments |
| `$questionnaire-package` | Da Vinci DTR | ❌ Missing | High (35-45 hrs) | Prior auth questionnaires |
| `$document` | Clinical Documents | ❌ Missing | Medium (25-35 hrs) | Generate FHIR documents |
| `$match` | Patient Matching | ❌ Missing | High (30-40 hrs) | MPI/record linkage |

**Total Estimated Effort for High Priority**: 270-390 hours (6-9 months at 10 hrs/week)

### Medium Priority Gaps (Enhanced Functionality)

| Operation | Use Case | Effort | Notes |
|-----------|----------|--------|-------|
| `$translate` | Code system mapping | Medium | ConceptMap support needed |
| `$subsumes` | Terminology hierarchy | Medium | Code system relationships |
| `$populate` | Form pre-fill | Medium | Questionnaire automation |
| `$lastn` | Recent observations | Low | Useful for vitals/labs |
| `$meta` | Metadata management | Low | Tag/profile queries |
| `$graphql` | Graph queries | High | Complex alternative API |
| `$encounter-everything` | Encounter data export | Medium | Similar to $patient-everything |

### Low Priority Gaps (Specialized Use Cases)

| Operation | Use Case | Notes |
|-----------|----------|-------|
| `$merge` | Patient record merging | Complex MPI functionality |
| `$stats` | Observation statistics | Analytics use case |
| `$evaluate-measure` | Quality measures | CQM reporting |
| `$care-gaps` | Quality gaps | Population health |
| `$apply` | Clinical decision support | Knowledge artifacts |
| `$submit` | Claims submission | Administrative workflow |

---

## Implementation Recommendations

### Phase 1: Terminology Services (Priority 1) - 50-80 hours

**Operations**:
1. `$expand` - Expand ValueSet to enumerated codes
2. `$validate-code` - Validate code against ValueSet/CodeSystem
3. `$lookup` - Retrieve concept display and properties

**Dependencies**:
- Terminology package loading system (✅ exists: `IPackageResourceRepository`)
- ValueSet/CodeSystem storage (✅ exists: `PackageResource` table)
- FHIR terminology SDK (✅ exists: Firely SDK 6.0)

**Implementation Pattern**:
```csharp
// File: src/Ignixa.Api/Endpoints/TerminologyEndpoints.cs
public static class TerminologyEndpoints {
    public static void MapTerminologyEndpoints(this IEndpointRouteBuilder endpoints) {
        // GET [base]/ValueSet/$expand?url={canonical}
        endpoints.MapGet("/ValueSet/$expand", HandleExpandAsync);

        // GET [base]/ValueSet/$validate-code?url={canonical}&code={code}&system={system}
        endpoints.MapGet("/ValueSet/$validate-code", HandleValidateCodeAsync);

        // GET [base]/CodeSystem/$lookup?system={system}&code={code}
        endpoints.MapGet("/CodeSystem/$lookup", HandleLookupAsync);
    }
}

// File: src/Ignixa.Application/Features/Terminology/TerminologyFeature.cs
public class TerminologyFeature : IPackageFeature {
    public string PackageId => "hl7.terminology";
    public IReadOnlyList<string> SystemOperations => new[] { "expand", "validate-code", "lookup" };
}
```

**References**:
- Existing: `ADR-2531-terminology-services-implementation.md`
- Existing: `InMemoryTerminologyService` (validation layer)

---

### Phase 2: Document Operations (Priority 1) - 40-60 hours

**Operations**:
1. `$docref` - Retrieve patient documents
2. `$document` - Generate FHIR document bundle

**Implementation Pattern**:
```csharp
// File: src/Ignixa.Api/Endpoints/DocumentEndpoints.cs
public static class DocumentEndpoints {
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder endpoints) {
        // GET [base]/DocumentReference/$docref?patient={id}&start={date}&end={date}
        endpoints.MapGet("/DocumentReference/$docref", HandleDocRefAsync);

        // GET [base]/Composition/{id}/$document
        endpoints.MapGet("/Composition/{id}/$document", HandleDocumentAsync);
    }
}
```

**Dependencies**:
- DocumentReference search implementation (✅ exists)
- Composition resource support (✅ exists)
- Bundle generation utilities (✅ exists: `BundleJsonNode`)

---

### Phase 3: Patient Data Access (Priority 1) - 60-80 hours

**Operations**:
1. `$patient-everything` - Return all patient data
2. `$group-everything` - Bulk patient data for cohorts

**Implementation Pattern**:
```csharp
// File: src/Ignixa.Application/Features/PatientEverything/PatientEverythingQuery.cs
public record PatientEverythingQuery(
    string PatientId,
    DateTimeOffset? Since,
    string[]? ResourceTypes,
    int? Count
) : IRequest<BundleJsonNode>;

// Handler uses search infrastructure to:
// 1. Get all references from Patient resource
// 2. Search for all resources with patient={id}
// 3. Recursively resolve _include references
// 4. Return searchset Bundle
```

**Dependencies**:
- Compartment definitions (✅ exists: search parameter system)
- Reverse reference search (✅ exists: `_has` support)
- Graph traversal for `_include` (✅ exists: search implementation)

**Complexity**:
- Performance: Potential for large result sets
- Memory: Streaming bundle assembly
- Pagination: Bundle continuation links

**References**:
- [FHIR Patient-everything Operation](https://hl7.org/fhir/patient-operation-everything.html)
- Existing: `ADR-2502-compartment-wildcard-search.md`

---

### Phase 4: Da Vinci Operations (Priority 2) - 100-140 hours

**Operations**:
1. `$member-match` - Patient identity matching across payers
2. `$submit-attachment` - Clinical data submission for claims
3. `$questionnaire-package` - Prior auth questionnaire retrieval

**Implementation Notes**:

#### $member-match
- **Complexity**: High - requires MPI/matching algorithm
- **Pattern**: Similar to `$match` but payer-specific
- **Input**: Patient demographics + Coverage
- **Output**: Unique member identifier

#### $submit-attachment
- **Complexity**: High - requires blob storage integration
- **Pattern**: Async operation with DurableTask
- **Input**: Attachment bundle (clinical docs, forms)
- **Output**: HTTP 200 with tracking ID

#### $questionnaire-package
- **Complexity**: Medium - requires package management
- **Pattern**: Query PackageResource table for Questionnaire + dependencies
- **Input**: Coverage + canonical URLs
- **Output**: Bundle with Questionnaire, Library, ValueSet

**Dependencies**:
- Package loading system (✅ exists)
- Blob storage (✅ exists: `IBlobStorageClient`)
- DurableTask framework (✅ exists: used for $export)

---

### Phase 5: Enhanced Operations (Priority 3) - 80-120 hours

**Operations**:
1. `$translate` - Code translation via ConceptMap
2. `$subsumes` - Code subsumption testing
3. `$populate` - Questionnaire pre-population
4. `$encounter-everything` - All encounter data
5. `$lastn` - Last N observations

**Notes**: Lower priority - implement based on customer demand

---

## Architecture Recommendations

### 1. Operation Registration Pattern (Existing ✅)

Continue using `IPackageFeature` interface for operation declarations:

```csharp
public class TerminologyServicesFeature : IPackageFeature {
    public string PackageId => "hl7.terminology";

    public IReadOnlyList<string> SystemOperations => new[] {
        "expand", "validate-code", "lookup", "translate", "subsumes"
    };

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ResourceOperations =>
        new Dictionary<string, IReadOnlyList<string>> {
            { "ValueSet", new[] { "expand", "validate-code" } },
            { "CodeSystem", new[] { "lookup", "validate-code", "subsumes" } },
            { "ConceptMap", new[] { "translate", "closure" } }
        };
}
```

### 2. Endpoint Pattern (Existing ✅)

Follow `OperationEndpoints.cs` pattern:
- Minimal API registration
- Medino handler dispatch
- Multi-tenant routing support
- Parameters resource parsing

### 3. Long-Running Operations Pattern (Existing ✅)

Use DurableTask framework (like `$export`) for operations requiring async processing:
- `$submit-attachment` - Process large attachments
- `$group-everything` - Bulk data retrieval
- Future: `$import` (see ADR-2526)

### 4. Capability Statement Integration (Existing ✅)

Operations automatically appear in CapabilityStatement via `OperationsSegment`:
- Queries `IPackageFeature` implementations
- Loads OperationDefinitions from `PackageResource` table
- Generates capability.rest.operation entries

---

## SMART App Launch Note

SMART App Launch defines **OAuth 2.0 endpoints**, not FHIR operations:
- `/authorize` - OAuth authorization endpoint
- `/token` - OAuth token endpoint
- `/.well-known/smart-configuration` - Discovery document

These are **NOT** FHIR operations with `$` prefix and are implemented separately via:
- ASP.NET Core authentication middleware
- IdentityServer or similar OAuth provider
- SMART-specific scopes (`patient/*.read`, `launch/patient`, etc.)

**References**:
- [SMART App Launch v2.2.0](https://build.fhir.org/ig/HL7/smart-app-launch/)
- Existing: `docs/investigations/smart-on-fhir-v2-implementation.md`

---

## Standards Compliance Matrix

| Implementation Guide | Version | Required Operations | Optional Operations | Current Support | Gap |
|---------------------|---------|---------------------|-----------------------|-----------------|-----|
| **US Core** | STU 6.1/7.0 | $docref | $expand | 0/1 | ❌ $docref |
| **IPA** | STU 1.0 | $docref | - | 0/1 | ❌ $docref |
| **Da Vinci HRex** | STU 1.0 | $member-match | - | 0/1 | ❌ $member-match |
| **Da Vinci PDex** | STU 2.0 | $patient-everything | $group-everything | 0/2 | ❌ Both |
| **Da Vinci CDex** | STU 2.1 | $submit-attachment | - | 0/1 | ❌ $submit-attachment |
| **Da Vinci DTR** | STU 2.1 | $questionnaire-package | - | 0/1 | ❌ $questionnaire-package |
| **CARIN BB** | STU 2.1 | None | - | N/A | ✅ None |
| **mCode** | STU 4.0 | None | - | N/A | ✅ None |
| **Bulk Data (export)** | STU 2.0 | $export | - | 1/1 | ✅ Complete |
| **Bulk Data (import)** | STU 2.0 | $import | - | 0/1 | ❌ $import (ADR-2526) |

**Compliance Score**: 2/10 high-priority operations implemented (20%)

---

## Cost-Benefit Analysis

### High ROI Operations (Implement First)

| Operation | ROI | Reason |
|-----------|-----|--------|
| `$docref` | **Very High** | Required by US Core and IPA - blocks certification |
| `$expand` | **Very High** | Essential for clinical decision support, terminology validation |
| `$validate-code` | **High** | Enables robust validation, required for many workflows |
| `$patient-everything` | **High** | Blue Button compliance, payer data exchange |

### Medium ROI Operations

| Operation | ROI | Reason |
|-----------|-----|--------|
| `$lookup` | Medium | Useful for terminology display, not always required |
| `$translate` | Medium | Code mapping - needed for multi-system environments |
| `$populate` | Medium | Improves UX for forms, not universally required |
| `$document` | Medium | Clinical document generation - niche use case |

### Specialized Operations (Customer-Driven)

| Operation | ROI | Reason |
|-----------|-----|--------|
| `$member-match` | Low-Medium | Only needed for payer-to-payer exchange |
| `$submit-attachment` | Low-Medium | Prior auth workflows only |
| `$questionnaire-package` | Low-Medium | Da Vinci DTR adopters only |
| `$match` | Low | MPI functionality - complex, specialized |

---

## Next Steps

### Immediate Actions (Next 2 Sprints)

1. **Create Epic**: "Terminology Operations Support"
   - Story: Implement `$expand`
   - Story: Implement `$validate-code`
   - Story: Implement `$lookup`
   - ADR: Update `ADR-2531-terminology-services-implementation.md`

2. **Create Epic**: "Document Operations Support"
   - Story: Implement `$docref`
   - Story: Implement `$document`
   - Tests: Integration tests with US Core test suite

3. **Prioritize**: `$patient-everything` for Q1 2026
   - Prototype: Performance testing with large patient datasets
   - Design: Pagination and streaming strategy
   - ADR: Create `ADR-XXXX-patient-everything-implementation.md`

### Long-Term Roadmap (6-12 Months)

1. **Q1 2026**: Terminology + Document operations (Phase 1-2)
2. **Q2 2026**: Patient data access operations (Phase 3)
3. **Q3 2026**: Da Vinci operations (Phase 4) - customer-driven
4. **Q4 2026**: Enhanced operations (Phase 5) - as needed

### Certification Targets

- **ONC (g)(10) Standardized API**: Requires US Core compliance
  - **Blocker**: `$docref` operation missing
  - **Target**: Q1 2026

- **CARIN Alliance Certification**: Blue Button compliance
  - **Requirement**: `$patient-everything` or equivalent search
  - **Target**: Q2 2026

- **Da Vinci Implementer**: Payer interoperability
  - **Requirement**: `$member-match`, `$patient-everything`, attachment operations
  - **Target**: Q3 2026 (customer-driven)

---

## References

### Implementation Guides Analyzed

1. **US Core** (STU 6.1/7.0): https://hl7.org/fhir/us/core/
2. **IPA** (STU 1.0): https://hl7.org/fhir/uv/ipa/
3. **Da Vinci HRex** (STU 1.0): https://hl7.org/fhir/us/davinci-hrex/
4. **Da Vinci PDex** (STU 2.0): https://hl7.org/fhir/us/davinci-pdex/
5. **Da Vinci CDex** (STU 2.1): https://build.fhir.org/ig/HL7/davinci-ecdx/
6. **Da Vinci DTR** (STU 2.1): https://hl7.org/fhir/us/davinci-dtr/
7. **CARIN Blue Button** (STU 2.1): https://hl7.org/fhir/us/carin-bb/
8. **mCode** (STU 4.0): https://build.fhir.org/ig/HL7/fhir-mCODE-ig/

### FHIR Core Specifications

- **Operations Framework**: https://hl7.org/fhir/operations.html
- **Operations List**: https://hl7.org/fhir/operationslist.html
- **Terminology Services**: https://hl7.org/fhir/terminology-service.html
- **Patient-everything**: https://hl7.org/fhir/patient-operation-everything.html

### Existing ADRs

- `ADR-2526-bulk-import-operation.md` - Import operation design
- `ADR-2531-terminology-services-implementation.md` - Terminology architecture
- `docs/investigations/smart-on-fhir-v2-implementation.md` - SMART App Launch

### External Resources

- **Terminology Services**: https://docs.fire.ly/projects/Firely-Server/en/latest/features_and_tools/terminology.html
- **HAPI FHIR Operations**: https://hapifhir.io/hapi-fhir/docs/server_plain/rest_operations_operations.html
- **Azure FHIR Patient-everything**: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/patient-everything

---

## Appendix A: Operation Definitions Summary

### Format

Each operation is defined with:
- **Canonical URL**: `http://hl7.org/fhir/OperationDefinition/[Resource]-[name]`
- **Context**: System (`[base]/$op`), Type (`[base]/Patient/$op`), Instance (`[base]/Patient/123/$op`)
- **Parameters**: Input and output parameters (primitives, resources, complex types)
- **Binding**: Required/optional parameters

### Example: $docref

```
Canonical: http://hl7.org/fhir/OperationDefinition/DocumentReference-docref
Context: Type-level (DocumentReference)
Method: GET
Parameters:
  - patient (required): Reference to Patient
  - start (optional): Start date
  - end (optional): End date
  - type (optional): Document type filter
Output: Bundle (searchset) containing DocumentReference resources
```

### Example: $expand

```
Canonical: http://hl7.org/fhir/OperationDefinition/ValueSet-expand
Context: Instance-level (ValueSet) or Type-level
Method: GET or POST
Parameters:
  - url (conditional): ValueSet canonical URL
  - valueSet (conditional): ValueSet resource
  - filter (optional): Text filter
  - count (optional): Max codes to return
  - offset (optional): Paging offset
Output: ValueSet with expansion.contains[] populated
```

---

## Appendix B: Ignixa Codebase References

### Current Operation Implementations

| File | Lines | Purpose |
|------|-------|---------|
| `src/Ignixa.Api/Endpoints/OperationEndpoints.cs` | 1-316 | $validate endpoints |
| `src/Ignixa.Api/Endpoints/ExportEndpoints.cs` | 1-254 | $export endpoints |
| `src/Ignixa.Application/Operations/Features/Validate/ValidateResourceHandler.cs` | N/A | $validate logic |
| `src/Ignixa.Application/BackgroundOperations/Export/ExportOrchestration.cs` | N/A | $export DurableTask |

### Operation Infrastructure

| File | Lines | Purpose |
|------|-------|---------|
| `src/Ignixa.Domain/Abstractions/IPackageFeature.cs` | 1-37 | Operation registration interface |
| `src/Ignixa.Application/Features/Metadata/Segments/OperationsSegment.cs` | 1-216 | CapabilityStatement generation |
| `src/Ignixa.Application/Features/Export/BulkDataExportFeature.cs` | 1-32 | $export feature declaration |

### Relevant ADRs

| Document | Relevance |
|----------|-----------|
| `ADR-2526-bulk-import-operation.md` | $import operation design (not yet implemented) |
| `ADR-2531-terminology-services-implementation.md` | Terminology architecture (needed for $expand, $validate-code, $lookup) |
| `ADR-2532-unified-validation-terminology-package-architecture.md` | Package loading (supports OperationDefinition resources) |

---

**Document Status**: Investigation Complete
**Next Review**: After Phase 1 implementation (Terminology Operations)
**Owner**: Development Team
**Stakeholders**: Product Management, Solutions Architecture
