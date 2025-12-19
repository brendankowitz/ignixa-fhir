# Investigation: FHIRPath Gap Analysis

**Feature**: fhirpath
**Status**: Investigation Complete
**Created**: 2025-11-18

**Date:** 2025-11-18
**Specification Version:** FHIRPath 3.0.0-ballot (R2 STU1)
**Implementation:** Ignixa.FhirPath v1.0
**Author:** Technical Analysis

---

## Executive Summary

The Ignixa.FhirPath implementation provides **comprehensive coverage** of the FHIRPath v2.0.0 (Normative) features with **64 implemented functions** covering all common FHIR use cases. However, there are **significant gaps** in the newer **STU (Standard for Trial Use)** features and functions introduced in FHIRPath 3.0.0-ballot (not yet finalized).

### Coverage Overview

| Category | Implemented | Missing | Coverage |
|----------|------------|---------|----------|
| **Normative Features** | 64 functions | 1 critical (Quantity eval) | **~98%** |
| **STU Features** | 2 functions | 45+ functions | **~4%** |
| **Operators** | 15 operators | 1 operator (`not`) | **~94%** |
| **Literal Types** | 7 types | 1 type (Long) | **~88%** |
| **Overall Compliance** | **Core: 98%** | **Extended: 4%** | **Combined: ~65%** |

### Specification Version Notes

⚠️ **Important Clarification:**
- **FHIRPath v2.0.0** (January 2020): **Normative, ANSI-certified, Production-ready**
  - 61 normative functions (stable, unlikely to change)
  - Our 64 implemented functions cover v2.0.0 + some v3.0 early additions

- **FHIRPath v3.0.0-ballot** (December 2024): **NOT FINALIZED, Subject to change**
  - Adds 33+ new functions and improvements
  - New STU features (Date/Time components, String extensions, Sorting, etc.)
  - Expected finalization timeline: Unknown (ballot in progress)

**Recommendation:** Build toward v3.0.0-ballot for future compatibility, but ensure v2.0.0 normative features work identically for production stability.

### Priority Classification

- **🔴 Critical Gaps:** Affect existing FHIR operations (1 item)
- **🟡 High Priority:** Commonly used features in modern FHIR (12 items)
- **🟢 Medium Priority:** STU features with specific use cases (18 items)
- **⚪ Low Priority:** Advanced/rare use cases (15+ items)

---

## Detailed Gap Analysis

### 1. Critical Gaps (🔴)

#### 1.1 Quantity Literal Evaluation
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **HIGH** - Required for FHIR Observation, medication dosages, vital signs
**Spec:** `5 'mg'`, `37.5 'Cel'`, `4 days`

**Current Behavior:**
```csharp
// Parsing works: QuantityExpression created
var expr = FhirPathCompiler.Parse("5 'mg'");

// Evaluation fails with NotImplementedException
var result = evaluator.Evaluate(expr, resource); // ❌ Throws
```

**Files Affected:**
- `src/Ignixa.FhirPath/Evaluation/FhirPathEvaluator.cs` (missing QuantityExpression handler)
- `src/Ignixa.FhirPath/Compilation/FhirPathDelegateCompiler.cs` (no compilation support)

**Required Implementation:**
- Quantity arithmetic: `+`, `-`, `*`, `/`, `mod`
- Quantity comparison: `=`, `!=`, `<`, `<=`, `>`, `>=`
- Unit conversion (UCUM units)
- Calendar duration support: `1 year`, `4 days`, `1 hour`

**Estimated Effort:** 2-3 weeks (complex - requires UCUM library integration)

**Recommendation:** **MUST IMPLEMENT** before Phase 22 (FHIR _history) completion, as Observation resources heavily rely on quantities.

---

### 2. High Priority Gaps (🟡)

#### 2.1 Aggregate Functions (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **HIGH** - Common in FHIR queries, analytics, validation rules
**Spec Section:** 5.8 Aggregates

**Missing Functions:**
```fhirpath
// General aggregation
aggregate(aggregator : expression [, init : value]) : value

// Specific aggregates
sum() : Integer | Long | Decimal | Quantity
min() : Integer | Long | Decimal | Quantity | Date | DateTime | Time | String
max() : Integer | Long | Decimal | Quantity | Date | DateTime | Time | String
avg() : Decimal | Quantity
```

