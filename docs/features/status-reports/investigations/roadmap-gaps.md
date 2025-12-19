# Investigation: Roadmap Gap Analysis

**Feature**: status-reports
**Status**: Complete
**Created**: 2025-01-08

## Executive Summary

Analysis of 118 E2E/Integration test files in `src-old/test/` reveals **~35% test coverage gap** in the current ADR-2510 implementation roadmap. Found **27 test files** representing **15+ significant feature gaps** across specialized operations, advanced search, and infrastructure concerns.

**Impact**: Original 72-week roadmap needs **+16 weeks** (4 new phases) to achieve 100% feature parity with legacy system.

**Updated Timeline**: 88 weeks (22 months) for complete v2 implementation.

---

## Gap Analysis Summary

| Category | Missing Features | Priority | Effort (weeks) | Suggested Phase |
|----------|------------------|----------|----------------|-----------------|
| Bulk Operations | $bulk-delete, $bulk-update | **HIGH** | 6 | Phase 16 (NEW) |
| Clinical Operations | $docref, $everything, $member-match | **MEDIUM-HIGH** | 4 | Phase 17 (NEW) |
| Compartment Search | Patient/Device compartments | **MEDIUM** | 3 | Phase 19 (NEW) |
| Advanced Search | _not-referenced, $includes, edge cases | **MEDIUM** | 2 | Phase 8 (expand) |
| Data Conversion | $convert-data (HL7v2, CDA) | **LOW-MEDIUM** | 3 | Phase 18 (NEW) |
| Infrastructure | Proxy support, CORS, custom headers | **MEDIUM** | 1 | Phase 15 (expand) |

**Total Additional Effort**: ~19 weeks across new and expanded phases

---

## CRITICAL GAPS (Must Fix)

### 1. $bulk-delete Operation (HIGH PRIORITY)

**Test File**: `BulkDeleteTests.cs` (1,125 lines)
**Current Roadmap Coverage**: ❌ Not mentioned in any phase
**Production Impact**: Critical for data management, compliance (GDPR/HIPAA), and system maintenance

