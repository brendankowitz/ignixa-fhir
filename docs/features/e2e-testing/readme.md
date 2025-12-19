# Feature: E2E Testing

End-to-end testing infrastructure and coverage for FHIR server functionality.

## Investigations

| Investigation | Status | Date | Description |
|---------------|--------|------|-------------|
| [gap-analysis](investigations/gap-analysis.md) | Complete | 2025-12-11 | Comparison with fhir-candle R4Tests, identifies missing test categories |
| [implementation-checklist](investigations/implementation-checklist.md) | Complete | 2025-12-11 | Task-by-task implementation guide with 60+ test scenarios |
| [analysis-readme](investigations/analysis-readme.md) | Complete | 2025-12-11 | Documentation index and quick start guide |
| [data-setup-patterns](investigations/data-setup-patterns.md) | Complete | 2025-12-05 | Single-patient vs multi-patient test data patterns |

## Overview

Comprehensive E2E test coverage for FHIR search, operations, and compliance.

## Current Coverage

**Well-Covered Areas** ✅
- Include/Revinclude (95% coverage, 40+ tests)
- Datatype Search (75% coverage - token, string, date)
- Basic Search (70% coverage)
- Chaining (60% coverage)

**Critical Gaps** ❌
- Compartment searches (0% coverage)
- Conditional operations (0% coverage)
- _summary parameter (0% coverage)
- Quantity searches (10% coverage)
- :missing modifier (0% coverage)

## Implementation Roadmap

### Phase 1: Core Features (HIGH Priority)
- CompartmentSearchTests.cs (3-5 tests)
- ConditionalOperationTests.cs (3-5 tests)
- SummaryParameterTests.cs (5-10 tests)
- QuantitySearchTests.cs (5-10 tests)
- MissingModifierTests.cs (5-10 tests)
- **Effort**: 20-30 hours

### Phase 2: Enhanced Search (MEDIUM Priority)
- DateSearchTests extensions
- NumberSearchTests
- TokenSearchTests extensions
- **Effort**: 10-15 hours

### Phase 3: Advanced Features (LOW Priority)
- SystemSearchTests
- SubscriptionTests (if roadmap)
- **Effort**: 5-10 hours

## Test Statistics

- Current E2E Tests: ~113 tests across 13 files
- fhir-candle R4Tests: ~140 test cases
- Identified Gaps: 60+ missing test scenarios
- Total Implementation: 35-55 hours (3 phases)

## FhirFakes Dependencies

The E2E testing feature requires enhancements to the FhirFakes library:

1. **Proposal 3**: Explicit field omission (WithoutActive) - 2-4 hours
2. **Proposal 4**: Profile metadata support - 3-4 hours
3. **Proposal 5**: ObservationBuilder - 4-6 hours

See [FhirFakes Enhancement Proposals](../fhir-faker/investigations/enhancement-proposals.md) for details.

## Test Patterns

### Single-Patient Journey
Use `ScenarioBuilder` for temporal sequences:
```csharp
var scenario = new ScenarioBuilder(schemaProvider)
    .WithPatient(age: 45, gender: "male")
    .AddEncounter("Annual physical")
    .AddObservation(VitalSigns.BloodPressure)
    .Build();
```

### Multi-Patient Comparison
Use direct builders for independent patients:
```csharp
var smith = PatientBuilderFactory.Create(SchemaProvider)
    .WithGivenName("Smith")
    .WithTag(tag)
    .Build();
```

## Success Criteria

- All Phase 1 tests passing (30-40 new test cases)
- Code coverage maintained or improved
- All tests use tag-based isolation
- Zero regression in existing tests

## See Also

- [FHIR Faker Feature](../fhir-faker/readme.md) - Test data generation
- [Search Feature](../search/readme.md) - Search implementation tested by E2E