**Use Cases:**
- **Vital signs analysis:** `Observation.where(code='blood-pressure').value.max()`
- **Medication dosage totals:** `MedicationRequest.dosage.sum()`
- **Validation rules:** `Encounter.diagnosis.rank.max() = Encounter.diagnosis.count()`

**Estimated Effort:** 1 week

**Recommendation:** Implement in Phase 23 (pre-GA stabilization)

---

#### 2.2 Math Functions (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **MEDIUM-HIGH** - Required for calculated fields, BMI, dosage calculations
**Spec Section:** 5.7 Math

**Missing Functions:**
```fhirpath
abs() : Integer | Long | Decimal | Quantity
ceiling() : Integer | Quantity
floor() : Integer | Quantity
exp() : Decimal
ln() : Decimal
log(base : Decimal) : Decimal
power(exponent : Integer | Decimal) : Decimal
round([precision : Integer]) : Integer | Decimal | Quantity
sqrt() : Decimal
truncate() : Integer
```

**Use Cases:**
- **BMI calculation:** `(weight / power(height, 2)).round(1)`
- **Dose adjustments:** `baseDose * exp(ln(ratio) * days)`
- **Age calculations:** `today().year() - birthDate.year()`

**Estimated Effort:** 1.5 weeks

**Recommendation:** Implement in Phase 24 (advanced features)

---

#### 2.3 Date/Time Component Extraction (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **HIGH** - Commonly used in search parameters, age calculations, temporal queries
**Spec Section:** 5.9 Date/Time

**Missing Functions:**
```fhirpath
year() : Integer
month() : Integer
day() : Integer
hour() : Integer
minute() : Integer
second() : Integer
millisecond() : Integer
timezone() : String
```

**Use Cases:**
- **Age calculation:** `today().year() - Patient.birthDate.year()`
- **Temporal filtering:** `Observation.effective.year() = 2024`
- **Time-of-day rules:** `Appointment.start.hour() between 9 and 17`

**Estimated Effort:** 1 week

**Recommendation:** Implement in Phase 23 (essential for search parameters)

---

#### 2.4 Date/Time Interval Functions (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **HIGH** - Required for duration calculations, age, time-based rules
**Spec Section:** 5.9 Date/Time

**Missing Functions:**
```fhirpath
duration(precision : String) : Quantity
difference(precision : String) : Quantity
```

**Use Cases:**
- **Age calculation:** `today().difference(Patient.birthDate, 'years')`
- **Encounter duration:** `Encounter.period.end.difference(Encounter.period.start, 'hours')`
- **Medication timing:** `now().difference(MedicationRequest.authoredOn, 'days')`

**Estimated Effort:** 1.5 weeks (complex - timezone handling)

**Recommendation:** Implement in Phase 23 (critical for temporal queries)

---

#### 2.5 String Functions - Extended (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **MEDIUM** - Useful for data quality, parsing, formatting
**Spec Section:** 5.4 String Manipulation

**Missing Functions:**
```fhirpath
lastIndexOf(substring : String) : Integer
trim() : String
split(separator: String) : collection
encode(format : String) : String      // base64, url
decode(format : String) : String      // base64, url
escape(target : String) : String      // html, json
unescape(target : String) : String    // html, json
matchesFull(regex : String, [flags : String]) : Boolean
```

**Use Cases:**
- **Name parsing:** `Patient.name.text.split(' ')`
- **Data cleanup:** `identifier.value.trim()`
- **URL encoding:** `searchParam.encode('url')`
- **Exact regex match:** `identifier.value.matchesFull('^[A-Z]{3}[0-9]{6}$')`

**Estimated Effort:** 1 week

**Recommendation:** Implement in Phase 24 (nice-to-have)

---

#### 2.6 Collection Sorting (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **MEDIUM-HIGH** - Common in UI display, report generation
**Spec Section:** 5.2.2 Projection and Selection

**Missing Function:**
```fhirpath
sort([keySelector: expression [asc | desc] [, ...]]) : collection
```

**Use Cases:**
- **Chronological order:** `Observation.sort(effective desc)`
- **Alphabetical:** `Patient.name.family.sort()`
- **Multi-key:** `Encounter.sort(period.start desc, priority asc)`

**Current Workaround:**
Application layer sorting (less efficient, breaks FHIRPath composability)

**Estimated Effort:** 1.5 weeks (multi-key sorting complexity)

**Recommendation:** Implement in Phase 24

---

#### 2.7 Coalesce Function (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **MEDIUM** - Useful for default values, null handling
**Spec Section:** 5.2.2 Projection and Selection

