# E2E Test Implementation Checklist

**Source**: `e2e-test-gap-analysis.md` and `fhirfakes-enhancement-proposals.md`  
**Status**: Ready for implementation  
**Last Updated**: 2025-12-11

---

## Phase 1: Core Missing Features (HIGH Priority) 🔴

### FhirFakes Enhancements

- [ ] **Proposal 4: Profile Metadata Support** (3-4 hours)
  - [ ] Add `WithProfileUri(string)` to PatientBuilder
  - [ ] Consider ResourceBuilder<T> base class approach
  - [ ] Add unit tests for profile metadata
  - [ ] Update XML docs with examples

- [ ] **Proposal 3: Explicit Field Omission** (2-4 hours)
  - [ ] Add `WithoutActive()` to PatientBuilder
  - [ ] Add `WithoutTelecom()` to PatientBuilder
  - [ ] Add `WithoutAddress()` to PatientBuilder
  - [ ] Refactor field generation to support omission
  - [ ] Add unit tests for missing fields

- [ ] **Proposal 5: ObservationBuilder** (4-6 hours)
  - [ ] Create new `ObservationBuilder` class
  - [ ] Add `WithCode(code, system, display)` method
  - [ ] Add `WithQuantityValue(value, unit, system)` method
  - [ ] Add `WithSubject(patientId)` method
  - [ ] Add `WithTag(tag)` method
  - [ ] Create `ObservationBuilderFactory`
  - [ ] Add comprehensive unit tests

### E2E Test Files

- [ ] **CompartmentSearchTests.cs** (3-5 tests)
  - [ ] Test: `GivenPatientCompartment_WhenSearchAllResources_ThenReturnsCompartmentResources`
    - Search: `GET /Patient/{id}/*`
    - Expected: All patient-linked resources
  - [ ] Test: `GivenPatientCompartment_WhenSearchSpecificType_ThenReturnsTypeOnly`
    - Search: `GET /Patient/{id}/Observation`
    - Expected: Only Observations for patient
  - [ ] Test: `GivenPatientCompartment_WhenSearchWithParameters_ThenFiltersResults`
    - Search: `GET /Patient/{id}/Observation?code=85354-9`
    - Expected: Filtered observations
  - [ ] Test: `GivenEmptyCompartment_WhenSearched_ThenReturnsEmptyBundle`
    - Search: `GET /Patient/{id}/*` (patient with no resources)
    - Expected: Empty bundle with total=0
  - [ ] Test: `GivenInvalidPatientId_WhenSearchCompartment_ThenReturns404`
    - Search: `GET /Patient/nonexistent/*`
    - Expected: 404 or empty bundle

- [ ] **ConditionalOperationTests.cs** (3-5 tests)
  - [ ] Test: `GivenConditionalCreate_WhenNoMatch_ThenCreatesNewResource`
    - Action: POST /Patient with If-None-Exist: _id=newid
    - Expected: 201 Created, new resource, Location header
  - [ ] Test: `GivenConditionalCreate_WhenOneMatch_ThenReturnsExisting`
    - Action: POST /Patient with If-None-Exist: _id=existing
    - Expected: 200 OK, existing resource, same ETag
  - [ ] Test: `GivenConditionalCreate_WhenMultipleMatches_ThenReturnsPreconditionFailed`
    - Action: POST /Patient with If-None-Exist: gender=male (matches 2+)
    - Expected: 412 Precondition Failed or 400 Bad Request
  - [ ] Test: `GivenConditionalUpdate_WhenNoMatch_ThenCreatesResource`
    - Action: PUT /Patient/{id} with If-Match
    - Expected: 201 Created (if server supports)
  - [ ] Test: `GivenConditionalDelete_WhenMatches_ThenDeletesResource`
    - Action: DELETE /Patient?identifier=xyz
    - Expected: 200 OK or 204 No Content