**Features Required:**
- **Soft delete** - Mark resources as deleted (`meta.tag` with deletion marker)
- **Hard delete** - Permanently remove resources
- **Purge history** - Delete all versions of resources
- **Reference handling** - `_remove-references` parameter to clean up dangling references
- **Include/revinclude support** - Bulk delete resources AND their includes
- **Excluded types** - `excludedResourceTypes` parameter (e.g., don't delete SearchParameter)
- **Pagination** - Handle >1 page of search results
- **Search parameter updates** - Reindex after bulk delete
- **Audit trail** - Track bulk deletions for compliance

**E2E Test Scenarios** (from BulkDeleteTests.cs):
```
✅ Soft delete with search parameters
✅ Hard delete with confirmation parameter
✅ Bulk delete with _include
✅ Bulk delete with _revinclude
✅ Delete >1000 resources with pagination
✅ Exclude SearchParameter from bulk delete
✅ Remove references when deleting (cascade)
✅ Verify search indices updated after delete
✅ Audit log contains bulk delete operations
```

**Suggested Implementation:**
```csharp
POST /Patient/$bulk-delete
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "_type", "valueString": "Patient" },
    { "name": "_filter", "valueString": "deceased eq true" },
    { "name": "mode", "valueCode": "soft-delete" },
    { "name": "_remove-references", "valueBoolean": true },
    { "name": "_count", "valueInteger": 1000 }
  ]
}
```

**Recommended Phase**: NEW Phase 16 "Bulk Operations Part 2" (after Phase 14)

---

### 2. $bulk-update Operation (HIGH PRIORITY)

**Test File**: `BulkUpdateTests.cs` (910 lines)
**Current Roadmap Coverage**: ❌ Not mentioned in any phase
**Production Impact**: Critical for data migration, schema changes, mass corrections

**Features Required:**
- **FHIRPath patch** - Apply patch to matching resources
- **JSON Patch** - Apply RFC 6902 patches in bulk
- **Include/revinclude support** - Update resources AND their includes
- **Parallel execution** - `_isParallel=true` for concurrent updates
- **Sequential execution** - `_isParallel=false` for ordered updates
- **Pagination** - Handle >1 page of search results
- **Excluded types** - Don't update StructureDefinition, SearchParameter, etc.
- **Failure handling** - Continue on error vs stop at first error
- **Version management** - Optimistic concurrency control

**E2E Test Scenarios** (from BulkUpdateTests.cs):
```
✅ Bulk update with FHIRPath patch
✅ Bulk update with JSON Patch
✅ Bulk update with _include
✅ Bulk update with _revinclude
✅ Parallel execution (_isParallel=true)
✅ Sequential execution (_isParallel=false)
✅ Update >1000 resources with pagination
✅ Exclude conformance resources
✅ Handle patch failures gracefully
✅ Verify search indices updated after update
```

**Suggested Implementation:**
```csharp
POST /Patient/$bulk-update
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "_type", "valueString": "Patient" },
    { "name": "_filter", "valueString": "address-state eq 'CA'" },
    { "name": "patch", "resource": {
        "resourceType": "Parameters",
        "parameter": [
          { "name": "operation", "part": [
              { "name": "type", "valueCode": "replace" },
              { "name": "path", "valueString": "address[0].state" },
              { "name": "value", "valueString": "California" }
            ]
          }
        ]
      }
    },
    { "name": "_isParallel", "valueBoolean": true },
    { "name": "_count", "valueInteger": 1000 }
  ]
}
```

**Recommended Phase**: NEW Phase 16 "Bulk Operations Part 2" (after Phase 14)

---

## HIGH PRIORITY GAPS

### 3. $docref Operation (US Core Requirement)

**Test File**: `Conformance/DocRefOperationTests.cs` (820 lines)
**Current Roadmap Coverage**: ❌ Not mentioned
**Production Impact**: **Required for US Core conformance** - clinical systems need document references

**Features Required:**
- **Patient-level operation**: `GET /Patient/[id]/$docref`
- **System-level operation**: `GET /$docref?patient=[id]`
- **Parameters**:
  - `patient` - Patient reference (required)
  - `start` - Start date for document period
  - `end` - End date for document period
  - `type` - Document type (CodeableConcept)
- **Unsupported warnings** - Return OperationOutcome warnings for `on-demand`, `profile` parameters
- **Bundle response** - Return searchset Bundle with DocumentReference resources

**E2E Test Scenarios**:
```
✅ $docref with patient parameter
✅ $docref with date range (start/end)
✅ $docref with type filter
✅ $docref returns searchset Bundle
✅ $docref returns unsupported parameter warnings
✅ $docref with GET query parameters
✅ $docref with POST Parameters resource
```

**Recommended Phase**: NEW Phase 17 "Clinical Operations" (~4 weeks)

---

### 4. Compartment Search (FHIR Spec Compliance)

**Test File**: `CompartmentTests.cs` (210 lines)
**Current Roadmap Coverage**: ❌ Not mentioned
**Production Impact**: FHIR spec compliance, common clinical workflow pattern

**Features Required:**
- **Compartment URLs**: `GET /Patient/[id]/Observation`
- **Wildcard**: `GET /Patient/[id]/*`
- **Type filter**: `GET /Patient/[id]/*?_type=Observation`
- **Device compartment**: `GET /Device/[id]/*`
- **Search within compartment**: `GET /Patient/[id]/Observation?code=http://loinc.org|1234-5`
- **Include/revinclude**: Compartment + _include combinations

**E2E Test Scenarios**:
```
✅ Patient compartment search
✅ Device compartment search
✅ Wildcard compartment (all types)
✅ Type filter in compartment
✅ Search parameters within compartment
✅ _include within compartment
✅ _revinclude within compartment
```

**Recommended Phase**: NEW Phase 19 "Compartment Search" (~3 weeks)

---

## MEDIUM PRIORITY GAPS

### 5. _not-referenced Search Parameter

**Test File**: `Search/NotReferencedSearchTests.cs`
**Current Roadmap Coverage**: ❌ Not in Phase 8 (Advanced Search)
**Use Case**: Find orphaned resources with no inbound references (cleanup/maintenance)

**Example**: `GET /Observation?_not-referenced=*:*`
Returns Observations that are NOT referenced by any other resource

**Recommended Phase**: Phase 8 (Advanced Search) - add to existing phase

---

### 6. $includes Operation (Paginated Includes)

**Test File**: `Search/IncludesOperationTests.cs` (314 lines)
**Current Roadmap Coverage**: ❌ Not in Phase 8
**Use Case**: Handle large _include/_revinclude result sets with separate pagination

**Features**:
- `_includesCount` - Separate limit for included resources
- `_includesContinuationToken` - Paginate includes independently
- "related" link - Next page URL for additional includes

**Example**:
```
GET /Patient?_include=Patient:organization&_count=10&_includesCount=50
```
Returns 10 patients + up to 50 organizations, with "related" link if more organizations exist

**Recommended Phase**: Phase 8 (Advanced Search) - add to existing phase

---

### 7. $everything Operation

**Test File**: `EverythingOperationTests.cs`
**Current Roadmap Coverage**: ⚠️ Mentioned in Phase 10+ but not detailed
**Use Case**: Retrieve patient compartment + all related resources

**Features**:
- `GET /Patient/[id]/$everything`
- `GET /$everything` (system-level)
- Parameters: `_since`, `_count`, `_type`

**Recommended Phase**: Phase 17 "Clinical Operations"

---

### 8. $member-match Operation

**Test File**: `MemberMatchTests.cs`
**Current Roadmap Coverage**: ⚠️ Mentioned in Phase 10+ but not detailed
**Use Case**: Coverage matching for payer-to-payer data exchange (Da Vinci PDex IG)

**Recommended Phase**: Phase 17 "Clinical Operations"

---

### 9. Proxy/Reverse Proxy Support

**Test File**: `Search/SearchProxyTests.cs`
**Current Roadmap Coverage**: ❌ Not in Phase 15 (Production Readiness)
**Production Impact**: Required for production deployment behind load balancers

**Features**:
- `X-Forwarded-Host` header handling
- `X-Forwarded-Prefix` header handling
- URL rewriting in Bundle pagination links
- Correct `self` and `next` links in search bundles

**Recommended Phase**: Phase 15 (Production Readiness) - add to existing phase

---

## LOW-MEDIUM PRIORITY GAPS

### 10. $convert-data Operation

**Test Files**: `ConvertDataTests.cs`, `CustomConvertDataTests.cs`
**Current Roadmap Coverage**: ⚠️ Mentioned in Phase 10+ but not detailed
**Use Case**: Legacy system integration (HL7 v2 → FHIR, CDA → FHIR)

**Recommended Phase**: NEW Phase 18 "Data Conversion" (~3 weeks) - **OR defer to v2.1**

---

### 11. Edge Case Tests

**Missing from Roadmap**:
- `Search/EscapeCharactersSearchTests.cs` - Special character handling
- `Search/TokenOverflowTests.cs` - Tokens >450 characters
- `Search/IdSearchTests.cs` - ID parameter edge cases

**Recommended Phase**: Add to Phase 2 (Search Foundation) or Phase 8 (Advanced Search)

---

## INFRASTRUCTURE GAPS

### 12. CORS Configuration

**Test File**: `CorsTests.cs`
**Current Roadmap Coverage**: ⚠️ Mentioned in Phase 11+ but needs explicit criteria
**Recommended Phase**: Phase 15 (Production Readiness) - add explicit E2E criteria

---

### 13. Custom Headers

**Test File**: `CustomHeadersTests.cs`
**Recommended Phase**: Phase 15 (Production Readiness)

---

### 14. Metrics & Telemetry

**Test File**: `Metric/MetricTests.cs`
**Current Roadmap Coverage**: ✅ Mentioned in Phase 15 but needs explicit E2E criteria
**Recommended Phase**: Phase 15 (Production Readiness) - add explicit test requirements

---

## PATCH OPERATIONS (Coverage Needs Detail)

**Test Files**:
- `JsonPatchTests.cs` - JSON Patch (RFC 6902)
- `FhirPathPatchTests.cs` - FHIRPath Patch
- `ConditionalPatchTests.cs` - Conditional patches

**Current Roadmap Coverage**: ⚠️ Phase 6 mentions patch but lacks E2E test criteria
**Recommended Action**: Expand Phase 6 with explicit acceptance criteria from these test files

---

## VALIDATION (Coverage Exists, Needs Enhancement)

**Test File**: `ValidateTests.cs`
**Current Roadmap Coverage**: ✅ Phase 7 mentions $validate
**Enhancement Needed**: See **ADR-2513: Two-Tier Validation Architecture**

**Key Points**:
- Legacy uses old Firely validation library (slow, >1s per resource)
- v2 needs **two-tier validation**:
  - **Tier 1**: Fast structural validation (<50ms) - always runs
  - **Tier 2**: Profile validation (<5s) - opt-in via $validate or header
- Modern Firely SDK 6.0 has improved validation performance

**Recommended Phase**:
- Phase 2: Implement Tier 1 (fast structural)
- Phase 7: Implement Tier 2 ($validate operation)

---

## RECOMMENDED ROADMAP UPDATES

### NEW PHASES TO ADD:

**Phase 16: Bulk Operations Part 2** (6 weeks) - **HIGH PRIORITY**
- $bulk-delete operation (soft, hard, purge modes)
- $bulk-update operation (FHIRPath + JSON Patch)
- Include/revinclude support for bulk operations
- Parallel vs sequential execution modes
- Pagination for large result sets
- Audit trail for bulk operations
- **E2E Tests**: BulkDeleteTests.cs, BulkUpdateTests.cs (ALL tests must pass)
- **Effort**: 6 weeks (~48 Claude Code hours)

**Phase 17: Clinical Operations** (4 weeks) - **MEDIUM-HIGH PRIORITY**
- $docref operation (US Core requirement)
- $everything operation (patient compartment)
- $member-match operation (payer exchange)
- **E2E Tests**: DocRefOperationTests.cs, EverythingOperationTests.cs, MemberMatchTests.cs
- **Effort**: 4 weeks (~32 Claude Code hours)

**Phase 18: Data Conversion** (3 weeks) - **LOW-MEDIUM PRIORITY**
- $convert-data operation
- HL7 v2 → FHIR conversion
- CDA → FHIR conversion
- Custom template support
- **E2E Tests**: ConvertDataTests.cs, CustomConvertDataTests.cs
- **Effort**: 3 weeks (~24 Claude Code hours)
- **Note**: Consider deferring to v2.1 if not critical

**Phase 19: Compartment Search** (3 weeks) - **MEDIUM PRIORITY**
- Patient compartments
- Device compartments
- Wildcard compartment search
- Search within compartments
- **E2E Tests**: CompartmentTests.cs (ALL tests must pass)
- **Effort**: 3 weeks (~24 Claude Code hours)

---

### ENHANCEMENTS TO EXISTING PHASES:

**Phase 2: Search Foundation** (+1 week)
- Add: `_id` parameter edge cases (IdSearchTests.cs)
- Add: Escape character handling (EscapeCharactersSearchTests.cs)
- Add: Tier 1 fast structural validation (<50ms)

**Phase 6: Patch Operations** (+1 week)
- Add explicit E2E criteria from JsonPatchTests.cs
- Add explicit E2E criteria from FhirPathPatchTests.cs
- Add explicit E2E criteria from ConditionalPatchTests.cs
- Document all patch test scenarios

**Phase 7: Validation** (+2 weeks)
- Implement Tier 2 profile validation ($validate operation)
- Use modern Firely SDK 6.0
- Profile caching for performance
- Pass ALL ValidateTests.cs scenarios

**Phase 8: Advanced Search** (+2 weeks)
- Add: `_not-referenced` parameter (NotReferencedSearchTests.cs)
- Add: `$includes` operation (IncludesOperationTests.cs)
- Add: Token overflow edge cases (TokenOverflowTests.cs)

**Phase 10/11: SQL/Cosmos Storage** (+1 week)
- Add: Token overflow handling (>450 char identifiers)
- Add: Large data optimizations

**Phase 15: Production Readiness** (+1 week)
- Add: Proxy/reverse proxy support (SearchProxyTests.cs)
- Add: CORS configuration (CorsTests.cs)
- Add: Custom header handling (CustomHeadersTests.cs)
- Add: Explicit E2E criteria for MetricTests.cs

---

## UPDATED TIMELINE

| Component | Original (weeks) | Additional (weeks) | New Total (weeks) |
|-----------|------------------|-------------------|-------------------|
| Phases 1-15 | 72 | +8 (enhancements) | 80 |
| Phase 16 (Bulk Ops) | 0 | +6 | 6 |
| Phase 17 (Clinical Ops) | 0 | +4 | 4 |
| Phase 18 (Conversion) | 0 | +3 | 3 |
| Phase 19 (Compartments) | 0 | +3 | 3 |
| **TOTAL** | **72 weeks** | **+24 weeks** | **96 weeks** |

**Updated Timeline**: **96 weeks (24 months / 2 years)** for 100% feature parity

**Alternative (Defer Phase 18)**: **93 weeks (23 months)** if $convert-data deferred to v2.1

---

## PRIORITY ASSESSMENT

### MUST HAVE (Critical for v2.0):
1. ✅ $bulk-delete - Production data management
2. ✅ $bulk-update - Data migration scenarios
3. ✅ $docref - US Core conformance
4. ✅ Compartment search - FHIR spec compliance
5. ✅ Two-tier validation - Performance requirement

### SHOULD HAVE (Important for v2.0):
6. ✅ $everything - Common clinical workflow
7. ✅ $includes operation - Large include optimization
8. ✅ Proxy support - Production deployment
9. ✅ _not-referenced - Maintenance operations

### COULD HAVE (Nice to have for v2.0):
10. ⚠️ $member-match - Payer-specific, defer if needed
11. ⚠️ Edge case tests - Handle gracefully, not blockers
12. ⚠️ CORS/Custom headers - Required but low complexity

### WON'T HAVE (Defer to v2.1):
13. ❌ $convert-data - Legacy-only, low priority
14. ❌ Token overflow - Rare edge case

---

## DEFINITION OF DONE UPDATE

**Original**: FHIR Server v2 is complete when all E2E tests pass
**Updated**: FHIR Server v2 is complete when:

1. **100% of E2E tests pass** (118 test files in src-old/test)
2. **All 19 phases complete** (15 original + 4 new)
3. **Performance targets met**:
   - <10ms CRUD (in-memory)
   - <50ms search (in-memory)
   - <50ms Tier 1 validation
   - <5s Tier 2 validation
4. **80%+ test coverage** maintained throughout
5. **F5 experience** preserved (zero-setup local development)

---

## IMPACT ANALYSIS

### Test Coverage Gap:
- **Total E2E test files**: 118
- **Roadmap references**: ~50 files
- **Missing references**: ~68 files (58% of tests)
- **After updates**: 118 files (100% coverage)

### Timeline Impact:
- **Original**: 72 weeks (18 months)
- **Updated**: 96 weeks (24 months)
- **Increase**: 24 weeks (6 months / +33%)

### Scope Impact:
- **NEW operations**: 5 ($bulk-delete, $bulk-update, $docref, $member-match, $convert-data)
- **NEW search features**: 2 (_not-referenced, $includes)
- **NEW infrastructure**: Compartments, proxy support, validation tiers

---

## RECOMMENDATIONS

1. **Immediate**: Update ADR-2510 with 4 new phases (16-19)
2. **Phase Planning**: Add explicit E2E test criteria to Phases 2, 6, 7, 8, 15
3. **Prioritization**: Implement Phases 16-17 before Phase 18 (defer $convert-data if needed)
4. **Testing**: Reference ALL 118 test files in updated "Definition of Done"
5. **Communication**: Update timeline expectations (24 months realistic for 100% parity)

---

## CONCLUSION

The roadmap is **solid for core FHIR operations** but missing **critical production features** ($bulk-delete, $bulk-update) and **US Core requirements** ($docref, compartments).

**Updated 96-week timeline** achieves true feature parity. Alternative 93-week timeline possible if $convert-data deferred to v2.1.

**Key Insight**: Legacy has 118 E2E tests for a reason - these features are used in production. v2 must implement all of them to be a true replacement.