**Missing Function:**
```fhirpath
coalesce(value : collection, [value : collection, ...]) : collection
```

**Use Cases:**
- **Default values:** `Patient.name.family.coalesce('Unknown')`
- **Fallback chains:** `telecom.where(use='work').value.coalesce(telecom.where(use='home').value, 'No contact')`

**Current Workaround:**
Multiple `iif()` calls (verbose)

**Estimated Effort:** 3 days

**Recommendation:** Implement in Phase 24

---

#### 2.8 Long Literal Type (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **LOW-MEDIUM** - Required for large integers (timestamps, IDs)
**Spec Section:** 2.3.1 Literals

**Missing Feature:**
```fhirpath
// 64-bit integers with 'L' suffix
9223372036854775807L  // Range: -2^63 to 2^63-1
```

**Use Cases:**
- **Unix timestamps:** `1700000000000L` (milliseconds since epoch)
- **Large identifiers:** Database IDs exceeding 2^31

**Current Workaround:**
Use Decimal type (loses integer semantics)

**Estimated Effort:** 1 week (tokenizer + evaluator changes)

**Recommendation:** Implement in Phase 25 (post-GA refinement)

---

#### 2.9 Logical NOT Operator
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **MEDIUM** - Improves readability vs. boolean negation patterns
**Spec Section:** 3.1 Operators

**Missing Operator:**
```fhirpath
not expression  // Unary logical negation
```

**Current Workaround:**
```fhirpath
// Instead of: Patient.active.not()
Patient.active = false  // or
Patient.active.exists().not()  // function-based workaround
```

**Estimated Effort:** 3 days

**Recommendation:** Implement in Phase 24

---

#### 2.10 ConformsTo Function
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **LOW** - Advanced validation use cases
**Spec Section:** 5.11 Utility Functions

**Missing Function:**
```fhirpath
conformsTo(profile : String) : Boolean
```

**Use Cases:**
- **Profile validation:** `Patient.conformsTo('http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient')`
- **Conditional logic:** `iif(conformsTo('strict-profile'), strictRule, lenientRule)`

**Estimated Effort:** 2 weeks (requires StructureDefinition engine integration)

**Recommendation:** Implement in Phase 26 (validation enhancements)

---

#### 2.11 New Functions in FHIRPath 3.0.0-ballot

**Context:** FHIRPath 3.0.0-ballot introduces **33+ new functions** and improvements not in the stable v2.0.0 release. These are categorized below by priority.

**NEW High-Value Functions (NEW in ballot):**
```fhirpath
// Date/Time Component Extraction (NEW)
year() : Integer          // Extract year from date/datetime
month() : Integer         // Extract month (1-12)
day() : Integer           // Extract day of month
hour() : Integer          // Extract hour (0-23)
minute() : Integer        // Extract minute (0-59)
second() : Integer        // Extract second (0-59)
millisecond() : Integer   // Extract millisecond
timezone() : String       // Extract timezone offset

// Date/Time Interval Calculation (NEW)
duration(precision: String) : Quantity    // Duration with precision
difference(date, precision: String) : Quantity  // Time difference

// Collection Enhancements (NEW)
sort([keySelector: expression [asc | desc]]) : collection  // Sort with key
coalesce(values...) : collection    // First non-empty value
repeatAll(projection) : collection  // Recursive with intermediates

// Aggregate Helpers (EXPANDED in ballot)
sum() : Integer | Long | Decimal | Quantity
min() : Integer | Long | Decimal | Quantity | Date | DateTime | Time | String
max() : Integer | Long | Decimal | Quantity | Date | DateTime | Time | String
avg() : Decimal | Quantity
```

**NEW String Functions (NEW in ballot):**
```fhirpath
trim() : String                      // Remove whitespace
split(separator: String) : collection  // Split string
join(separator: String) : String    // Join collection
lastIndexOf(substring) : Integer    // Find last occurrence
encode(format: String) : String     // Base64/URL encode
decode(format: String) : String     // Base64/URL decode
escape(target: String) : String     // HTML/JSON escape
unescape(target: String) : String   // HTML/JSON unescape
matchesFull(regex, flags) : Boolean // Exact regex match
```

**NEW Type Support (NEW in ballot):**
```fhirpath
toLong() : Long                     // Convert to 64-bit integer
convertsToLong() : Boolean          // Test conversion
```