- [ ] **SummaryParameterTests.cs** (5-10 tests)
  - [ ] Test: `GivenSummaryFalse_WhenSearched_ThenReturnsFullRepresentation`
    - Search: `?_summary=false`
    - Assert: Longest response, has all fields
  - [ ] Test: `GivenSummaryTrue_WhenSearched_ThenReturnsSummaryElements`
    - Search: `?_summary=true`
    - Assert: Shorter than false, has summary elements
  - [ ] Test: `GivenSummaryText_WhenSearched_ThenReturnsTextOnly`
    - Search: `?_summary=text`
    - Assert: Contains narrative text elements
  - [ ] Test: `GivenSummaryData_WhenSearched_ThenReturnsDataElements`
    - Search: `?_summary=data`
    - Assert: Data elements, no text/narrative
  - [ ] Test: `GivenSummaryCount_WhenSearched_ThenReturnsCountOnly`
    - Search: `?_summary=count`
    - Assert: total present, entry.length = 0
  - [ ] Test: `GivenSummaryParameter_WhenSearched_ThenSelfLinkIncludesSummary`
    - Search: `?_summary={flag}`
    - Assert: self link contains `_summary={flag}`
  - [ ] Test: `GivenMultipleSummaryFlags_WhenCompared_ThenResponseLengthOrdered`
    - Search all flags, compare response lengths
    - Assert: false >= true >= text >= data, count smallest
  - [ ] Test: `GivenSummaryParameter_WhenSearched_ThenTotalConsistent`
    - Search with all flags
    - Assert: total is same across all flags
  - [ ] Test: `GivenInvalidSummaryValue_WhenSearched_ThenReturnsError`
    - Search: `?_summary=invalid`
    - Assert: 400 Bad Request or ignored (check spec)
  - [ ] Test: `GivenSummaryWithIncludes_WhenSearched_ThenIncludesAlsoSummarized`
    - Search: `?_include=Patient:organization&_summary=true`
    - Assert: Included resources also summarized

- [ ] **QuantitySearchTests.cs** (5-10 tests)
  - [ ] Test: `GivenQuantityObservations_WhenSearchedExact_ThenReturnsMatch`
    - Search: `value-quantity=185`
    - Expected: Exact match (with tolerance)
  - [ ] Test: `GivenQuantityObservations_WhenSearchedGreaterOrEqual_ThenReturnsMatching`
    - Search: `value-quantity=ge185`
    - Expected: value >= 185
  - [ ] Test: `GivenQuantityObservations_WhenSearchedGreaterThan_ThenReturnsMatching`
    - Search: `value-quantity=gt185`
    - Expected: value > 185
  - [ ] Test: `GivenQuantityObservations_WhenSearchedLessOrEqual_ThenReturnsMatching`
    - Search: `value-quantity=le185`
    - Expected: value <= 185
  - [ ] Test: `GivenQuantityObservations_WhenSearchedLessThan_ThenReturnsMatching`
    - Search: `value-quantity=lt185`
    - Expected: value < 185
  - [ ] Test: `GivenQuantityObservations_WhenSearchedWithSystemAndUnit_ThenReturnsMatch`
    - Search: `value-quantity=185|http://unitsofmeasure.org|[lb_av]`
    - Expected: Matches system+unit+value
  - [ ] Test: `GivenQuantityObservations_WhenSearchedWithUnitOnly_ThenReturnsMatch`
    - Search: `value-quantity=185||[lb_av]`
    - Expected: Matches unit+value (any system)
  - [ ] Test: `GivenQuantityObservations_WhenSearchedComposite_ThenReturnsMatch`
    - Search: `code-value-quantity=http://loinc.org|29463-7$185|...|[lb_av]`
    - Expected: Matches code AND value
  - [ ] Test: `GivenQuantityObservations_WhenNoMatch_ThenReturnsEmpty`
    - Search: `value-quantity=999999`
    - Expected: Empty bundle
  - [ ] Test: `GivenQuantityWithDifferentUnits_WhenSearched_ThenRespects UnitSystem`
    - Search: `value-quantity=185|...|[lb_av]` vs `|...|kg`
    - Expected: Only matches correct unit (no conversion yet)

- [ ] **MissingModifierTests.cs** (5-10 tests)
  - [ ] Test: `GivenPatientsWithAndWithoutActive_WhenSearchedMissingTrue_ThenReturnsWithout`
    - Search: `active:missing=true`
    - Expected: Patients without active field
  - [ ] Test: `GivenPatientsWithAndWithoutActive_WhenSearchedMissingFalse_ThenReturnsWith`
    - Search: `active:missing=false`
    - Expected: Patients with active field (any value)
  - [ ] Test: `GivenObservationsWithAndWithoutProfile_WhenSearchedMissing_ThenFilters`
    - Search: `_profile:missing=true`
    - Expected: Observations without meta.profile
  - [ ] Test: `GivenPatientsWithAndWithoutTelecom_WhenSearchedMissing_ThenFilters`
    - Search: `telecom:missing=true`
    - Expected: Patients without telecom
  - [ ] Test: `GivenMixedData_WhenSearchedMissingFalse_ThenReturnsOnlyWithValue`
    - Search: `gender:missing=false`
    - Expected: Only patients with gender (not null)
  - [ ] Test: `GivenMissingModifierWithValue_WhenSearched_ThenIgnoresValue`
    - Search: `active:missing=true&active=true`
    - Expected: Only missing=true applies (or error)
  - [ ] Test: `GivenMissingModifierOnReference_WhenSearched_ThenFilters`
    - Search: `organization:missing=true`
    - Expected: Patients without organization reference
  - [ ] Test: `GivenMissingModifierOnToken_WhenSearched_ThenFilters`
    - Search: `identifier:missing=false`
    - Expected: Patients with identifier
  - [ ] Test: `GivenMissingModifierOnDate_WhenSearched_ThenFilters`
    - Search: `birthdate:missing=true`
    - Expected: Patients without birthdate
  - [ ] Test: `GivenInvalidMissingValue_WhenSearched_ThenReturnsError`
    - Search: `active:missing=invalid`
    - Expected: 400 Bad Request

