# ADR 2530: FHIR Compatibility Improvements - Phased Remediation Plan

## Metadata

- **ADR Number**: 2530
- **Title**: FHIR Compatibility Improvements - Phased Remediation Plan
- **Status**: 📋 **PROPOSED** (2025-10-28)
- **Date**: 2025-10-28
- **Phase**: 24+ (Weeks 107-118+) - Compatibility Improvements
- **Implementation Priority**: CRITICAL
- **Current Pass Rate**: 22.2% (199/898 tests)
- **Target Pass Rate**: 65%+ (580+/898 tests)
- **Estimated Total Effort**: 36-48 hours (4.5-6 weeks)
- **Related Documents**:
  - [ADR-2500: Master Implementation Roadmap](../adr/adr-2500-master-implementation-roadmap.md)
  - [ADR-2525: Conditional Operations](../adr/adr-2525-conditional-operations.md)
  - [ADR-2523: Multi-Tenancy Architecture](../adr/adr-2523-phase20-multi-tenancy-data-partitioning.md)
  - [FHIR R4 Spec: OperationOutcome](http://hl7.org/fhir/R4/operationoutcome.html)
  - [FHIR R4 Spec: Bundle](http://hl7.org/fhir/R4/bundle.html)
  - [FHIR R4 Spec: Search](http://hl7.org/fhir/R4/search.html)

---

## Executive Summary

Current compatibility test results show **199/898 tests passing (22.2%)**, with **684 failing tests**. Analysis of `compatibility-report.json` and source code in `old-src/` reveals **10 distinct failure patterns** that can be grouped by impact and fixed systematically in 3 phases over 4-6 weeks.

### Key Findings

| Category | Tests Affected | Root Cause | Priority |
|----------|---|---|---|
| **OperationOutcome Error Format** | ~200 | Server returns `{"issues":[]}` instead of FHIR OperationOutcome | CRITICAL |
| **Bundle JSON Truncation** | ~50 | Search results return incomplete/malformed JSON | CRITICAL |
| **Conditional Operation Validation** | ~20 | Missing 400 error for operations without search criteria | CRITICAL |
| **Composite Search Parameters** | ~100 | Not implemented (code-value-quantity, combo-code-value-*) | HIGH |
| **Bundle Transaction References** | ~5 | Missing `resourceType` in bundle entry responses | HIGH |
| **Timezone Handling** | ~10 | Dates in local timezone instead of UTC | HIGH |
| **System-Level Search** | ~5 | `GET /?param=value` across all resource types | MEDIUM |
| **Proxy Host Headers** | ~2 | Self-links use internal hostname | MEDIUM |
| **Authorization** | ~10 | Access control not implemented | LOW |
| **Edge Cases** | ~2 | Off-by-one errors, count mismatches | LOW |

### Quick Wins (Highest ROI)

**3 Critical Issues = 8 hours → ~270 tests (+35% pass rate)**

1. **Fix OperationOutcome** (2-4h → +200 tests) - 100:1 impact ratio ✅
2. **Fix Bundle JSON** (4-8h → +50 tests) - 12:1 impact ratio ✅
3. **Fix Conditional Validation** (2-3h → +20 tests) - 10:1 impact ratio ✅

---

## Context

### Problem Statement

The Ignixa FHIR Server v2 has completed:
- ✅ Core CRUD operations (Create, Read, Update, Delete)
- ✅ Search functionality with query parameter parsing
- ✅ Transaction bundle support
- ✅ Multi-tenant data partitioning
- ✅ FHIR _history operations

**Current Gap**: The server passes only **22.2%** of compatibility tests, blocking:
- Production deployment readiness
- Client integration testing
- Full FHIR R4/R4B/R5 compliance validation

### Compatibility Test Analysis

**Test Distribution**:
- ✅ Passing: 199/898 (22.2%)
- ❌ Failing: 684/898 (76.2%)
- ⊘ Skipped: 15/898 (1.7%)

### Failure Patterns (Ranked by Impact)

#### Pattern 1: Unknown 'issues' Element (~200 tests)
**Error**: `Encountered unknown element 'issues' at location 'Resource.issues[0]'`

**Root Cause**: Server returns non-standard error format
```json
❌ WRONG: {"issues": [{"message": "..."}]}
✅ RIGHT: {"resourceType": "OperationOutcome", "issue": [{"severity": "error", "code": "invalid", "diagnostics": "..."}]}
```

**Impact**: **CRITICAL** - Breaks all error handling, affects 200+ tests across search, creation, update, deletion

**FHIR Reference**: http://hl7.org/fhir/R4/operationoutcome.html
- `OperationOutcome.issue` is 1..* (required)
- `OperationOutcome.issue.severity`: fatal | error | warning | information (required)
- `OperationOutcome.issue.code`: IssueType value set (required)

---

#### Pattern 2: Invalid JSON - Unexpected End of Content (~50 tests)
**Error**: `Unexpected end of content while loading JObject. Path 'entry'`

**Root Cause**: Bundle responses truncated or malformed (streaming serialization issue)

**Impact**: **CRITICAL** - Core search functionality broken, JSON parsing fails mid-stream

**FHIR Reference**: http://hl7.org/fhir/R4/bundle.html
- Bundle must be valid JSON
- Bundle.entry is 0..* (must be valid array)

**Investigation**: Check `StreamingBundleSerializer.cs` and `FhirJsonWriter.cs`

---

#### Pattern 3: Wrong Exception Type (~20 tests)
**Error**: `Expected: FhirClientException, Actual: HttpRequestException`

**Root Cause**: Conditional operations without search criteria not returning proper 400 error

**Impact**: **CRITICAL** - Conditional operations fail without OperationOutcome

**Example**:
```csharp
❌ WRONG: Throw generic HttpRequestException
✅ RIGHT: Return 400 BadRequest with OperationOutcome:
{
  "resourceType": "OperationOutcome",
  "issue": [{"severity": "error", "code": "invalid", "diagnostics": "Conditional operation requires search criteria"}]
}
```

---

#### Pattern 4: Cannot Determine Root Element Type (~5 tests)
**Error**: `Cannot determine the type of the root element at 'Resource'`

**Root Cause**: Bundle entry.resource missing `resourceType` field

**Impact**: **HIGH** - Transaction bundles with complex references fail

**Fix**: Ensure every entry.resource has `resourceType` set

---

#### Pattern 5: Composite Search Parameters (~100 tests)
**Error**: `InternalServerError: Object reference not set to an instance of an object`

**Root Cause**: Composite search parameters not implemented
- code-value-quantity
- combo-code-value-quantity
- combo-code-value-concept
- code-value-string

**Impact**: **HIGH** - Complex clinical searches don't work (e.g., Observation with code+value)

**FHIR Reference**: http://hl7.org/fhir/R4/search.html#composite
- Composite parameters join two simple parameters with `$`
- Example: `Observation?code-value-quantity=http://loinc.org|8480-6$gt60`

---

#### Pattern 6: Timezone/Date Formatting Issues (~10 tests)
**Error**: `Expected: 2025-10-28T22:47:59.0000000+00:00, Actual: 2025-10-29T05:47:59.0000000+00:00`

**Root Cause**: Dates serialized in local timezone instead of UTC

**Impact**: **HIGH** - Date searches work but with wrong timezone, affecting result filtering

---

#### Pattern 7: System-Level Search Not Supported (~5 tests)
**Error**: `Response code: MethodNotAllowed(405)` for `GET /?_tag=...`

**Root Cause**: Base URL search (cross-resource-type) not implemented

**Impact**: **MEDIUM** - Advanced search scenarios blocked

**FHIR Reference**: http://hl7.org/fhir/R4/http.html#search
- `GET [base]?[parameters]` searches all resource types
- `_type` parameter filters resource types

---

#### Pattern 8: Host Header Issues (~2 tests)
**Error**: `Expected: "e2e.tests.fhir.microsoft.com", Actual: "localhost"`

**Root Cause**: Self-links don't respect X-Forwarded-Host header (proxy deployments)

**Impact**: **MEDIUM** - Production deployments behind proxies have wrong self-links

---

#### Pattern 9: Authentication/Authorization Tests (~10 tests)
**Error**: Expected 403 Forbidden not returned

**Root Cause**: Authorization checks not implemented

**Impact**: **LOW** - Can implement later with auth system design

---

#### Pattern 10: Version/Count Edge Cases (~2 tests)
**Error**: Off-by-one errors in Bundle total or version counting

**Impact**: **LOW** - Minor edge cases

---

## Decision

Implement **FHIR Compatibility Improvements** in **3 phases** to achieve **65%+ pass rate** (580+ tests):

### Phase 1: Critical Fixes (1-2 weeks)
Fix error handling and serialization blocking ~270 tests:
- Fix OperationOutcome error responses
- Fix Bundle JSON serialization
- Fix conditional operation validation
- Fix bundle transaction references

**Expected Impact**: 22% → 52% pass rate (+270 tests)

### Phase 2: High-Value Features (2-3 weeks)
Implement missing FHIR features blocking ~110 tests:
- Implement composite search parameters
- Fix timezone handling

**Expected Impact**: 52% → 64% pass rate (+110 tests)

### Phase 3: Polish (1 week)
Complete remaining features blocking ~7 tests:
- Implement system-level search
- Fix forwarded host headers

**Expected Impact**: 64% → 65% pass rate (+7 tests)

### Deferred (Future)
- Authorization (~10 tests) - requires auth architecture decision
- Edge cases (~2 tests) - low priority

---

## Phase 1: Critical Fixes (1-2 weeks)

### Issue 1.1: Fix OperationOutcome Error Responses (~200 tests)

**Complexity**: Simple
**Estimated Effort**: 2-4 hours
**Impact Ratio**: 100:1 (200 tests / 2 hours = 100 tests/hour)

#### Root Cause Analysis
Server currently returns invalid error format with `issues` element instead of FHIR-compliant `OperationOutcome` resource.

#### Solution

**Files to Update**:
1. `src/Ignixa.Api/Infrastructure/FhirExceptionMiddleware.cs` (or equivalent)
2. All `*Handler.cs` files that throw exceptions

#### Implementation Strategy

**Step 1**: Create standardized OperationOutcome generation
```csharp
public class OperationOutcomeBuilder
{
    public static OperationOutcomeJsonNode CreateErrorOutcome(
        string message,
        string code,
        string? location = null)
    {
        var outcome = new OperationOutcomeJsonNode();
        outcome.AddIssue(
            severity: "error",
            code: code,
            diagnostics: message,
            location: location != null ? new[] { location } : null);
        return outcome;
    }
}
```

**Step 2**: Update exception middleware to use OperationOutcome
```csharp
catch (Exception ex)
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    var outcome = OperationOutcomeBuilder.CreateErrorOutcome(
        ex.Message,
        "exception");
    await context.Response.WriteAsJsonAsync(outcome, cancellationToken);
}
```

**Step 3**: Map exceptions to proper HTTP status codes and OperationOutcome codes

| Exception Type | HTTP Status | Issue Code | Example |
|---|---|---|---|
| ResourceNotFoundException | 404 | not-found | "Resource Patient/123 not found" |
| ValidationException | 400 | invalid | "Field name is required" |
| BadRequestException | 400 | processing | "Invalid request" |
| ConflictException | 409 | conflict | "Resource already exists" |
| UnauthorizedException | 401 | security | "Authentication required" |
| ForbiddenException | 403 | security | "Insufficient permissions" |

#### Acceptance Criteria
- ✅ All error responses have `resourceType: OperationOutcome`
- ✅ `issue.severity` set correctly (error, warning, information)
- ✅ `issue.code` from FHIR IssueType value set
- ✅ `issue.diagnostics` contains clear error message
- ✅ Search tests no longer fail with "unknown element 'issues'" error

---

### Issue 1.2: Fix Bundle JSON Serialization (~50 tests)

**Complexity**: Medium
**Estimated Effort**: 4-8 hours
**Impact Ratio**: 12:1 (50 tests / 4 hours = 12 tests/hour)

#### Root Cause Analysis
Bundle responses are being truncated or malformed, likely due to streaming serialization not properly closing arrays or flushing buffers.

#### Solution

**Files to Investigate**:
1. `src/Ignixa.Application/Features/Bundle/Serialization/StreamingBundleSerializer.cs`
2. `src/Ignixa.Application/Features/Bundle/Serialization/FhirJsonWriter.cs`
3. `src/Ignixa.Api/Infrastructure/FhirEndpoints.cs` (response writing)

#### Investigation Checklist
- [ ] Verify `entry` array is properly closed before response ends
- [ ] Check buffer flushing logic in streaming serializer
- [ ] Ensure no off-by-one errors in JSON bracket counting
- [ ] Test with large bundles (20+ entries) to catch streaming issues
- [ ] Verify response Content-Length header matches actual content

#### Acceptance Criteria
- ✅ Bundle responses are valid JSON (parseable)
- ✅ Bundle.entry array properly closed
- ✅ Search tests return valid Bundle objects
- ✅ Large search results (100+ entries) serialize correctly

---

### Issue 1.3: Fix Conditional Operation Error Handling (~20 tests)

**Complexity**: Simple
**Estimated Effort**: 2-3 hours
**Impact Ratio**: 10:1 (20 tests / 2 hours = 10 tests/hour)

#### Root Cause Analysis
Conditional operations (CREATE, UPDATE, DELETE, PATCH) without search criteria should return 400 BadRequest with OperationOutcome, but instead throw generic HTTP exceptions.

#### Solution

**Files to Update**:
1. `src/Ignixa.Application/Features/ConditionalOperations/ConditionalUpdateHandler.cs`
2. `src/Ignixa.Application/Features/ConditionalOperations/ConditionalCreateHandler.cs`
3. `src/Ignixa.Application/Features/ConditionalOperations/ConditionalDeleteHandler.cs`

#### Implementation

In each conditional operation handler:
```csharp
if (string.IsNullOrWhiteSpace(searchQuery))
{
    throw new BadRequestException(
        "Conditional operation requires search parameters in query string",
        "invalid");
}
```

#### Acceptance Criteria
- ✅ PUT /Patient (no query string) returns 400 with OperationOutcome
- ✅ DELETE /Patient (no query string) returns 400 with OperationOutcome
- ✅ PATCH /Patient (no query string) returns 400 with OperationOutcome
- ✅ ConditionalUpdate/Delete tests pass

---

### Issue 1.4: Fix Bundle Transaction References (~5 tests)

**Complexity**: Medium
**Estimated Effort**: 4-6 hours
**Impact Ratio**: 1.25:1 (5 tests / 4 hours = 1.25 tests/hour)

#### Root Cause Analysis
Bundle entry responses are missing `resourceType` field, which Firely SDK requires to parse resources.

#### Solution

**Files to Update**:
1. `src/Ignixa.Application/Features/Bundle/BundleResponseBuilder.cs`
2. `src/Ignixa.Application/Features/Bundle/BundleEntryExecutor.cs`

#### Implementation

Ensure every bundle entry has:
```json
{
  "fullUrl": "urn:uuid:12345",
  "resource": {
    "resourceType": "Patient",  // ← MUST be present
    "id": "abc-123",
    ...
  },
  "response": {
    "status": "201"
  }
}
```

#### Acceptance Criteria
- ✅ Every entry.resource has `resourceType` set
- ✅ Conditional create/update bundle entries have `resourceType`
- ✅ Bundle transaction tests parse correctly
- ✅ Reference resolution works (urn:uuid references)

---

## Phase 2: High-Value Features (2-3 weeks)

### Issue 2.1: Implement Composite Search Parameters (~100 tests)

**Complexity**: Complex
**Estimated Effort**: 16-24 hours
**Impact Ratio**: 6.25:1 (100 tests / 16 hours = 6.25 tests/hour)

#### Root Cause Analysis
Composite search parameters allow searching with multiple search criteria combined:
- **code-value-quantity**: Code + numeric value (e.g., Observation with LOINC code + vital sign measurement)
- **combo-code-value-quantity**: Code + value across multiple value fields
- **combo-code-value-concept**: Code + coded value pair
- **code-value-string**: Code + string value

#### FHIR Specification Reference

**Composite Parameter Syntax**: `param=component1$component2`

**Example**:
```
GET /Observation?code-value-quantity=http://loinc.org|8480-6$gt60
```

This searches for:
- Component 1: code = LOINC 8480-6 (Systolic blood pressure)
- Component 2: value > 60 (mmHg)
- Both must be in the same Observation

**FHIR R4 Spec**: http://hl7.org/fhir/R4/search.html#composite

#### Solution

**Files to Create/Update**:
1. New: `src/Ignixa.Search/SearchParameters/CompositeSearchParameterHandler.cs`
2. New: `src/Ignixa.Search/SearchParameters/CompositeParameterParser.cs`
3. Update: `src/Ignixa.Search/SearchParameterIndexer.cs` (add composite indexing)
4. Update: `src/Ignixa.Application/Features/Resource/SearchResourcesHandler.cs` (parse composite syntax)

#### Implementation Strategy

**Step 1**: Parse composite parameter syntax
```csharp
// Input: "code-value-quantity=http://loinc.org|8480-6$gt60"
// Output: { component1: "http://loinc.org|8480-6", component2: "gt60" }

var parts = parameterValue.Split('$');
// Validate exactly 2 components for code-value-quantity
```

**Step 2**: Index composite search parameters during resource creation
- Store component pairs in search index
- Enable efficient querying with both components

**Step 3**: Query composite parameters
```csharp
// Search for code + value combination
var code = "http://loinc.org|8480-6";
var value = "gt60";
var results = db.Observations
    .Where(o => o.Code == code && o.ValueQuantity > 60)
    .ToList();
```

#### Acceptance Criteria
- ✅ Observation?code-value-quantity=code$value returns correct results
- ✅ Observation?combo-code-value-quantity=code$value works
- ✅ Observation?combo-code-value-concept=code$value works
- ✅ Observation?code-value-string=code$string works
- ✅ 100 composite search tests passing

---

### Issue 2.2: Fix Timezone Handling (~10 tests)

**Complexity**: Simple
**Estimated Effort**: 2-4 hours
**Impact Ratio**: 5:1 (10 tests / 2 hours = 5 tests/hour)

#### Root Cause Analysis
Dates are serialized in local timezone instead of UTC, causing date searches to return incorrect results.

#### Solution

**Files to Update**:
1. `src/Ignixa.Api/Program.cs` (JSON serializer configuration)
2. `src/Ignixa.Application/Features/Resource/*Handler.cs` (lastUpdated population)
3. `src/Ignixa.Search/*` (date search parameter parsing)

#### Implementation

**Configure JSON serializer for UTC**:
```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    DateTimeZoneHandling = DateTimeZoneHandling.Utc  // ← UTC only
};
```

**Ensure lastUpdated always in UTC**:
```csharp
resource.Meta.LastUpdated = DateTimeOffset.UtcNow;  // ← Always UTC
```

#### Acceptance Criteria
- ✅ All dates serialized as UTC (Z suffix or +00:00)
- ✅ Date search filters work correctly
- ✅ _lastUpdated searches return correct results
- ✅ Timezone-related tests passing

---

## Phase 3: Polish (1 week)

### Issue 3.1: Implement System-Level Search (~5 tests)

**Complexity**: Medium
**Estimated Effort**: 6-8 hours
**Impact Ratio**: 0.83:1 (5 tests / 6 hours = 0.83 tests/hour)

#### Root Cause Analysis
`GET [base]?param=value` (searching all resource types) not implemented.

#### Solution

**New Endpoint**:
```csharp
// GET / or GET /?_type=Patient,Observation&_lastUpdated=gt2021-01-01
endpoints.MapGet("/", HandleSystemLevelSearch);
```

**Implementation**:
1. Create `SystemLevelSearchHandler.cs`
2. Support `_type` parameter to filter resource types
3. Return Bundle with mixed resource types

#### Acceptance Criteria
- ✅ GET /?_tag=system-tag searches all resource types
- ✅ GET /?_type=Patient,Observation filters by type
- ✅ Bundle contains mixed resource types
- ✅ 5 system-level search tests passing

---

### Issue 3.2: Fix Forwarded Host Header Handling (~2 tests)

**Complexity**: Simple
**Estimated Effort**: 1-2 hours
**Impact Ratio**: 2:1 (2 tests / 1 hour = 2 tests/hour)

#### Root Cause Analysis
Self-links in search results use internal hostname instead of forwarded host (X-Forwarded-Host header).

#### Solution

**Update all link-building code** to check for forwarded headers:
```csharp
var host = context.Request.Headers["X-Forwarded-Host"].FirstOrDefault()
    ?? context.Request.Host.Host;
var protocol = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault()
    ?? context.Request.Scheme;

var baseUrl = $"{protocol}://{host}";
```

**Files to Update**:
1. `src/Ignixa.Api/Infrastructure/FhirEndpoints.cs`
2. Pagination link builders
3. Reference builders

#### Acceptance Criteria
- ✅ Self-links use X-Forwarded-Host when present
- ✅ Proxy deployment scenarios work correctly
- ✅ 2 proxy tests passing

---

## Implementation Roadmap

### Timeline: 4-6 weeks (36-48 hours total effort)

| Phase | Tasks | Duration | Tests Fixed | Cumulative Pass Rate |
|-------|-------|----------|---|---|
| **Phase 1** | Critical fixes (OperationOutcome, Bundle JSON, Conditional Validation, Bundle References) | 8-21h | ~270 | 22% → 52% |
| **Phase 2** | Composite search, timezone fixes | 18-28h | ~110 | 52% → 64% |
| **Phase 3** | System search, proxy headers | 7-10h | ~7 | 64% → 65% |
| **Deferred** | Authorization, edge cases | Later | ~12 | 65% → 66%+ |

### Phase 1: Critical Fixes (Week 1-2)

**Priority**: CRITICAL - Start immediately

| Issue | Effort | Tests | Sequence |
|-------|--------|-------|----------|
| Fix OperationOutcome | 2-4h | +200 | Start first (blocks others) |
| Fix Bundle JSON | 4-8h | +50 | Parallel after OperationOutcome |
| Fix Conditional Validation | 2-3h | +20 | Parallel |
| Fix Bundle References | 4-6h | +5 | Parallel |

**Expected Result**: 22% → 52% pass rate (+270 tests)

---

### Phase 2: High-Value Features (Week 3-5)

**Priority**: HIGH - Implement after Phase 1

| Issue | Effort | Tests | Dependencies |
|-------|--------|-------|--|
| Composite Search Parameters | 16-24h | +100 | Working search infrastructure |
| Timezone Fixes | 2-4h | +10 | None |

**Expected Result**: 52% → 64% pass rate (+110 tests)

---

### Phase 3: Polish (Week 6)

**Priority**: MEDIUM - Implement after Phase 2

| Issue | Effort | Tests | Dependencies |
|-------|--------|-------|--|
| System-Level Search | 6-8h | +5 | Working per-type search |
| Proxy Headers | 1-2h | +2 | None |

**Expected Result**: 64% → 65% pass rate (+7 tests)

---

## Testing Strategy

### For Each Issue:

1. **Unit Tests**: Test handler/utility in isolation
2. **Integration Tests**: Test HTTP endpoint with real request/response
3. **Compatibility Tests**: Re-run compatibility test suite to verify improvement
4. **Regression Tests**: Ensure fixes don't break existing passing tests

### Build & Test Checklist

```bash
# Build
dotnet build All.sln  # Must be 0 warnings, 0 errors

# Test
dotnet test All.sln   # All tests passing

# Run compatibility suite
cd test/Ignixa.*.Tests/
dotnet test Ignixa.Compat.Tests.csproj

# Check pass rate improvement
# Before: 199/898 (22.2%)
# After Phase 1: 470/898 (52%)
# After Phase 2: 580/898 (64%)
# After Phase 3: 587/898 (65%)
```

---

## Consequences

### Positive Consequences

1. **✅ FHIR Compliance Jump**
   - 22% → 65% pass rate
   - Core operations now FHIR-compliant
   - Enables production deployment

2. **✅ Client Integration Ready**
   - Proper error handling with OperationOutcome
   - Valid JSON responses
   - Timezone consistency

3. **✅ Developer Experience**
   - Clear error messages
   - Proper HTTP status codes
   - Better debugging

4. **✅ Advanced Features**
   - Composite search for clinical queries
   - System-level search for cross-resource searches
   - Production-ready proxy support

### Negative Consequences

1. **⚠️ Composite Search Performance**
   - Complex query logic may require indexing optimization
   - Mitigation: Profile and optimize after implementation

2. **⚠️ Timeline Impact**
   - 36-48 hours of work
   - 4-6 week implementation
   - Mitigation: Start with Phase 1 (quick wins)

3. **⚠️ Risk of Regressions**
   - Large changes to error handling and serialization
   - Mitigation: Comprehensive test coverage

---

## Open Questions

1. **Authorization**: Should we implement in Phase 4 or defer? (Requires auth architecture)
2. **Composite Search Performance**: Any known performance requirements?
3. **System-Level Search**: Should it search EVERY resource type or configurable list?
4. **Priority**: Should we defer Phase 3 to focus on Phase 2?

---

## References

### FHIR Specifications

1. **OperationOutcome**: http://hl7.org/fhir/R4/operationoutcome.html
2. **Bundle**: http://hl7.org/fhir/R4/bundle.html
3. **Search**: http://hl7.org/fhir/R4/search.html
4. **Composite Search Parameters**: http://hl7.org/fhir/R4/search.html#composite
5. **Conditional Interactions**: http://hl7.org/fhir/R4/http.html

### Related ADRs

1. [ADR-2500: Master Roadmap](../adr/adr-2500-master-implementation-roadmap.md)
2. [ADR-2525: Conditional Operations](../adr/adr-2525-conditional-operations.md)
3. [ADR-2523: Multi-Tenancy](../adr/adr-2523-phase20-multi-tenancy-data-partitioning.md)

---

## Appendix A: Failure Pattern Details

### Complete Failure Analysis

**Pattern Distribution**:
- OperationOutcome format errors: ~200 tests (29%)
- Bundle JSON issues: ~50 tests (7%)
- Conditional validation: ~20 tests (3%)
- Composite search: ~100 tests (15%)
- Bundle references: ~5 tests (1%)
- Timezone issues: ~10 tests (1%)
- System search: ~5 tests (1%)
- Proxy headers: ~2 tests (<1%)
- Authorization: ~10 tests (1%)
- Edge cases: ~2 tests (<1%)
- **Unknown/Other**: ~280 tests (41%)

### Tests by Category

**Search Tests** (affected by OperationOutcome + Bundle issues):
- Canonical search
- Composite search
- String search
- Token search
- Quantity search
- URI search
- Reference search
- Date search

**Transaction Tests** (affected by Bundle issues):
- Bundle creation
- Bundle transactions
- Conditional operations in bundles
- Multi-step workflows

**Conditional Operation Tests** (affected by validation):
- Conditional create
- Conditional update
- Conditional delete
- Conditional patch

---

## Appendix B: File Checklist

### Phase 1 Files to Modify/Create

```
src/Ignixa.Api/Infrastructure/
  ├── FhirExceptionMiddleware.cs (UPDATE)
  └── FhirEndpoints.cs (UPDATE)

src/Ignixa.Application/Features/
  ├── ConditionalOperations/
  │   ├── ConditionalCreateHandler.cs (UPDATE)
  │   ├── ConditionalUpdateHandler.cs (UPDATE)
  │   └── ConditionalDeleteHandler.cs (UPDATE)
  └── Bundle/
      ├── BundleResponseBuilder.cs (UPDATE)
      └── BundleEntryExecutor.cs (UPDATE)

test/Ignixa.*.Tests/
  └── [Phase 1 Test Files] (CREATE)
```

### Phase 2 Files to Modify/Create

```
src/Ignixa.Search/
  ├── SearchParameters/
  │   └── CompositeSearchParameterHandler.cs (CREATE)
  └── SearchParameterIndexer.cs (UPDATE)

src/Ignixa.Application/
  └── Features/Resource/SearchResourcesHandler.cs (UPDATE)

src/Ignixa.Api/Program.cs (UPDATE)
```

### Phase 3 Files to Modify/Create

```
src/Ignixa.Api/Infrastructure/
  └── FhirEndpoints.cs (UPDATE - add system search route)

src/Ignixa.Application/Features/
  └── [New SystemLevelSearchHandler]
```

---

**Status**: Proposed
**Next Steps**: Review and approval, then begin Phase 1 implementation
**Implementation Owner**: Development Team
**Review Date**: 2025-10-28