**Impact Summary:**
- **33+ new functions** not in v2.0.0 (all marked STU in ballot)
- Majority are **common use cases** (sorting, date components, string utilities)
- Some are **domain-specific** (ConformsTo for FHIR validation)
- Implementation these enhances search, display, and validation capabilities

**Estimated Effort for All:** ~13-18 weeks (covered in Roadmap section)

---

### 3. Medium Priority Gaps (🟢)

#### 3.1 RepeatAll Function (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **LOW-MEDIUM** - Advanced recursive traversal
**Spec Section:** 5.2.2 Projection and Selection

**Missing Function:**
```fhirpath
repeatAll(projection: expression) : collection
```

**Difference from `repeat()`:**
- `repeat()`: Follows until stable (no new items)
- `repeatAll()`: Includes all intermediate results

**Use Cases:**
- **Full hierarchy:** `Observation.repeatAll(hasMember.resolve())`
- **Transitive closure:** `Organization.repeatAll(partOf.resolve())`

**Estimated Effort:** 1 week

**Recommendation:** Implement in Phase 25

---

#### 3.2 DefineVariable Function (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **LOW** - Advanced expression composition
**Spec Section:** 5.11 Utility Functions

**Missing Function:**
```fhirpath
defineVariable(name : String, value : collection) : collection
```

**Use Cases:**
- **Complex expressions:** `defineVariable('age', today().year() - birthDate.year()).where(%age > 65)`

**Current Workaround:**
External variable binding via `EvaluationContext`

**Estimated Effort:** 1.5 weeks

**Recommendation:** Implement in Phase 26

---

#### 3.3 Precision Function (STU)
**Status:** ❌ NOT IMPLEMENTED
**Impact:** **LOW** - Metadata extraction for dates/quantities
**Spec Section:** 5.11 Utility Functions

**Missing Function:**
```fhirpath
precision() : Integer
```

**Use Cases:**
- **Date precision:** `@2024.precision() = 4` (year only)
- **Decimal precision:** `3.14159.precision() = 5`

**Estimated Effort:** 3 days

**Recommendation:** Implement in Phase 25

---

#### 3.4 Combine with PreserveOrder (STU)
**Status:** ⚠️ PARTIAL - Missing optional parameter
**Impact:** **LOW** - Advanced collection operations
**Spec Section:** 5.3 Collections

**Current Implementation:**
```csharp
combine(other : collection) : collection
```

**Missing:**
```fhirpath
combine(other : collection, preserveOrder : Boolean) : collection
```

**Estimated Effort:** 2 days (parameter addition)

**Recommendation:** Implement in Phase 25

---

### 4. Low Priority Gaps (⚪)

These are advanced/rare features with minimal impact on typical FHIR operations:

1. **lowBoundary() / highBoundary()** - Currently implemented but marked STU in spec
2. **Advanced regex flags** in `matches()` and `replaceMatches()` - May need verification
3. **Calendar duration arithmetic** - Part of Quantity evaluation (covered above)
4. **Type system edge cases** - Implicit/explicit conversions may need spec alignment

---

## Implementation Roadmap

⚠️ **Ballot Status Disclaimer:**
This roadmap targets **FHIRPath 3.0.0-ballot** features. Note that:
- **Ballot spec is NOT finalized** - Features may change before official release
- **v2.0.0 normative features** are stable and should remain unchanged
- **Recommend feature flags** for ballot-only features (in case of spec changes)
- **Monitor HL7 voting** on the ballot for finalization timeline