---

## Phase 2: Enhanced Search (MEDIUM Priority) 🟡

### FhirFakes Enhancements

- [ ] **Proposal 1: MultipleBirth Support** (1-2 hours)
  - [ ] Add `WithMultipleBirth(int order)` to PatientBuilder
  - [ ] Add `WithMultipleBirth(bool)` overload
  - [ ] Handle mutually exclusive variants
  - [ ] Add unit tests

- [ ] **Proposal 2: BirthDate Precision** (1-2 hours)
  - [ ] Add `WithBirthDate(int year)` overload
  - [ ] Add `WithBirthDate(int year, int month)` overload
  - [ ] Update existing `WithBirthDate(year, month, day)` if needed
  - [ ] Add validation for valid dates
  - [ ] Add unit tests

- [ ] **Proposal 6: Identifier Helper** (2-3 hours)
  - [ ] Add `WithIdentifier(system, value, typeSystem, typeCode)` to PatientBuilder
  - [ ] Add `WithMedicalRecordNumber(value, system)` convenience method
  - [ ] Support multiple identifiers
  - [ ] Add unit tests

### E2E Test Files

- [ ] **Extend DateSearchTests.cs** (3-5 new tests)
  - [ ] Test: `GivenPatientsWithYearOnlyBirthDate_WhenSearchedByYear_ThenReturnsMatching`
    - Search: `birthdate=1982`
    - Expected: All patients born in 1982 (any month/day)
  - [ ] Test: `GivenPatientsWithMonthPrecisionBirthDate_WhenSearchedByMonth_ThenReturnsMatching`
    - Search: `birthdate=1982-01`
    - Expected: All patients born in Jan 1982
  - [ ] Test: `GivenPatientsWithDayPrecisionBirthDate_WhenSearchedExact_ThenReturnsMatch`
    - Search: `birthdate=1982-01-23`
    - Expected: Exact date match
  - [ ] Test: `GivenMixedPrecisionBirthDates_WhenSearchedWithPrefix_ThenHandlesPrecision`
    - Search: `birthdate=ge1982` (should match 1982+ regardless of precision)
  - [ ] Test: `GivenPartialDateSearch_WhenCombinedWithRange_ThenFiltersCorrectly`
    - Search: `birthdate=ge1982&birthdate=le1985`

- [ ] **Create NumberSearchTests.cs** (5-8 tests)
  - [ ] Test: `GivenPatientsWithMultipleBirthOrder_WhenSearchedExact_ThenReturnsMatch`
    - Search: `multiplebirth=3`
  - [ ] Test: `GivenPatientsWithMultipleBirthOrder_WhenSearchedLessOrEqual_ThenReturnsMatching`
    - Search: `multiplebirth=le3`
  - [ ] Test: `GivenPatientsWithMultipleBirthOrder_WhenSearchedLessThan_ThenReturnsMatching`
    - Search: `multiplebirth=lt3`
  - [ ] Test: `GivenPatientsWithMultipleBirthOrder_WhenSearchedGreaterOrEqual_ThenReturnsMatching`
    - Search: `multiplebirth=ge2`
  - [ ] Test: `GivenPatientsWithMultipleBirthBoolean_WhenSearched_ThenMatchesBooleanOnly`
    - Search: `multiplebirth=true` or `multiplebirth=false`
    - Note: May need to check spec for how boolean vs integer is searched
  - [ ] Test: `GivenMixedMultipleBirthTypes_WhenSearched_ThenHandlesBothTypes`
    - Some patients with integer, some with boolean
  - [ ] Test: `GivenNoMultipleBirth_WhenSearchedMissing_ThenReturnsPatients`
    - Search: `multiplebirth:missing=true`
  - [ ] Test: `GivenMultipleBirthWithRange_WhenSearched_ThenFiltersCorrectly`
    - Search: `multiplebirth=ge1&multiplebirth=le3`