See [FHIRPath Specification URLs](#9-specification-urls) for tracking ballot status.

### Phase 23: Critical Gaps & High-Value Features (5-6 weeks)
**Target:** Pre-GA feature completeness, prioritize blocking issues

1. **Week 1-3:** Quantity Literal Evaluation (🔴 Critical)
   - UCUM library integration
   - Arithmetic and comparison operators
   - Calendar duration support
   - Unit tests with FHIR Observation examples
   - **Reason:** Blocks FHIR Observation, vital signs, medication dosages

2. **Week 4:** Aggregate Functions (🟡 High Priority)
   - `sum()`, `min()`, `max()`, `avg()` (simpler than generic `aggregate()`)
   - Type handling (Integer/Long/Decimal/Quantity)
   - Unit tests with FHIR analytics examples
   - **Reason:** Higher ROI than generic aggregate, commonly used in validation

3. **Week 5-6:** Date/Time Component Extraction (🟡 High)
   - `year()`, `month()`, `day()`, `hour()`, `minute()`, `second()`, `millisecond()`, `timezone()`
   - Integration with existing Date/DateTime/Time types
   - Unit tests with age/temporal filtering examples
   - **Reason:** Essential for search parameters and temporal queries

### Phase 24: Date/Time Intervals & Extended Features (4-5 weeks)
**Target:** Temporal query support and developer experience

1. **Week 1-2:** Date/Time Interval Functions (🟡 High)
   - `duration()`, `difference()` (moved up from Phase 23)
   - Timezone-aware calculations
   - Unit tests with age calculation examples
   - **Reason:** Paired with date components for complete temporal support

2. **Week 3:** Math Functions (🟡 High)
   - All 10 math functions (abs, ceiling, floor, exp, ln, log, power, round, sqrt, truncate)
   - Type coercion rules
   - Unit tests with BMI, dosage calculations

3. **Week 4-5:** String Functions Extended & Collection Utilities (🟡 High)
   - `trim()`, `split()`, `join()`, `lastIndexOf()`
   - `encode()`, `decode()`, `escape()`, `unescape()`
   - `coalesce()` function
   - `sort([keySelector])` with multi-key support
   - `not` unary operator
   - Unit tests

### Phase 25: STU Completeness (3-4 weeks)
**Target:** Full STU feature parity

1. **Week 1:** Long Literal Type (🟡 Medium)
   - Tokenizer changes (`L` suffix)
   - Evaluator type handling
   - Unit tests

2. **Week 2:** Advanced Collection Functions (🟢 Medium)
   - `repeatAll()`
   - `combine()` with `preserveOrder`
   - Unit tests

3. **Week 3:** Utility Functions (🟢 Medium)
   - `precision()`
   - Unit tests

4. **Week 4:** DefineVariable (🟢 Medium)
   - Variable scoping
   - Integration with existing `EvaluationContext`
   - Unit tests

### Phase 26: Advanced Validation (2-3 weeks)
**Target:** Enterprise-grade validation

1. **Week 1-2:** ConformsTo Function (🟡 Medium)
   - StructureDefinition integration
   - Profile resolution
   - Unit tests

2. **Week 3:** Validation refinements
   - Edge case handling
   - Performance optimization

---

## Testing Strategy

### 1. Official FHIRPath Test Suite
**Recommendation:** Integrate HL7's official FHIRPath test suite (if available)
- Download from: https://github.com/HL7/FHIRPath
- Run as part of CI/CD pipeline
- Track compliance percentage

### 2. FHIR-Specific Test Cases
**Recommendation:** Create domain-specific tests using real FHIR resources
- Patient demographics (name, age, contact)
- Observation vital signs (quantities, ranges)
- Medication dosages (quantity calculations)
- Encounter timelines (date/time intervals)

### 3. Performance Benchmarks
**Recommendation:** Ensure new features don't degrade performance
- Baseline: Current compiled path performance (~180-250ns)
- Target: New features within 2x of baseline
- Use BenchmarkDotNet for measurement

### 4. Backwards Compatibility
**Recommendation:** Ensure existing expressions continue to work
- Regression test suite (all 6,354 existing test lines)
- No breaking changes to public APIs
- Deprecation path for any changes

---

## Risk Assessment

### High Risk Items

1. **Quantity Evaluation (🔴)**
   - **Risk:** Complex UCUM unit conversion, calendar duration semantics
   - **Mitigation:** Use proven library (e.g., UnitsNet or Fhir.Metrics), extensive unit tests
   - **Fallback:** Limit to basic units initially, expand incrementally

2. **Date/Time Intervals (🟡)**
   - **Risk:** Timezone edge cases, daylight saving time, calendar math
   - **Mitigation:** Use NodaTime library, reference implementation alignment
   - **Fallback:** Document known limitations, warn on ambiguous cases

3. **ConformsTo (🟡)**
   - **Risk:** Requires StructureDefinition resolution, complex dependency
   - **Mitigation:** Leverage existing validation infrastructure, limit to basic profiles
   - **Fallback:** Defer to Phase 27+ if too complex

### Low Risk Items

- Aggregate functions (standard LINQ operations)
- Math functions (standard .NET Math library)
- String functions (standard .NET String methods)
- Collection sorting (standard LINQ OrderBy/ThenBy)

---

## Recommendations Summary

### Immediate Actions (Phase 23)
1. **Implement Quantity Literal Evaluation** - Blocks FHIR Observation usage
2. **Implement Date/Time Functions** - Essential for search parameters
3. **Implement Aggregate Functions** - Common in validation rules

### Short-term Actions (Phase 24)
4. Implement Math Functions - Calculated fields, BMI, dosages
5. Implement String Extended Functions - Data quality, parsing
6. Implement Collection Sorting - UI display, reporting
7. Implement Coalesce & NOT - Developer experience

### Long-term Actions (Phase 25-26)
8. Implement Long Literal Type - Large identifiers, timestamps
9. Implement Advanced Collection Functions - Recursive traversal
10. Implement Utility Functions - Metadata extraction
11. Implement ConformsTo - Enterprise validation

### Quality Assurance
- Integrate HL7 official FHIRPath test suite
- Create FHIR-specific test cases
- Performance benchmarking
- Backwards compatibility verification

---

## Specification Reference URLs

**Official FHIRPath Versions**:
- **v2.0.0 Normative** (Stable): https://hl7.org/fhirpath/N1/
- **v3.0.0-ballot** (Continuous Build): https://build.fhir.org/ig/HL7/FHIRPath/
- **Version History**: https://hl7.org/fhirpath/history.html

**Development Resources**:
- **HL7 FHIRPath Repository**: https://github.com/HL7/FHIRPath
- **FHIRPath Test Suite**: https://github.com/HL7/FHIRPath/tree/master/tests
- **Reference Implementations**: https://confluence.hl7.org/display/FHIRI/FHIRPath+Implementations

**Track Ballot Progress**:
- **HL7 Ballot Tracker**: https://www.hl7.org/ballot/
- **Current Status**: Check for "FHIRPath" ballot items for finalization status

---

## Conclusion

The Ignixa.FhirPath implementation provides **excellent coverage of FHIRPath v2.0.0 normative features** (98%, 64 implemented functions), making it suitable for **production FHIR server operations**. However, to achieve **full FHIRPath 3.0.0-ballot compliance**, approximately **13-18 weeks of development** across 4 phases is recommended.

**Key Findings from Spec Alignment Review:**
- ✅ FHIRPath v2.0.0 is stable (Normative, ANSI-certified)
- ⚠️ FHIRPath v3.0.0-ballot adds 33+ new functions (not yet finalized)
- 🔴 **Critical blocking gap**: Quantity Literal Evaluation (affects Observations, vital signs, medications)
- 🟡 **High-value gaps**: Aggregate helpers (sum/min/max/avg), Date/Time components, Date/Time intervals

**Reordered Implementation Strategy:**
With updated prioritization, Phase 23 focuses on:
1. **Quantity Literal Evaluation** (Weeks 1-3) - Unblocks FHIR Observation support
2. **Aggregate Helpers** (Week 4) - Higher ROI than generic aggregate()
3. **Date/Time Components** (Weeks 5-6) - Essential for temporal queries

With the proposed roadmap, Ignixa.FhirPath can achieve:
- **100% v2.0.0 Normative compliance** by end of Phase 23
- **90%+ v3.0.0-ballot STU compliance** by end of Phase 25
- **Enterprise-grade validation** by end of Phase 26

⚠️ **Important:** Monitor FHIRPath 3.0.0 ballot status for finalization. Implement ballot features with feature flags to accommodate potential spec changes before official release.

**Total Estimated Effort:** 13-18 weeks across 4 development phases.

---

## Appendix: Function Coverage Matrix

| Function Category | Spec Count | Implemented | Missing | Coverage |
|-------------------|------------|-------------|---------|----------|
| Existence | 12 | 12 | 0 | 100% |
| Filtering & Projection | 8 | 5 | 3 | 63% |
| Subsetting | 9 | 9 | 0 | 100% |
| Combining | 4 | 3 | 1 | 75% |
| Boolean Logic | 6 | 6 | 0 | 100% |
| String Manipulation | 19 | 11 | 8 | 58% |
| Math | 10 | 0 | 10 | 0% |
| Conversion | 18 | 16 | 2 | 89% |
| Date/Time | 10 | 2 | 8 | 20% |
| Aggregates | 5 | 0 | 5 | 0% |
| Tree Navigation | 2 | 2 | 0 | 100% |
| Utility | 8 | 3 | 5 | 38% |
| FHIR-Specific | 4 | 3 | 1 | 75% |
| **TOTAL** | **115** | **64** | **51** | **56%** |

---

**END OF REPORT**