- [ ] **Extend TokenSearchTests.cs** (3-5 new tests)
  - [ ] Test: `GivenPatientWithTypedIdentifier_WhenSearchedWithOfType_ThenReturnsMatch`
    - Search: `identifier:of-type=http://.../v2-0203|MR|12345`
    - Expected: Patient with MR identifier
  - [ ] Test: `GivenPatientWithMultipleIdentifierTypes_WhenSearchedWithOfType_ThenFiltersCorrectly`
    - Create patient with MR and DL identifiers
    - Search each type separately
  - [ ] Test: `GivenObservationSubject_WhenSearchedWithTypeModifier_ThenFiltersCorrectly`
    - Search: `subject:Patient=Patient/xyz` (should match)
    - Search: `subject:Device=Patient/xyz` (should not match)
  - [ ] Test: `GivenReferenceWithWrongType_WhenSearchedWithTypeModifier_ThenReturnsEmpty`
    - Ensure type filtering works
  - [ ] Test: `GivenIdentifierOfTypeWithMissingFields_WhenSearched_ThenHandlesGracefully`
    - Test edge cases: missing type, missing system, etc.

---

## Phase 3: Advanced Features (LOW Priority) 🟢

### E2E Test Files

- [ ] **SystemSearchTests.cs** (3-5 tests)
  - [ ] Test: `GivenMultipleResourceTypes_WhenSearchedWithTypeParameter_ThenReturnsAllTypes`
    - Search: `?_type=Patient,Observation`
    - Expected: Bundle with both resource types
  - [ ] Test: `GivenSystemSearch_WhenFilteredByType_ThenReturnsOnlySpecified`
    - Search: `?_type=Patient` (should not return Observations)
  - [ ] Test: `GivenSystemSearch_WhenCombinedWithOtherParams_ThenFiltersCorrectly`
    - Search: `?_type=Patient&_lastUpdated=ge2024-01-01`
  - [ ] Test: `GivenInvalidResourceType_WhenSearched_ThenReturnsErrorOrIgnores`
    - Search: `?_type=InvalidResourceType`
  - [ ] Test: `GivenEmptyTypeParameter_WhenSearched_ThenHandlesGracefully`
    - Search: `?_type=`

- [ ] **SubscriptionTests.cs** (if on roadmap)
  - [ ] Topic parsing tests
  - [ ] Subscription parsing tests
  - [ ] Notification handling tests
  - [ ] Webhook delivery tests

---

## Testing Infrastructure

- [ ] Update `CapabilityDrivenTestBase` if needed
- [ ] Add new capability checks for:
  - [ ] Compartment search support
  - [ ] Conditional operation support
  - [ ] _summary parameter support
  - [ ] value-quantity search support
- [ ] Add test fixture for compartment tests if needed
- [ ] Add test fixture for conditional operations if needed

---

## Documentation

- [ ] Update `test/Ignixa.Api.E2ETests/README.md` with:
  - [ ] New test categories
  - [ ] Examples of compartment searches
  - [ ] Examples of conditional operations
  - [ ] Examples of _summary usage
- [ ] Update main README.md if test coverage is mentioned
- [ ] Add XML docs to all new FhirFakes methods
- [ ] Create migration guide if breaking changes

---

## Quality Checks

- [ ] All tests use tag-based isolation
- [ ] All tests have capability checks
- [ ] All tests use FluentAssertions
- [ ] All tests follow AAA pattern (Arrange, Act, Assert)
- [ ] No hardcoded resource IDs (except in conditional tests)
- [ ] Tests are independent (no shared state)
- [ ] Tests clean up resources (via tags)
- [ ] All FhirFakes methods have XML docs
- [ ] All FhirFakes methods have unit tests
- [ ] Code coverage maintained or improved

---

## Verification

- [ ] Run all existing E2E tests (should still pass)
- [ ] Run all new E2E tests
- [ ] Run FhirFakes unit tests
- [ ] Check code coverage reports
- [ ] Manual verification of key scenarios
- [ ] Performance regression check (if needed)

---

## Progress Tracking

**Phase 1 Completion**: 0% (0/34 items)  
**Phase 2 Completion**: 0% (0/21 items)  
**Phase 3 Completion**: 0% (0/5 items)  
**Overall Completion**: 0% (0/60 items)

**Estimated Total Effort**: 
- Phase 1: 20-30 hours
- Phase 2: 10-15 hours
- Phase 3: 5-10 hours
- **Total**: 35-55 hours

---

## Notes

- Priorities can be adjusted based on roadmap
- Some tests may be skipped if feature not supported
- Unit conversions (lb to kg) are marked as TODO in original tests
- Authorization/scoping tests can be Phase 4 if not immediate need
- Consider parallelization of test execution for large suites

---

## References

- Main Analysis: `docs/investigations/e2e-test-gap-analysis.md`
- Enhancement Proposals: `docs/investigations/fhirfakes-enhancement-proposals.md`
- Source Reference: [fhir-candle R4Tests.cs](https://github.com/FHIR/fhir-candle/blob/main/src/fhir-candle.Tests/R4Tests.cs)
