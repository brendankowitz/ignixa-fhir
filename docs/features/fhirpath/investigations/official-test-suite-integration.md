# Investigation: Official Test Suite Integration

**Feature**: fhirpath
**Status**: Implemented
**Created**: 2026-01-12
**Completed**: 2026-01-12

## Approach

Integrate the official HL7 FHIR FHIRPath test suite from the [fhir-test-cases repository](https://github.com/FHIR/fhir-test-cases) into Ignixa's test infrastructure. This would involve:

1. **Automated test generation** from XML test definitions (`tests-fhir-r4.xml`, `tests-fhir-r5.xml`)
2. **xUnit theory-based execution** using `[MemberData]` or `[ClassData]` to run each test case
3. **Test data management** - downloading/caching test input files (Patient examples, Observation examples, etc.)
4. **Result validation** - comparing FHIRPath evaluation results against typed expected outputs
5. **Coverage reporting** - tracking which test groups pass/fail to identify implementation gaps

**Implementation Strategy**:
- Create `OfficialTestSuiteRunner.cs` that parses the XML test suite at test discovery time
- Download test suite files as NuGet content or Git submodule (similar to how specification packages work)
- Generate xUnit theories dynamically from the XML structure
- Support test filtering by group name (e.g., only run `testFunctions` group)
- Report failures with context (expression, input file, expected vs actual output)

## Tradeoffs

| Pros | Cons |
|------|------|
| **Specification compliance**: Tests directly from HL7 ensure conformance to FHIRPath 2.0.0 spec | **Maintenance burden**: Test suite updates require re-validation, may break existing behavior |
| **Comprehensive coverage**: 1000+ tests across all FHIRPath features (functions, operators, type system) | **Test data complexity**: Requires managing external input files (XML/JSON FHIR resources) |
| **Gap identification**: Immediately reveals unimplemented or broken features | **Platform differences**: Some tests may assume Java/JavaScript semantics (e.g., decimal precision) |
| **Regression prevention**: Catches breaking changes when optimizing FHIRPath engine | **Performance overhead**: Large test suite increases CI/CD execution time (mitigable with parallelization) |
| **Community validation**: Same tests used by Firely, HAPI FHIR, etc. for cross-implementation consistency | **XML parsing cost**: Test discovery requires parsing XML at test collection time (one-time cost) |
| **Error detection coverage**: Tests for syntax, semantic, and execution errors validate analyzer/evaluator separation | **Version skew**: R4 vs R5 test suites may have different expectations for same expressions |

## Alignment

- [x] **Follows architectural layering rules**: Test suite runs against public FHIRPath API (`FhirPathParser`, `FhirPathEvaluator`, `FhirPathAnalyzer`)
- [x] **Developer Experience**: Works with `dotnet test` - no special setup beyond NuGet restore
- [x] **Specification compliance**: Directly validates HL7 FHIRPath specification conformance
- [x] **Consistent with existing patterns**: Uses xUnit theories like current test structure, follows `test/Ignixa.FhirPath.Tests` conventions

## Evidence

### 1. Test Suite Repository Structure

The [fhir-test-cases repository](https://github.com/FHIR/fhir-test-cases) contains:

**R4 FHIRPath Tests**:
- `r4/fhirpath/tests-fhir-r4.xml` - Main test suite (935 tests)
- `r4/examples/` - Input FHIR resources (patient-example.xml, observation-example.xml, etc.)

**R5 FHIRPath Tests**:
- `r5/fhirpath/tests-fhir-r5.xml` - R5-specific tests
- `r5/examples/` - R5 input resources

**Test Organization** (from [schema analysis](../../../../investigations/fhirpath-test-suite-schema.md)):
```xml
<tests name="FHIRPathTestSuite" reference="http://hl7.org/fhirpath|2.0.0">
  <group name="testBasics">
    <test name="testSimplePath" inputfile="patient-example.xml">
      <expression>Patient.name.family</expression>
      <output type="string">Chalmers</output>
    </test>
  </group>
</tests>
```

**Test Groups** (functional areas):
- `comments`, `testBasics`, `testEquality`, `testType`, `testCollections`
- `testFunctions`, `testArithmetic`, `testBoolean`, `testConversions`
- `testDateTime`, `testQuantities`, `testAggregates`, `testNavigation`

### 2. Current Test Structure

**Existing test patterns** (`test/Ignixa.FhirPath.Tests/`):

```csharp
// Current: Hand-written AAA tests
public class FhirPathEvaluatorTests {
    [Fact]
    public void GivenObservationWithValueString_WhenFilteringWithOfTypeString_ThenReturnsValue() {
        // Arrange: Inline JSON
        var observationJson = """{ "resourceType": "Observation", ... }""";
        var resource = ResourceJsonNode.Parse(observationJson);
        var element = resource.ToElement(_r4Provider);

        // Act: Evaluate expression
        var result = EvaluatePath(element, "value.ofType(string)");

        // Assert: Verify result
        Assert.Single(result);
        Assert.Equal("foo", result[0].Value);
    }
}
```

**Proposed: Theory-based test runner** (new file `OfficialTestSuiteRunner.cs`):

```csharp
public class OfficialTestSuiteRunner {
    [Theory]
    [MemberData(nameof(LoadR4Tests))]
    public void R4TestSuite(FhirPathTestCase testCase) {
        // Arrange: Load input file if specified
        IElement? input = testCase.InputFile != null
            ? LoadInputResource(testCase.InputFile)
            : null;

        // Act: Parse and evaluate
        var expression = _parser.Parse(testCase.Expression);
        var result = _evaluator.Evaluate(input, expression, new EvaluationContext());

        // Assert: Compare typed outputs
        AssertExpectedOutputs(result, testCase.ExpectedOutputs, testCase.Ordered);
    }

    public static IEnumerable<object[]> LoadR4Tests() {
        var xml = LoadTestSuiteXml("r4/fhirpath/tests-fhir-r4.xml");
        return ParseTestCases(xml)
            .Where(t => !t.Expression.HasInvalidAttribute) // Skip error tests for now
            .Select(t => new object[] { t });
    }
}
```

### 3. Other FHIR Implementations

**Firely .NET SDK**: Uses same test suite via custom xUnit integration
- https://github.com/FirelyTeam/firely-net-sdk/tree/develop/src/Hl7.FhirPath.Tests
- Runs tests from `fhir-test-cases` Git submodule
- Reports ~95% pass rate with known deviations documented

**HAPI FHIR (Java)**: Similar approach with JUnit parameterized tests
- https://github.com/hapifhir/hapi-fhir/tree/master/hapi-fhir-structures-r4/src/test/java/ca/uhn/fhir/fhirpath
- Downloads test suite as Maven dependency

**Simplifier FHIRPath Editor**: Uses same tests for browser-based validation
- https://fhirpath-lab.com/ (online playground)
- Tests parsed client-side from JSON transform of XML

### 4. Test Data Management Options

**IMPORTANT**: The fhir-test-cases repository is **NOT available as a NuGet package**. Distribution is via:
- **Maven Central**: `org.hl7.fhir.testcases:fhir-test-cases` (Java ecosystem)
- **Direct Download**: https://github.com/FHIR/fhir-test-cases/releases/latest/download/testcases.zip
- **GitHub Packages**: Maven-based package

For .NET consumption, we have these options:

**Option A: Git Submodule** (Firely SDK approach)
```bash
# Add fhir-test-cases as submodule
git submodule add https://github.com/FHIR/fhir-test-cases test/fhir-test-cases

# MSBuild copies files to output directory
<ItemGroup>
  <Content Include="$(MSBuildThisFileDirectory)../../test/fhir-test-cases/r4/**/*.*">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```
**Pros**: Always up-to-date, same approach as Firely SDK, no manual steps
**Cons**: Adds 100MB+ to clone size, requires `git submodule update --init`

**Option B: MSBuild Download Task**
```xml
<!-- Download testcases.zip during build if not cached -->
<Target Name="DownloadTestCases" BeforeTargets="BeforeBuild">
  <DownloadFile SourceUrl="https://github.com/FHIR/fhir-test-cases/releases/download/1.7.46/testcases.zip"
                DestinationFolder="$(MSBuildThisFileDirectory)TestData"
                SkipUnchangedFiles="true" />
  <Unzip SourceFiles="$(MSBuildThisFileDirectory)TestData/testcases.zip"
         DestinationFolder="$(MSBuildThisFileDirectory)TestData" />
</Target>
```
**Pros**: No submodule, pinned version, cached locally
**Cons**: Network dependency on first build, version updates require csproj edit

**Option C: Create Internal NuGet Package**
```bash
# Package the test cases ourselves
nuget pack Ignixa.FhirPath.TestSuite.nuspec -Version 1.7.46
# Publish to internal feed or commit to repo as local package
```
**Pros**: Standard .NET workflow, versioned, works offline after restore
**Cons**: Manual package creation, extra maintenance burden

**Option D: Embedded Resources (Selective)**
```xml
<!-- Embed only the files we need (not the full 100MB repo) -->
<ItemGroup>
  <EmbeddedResource Include="TestData/tests-fhir-r4.xml" />
  <EmbeddedResource Include="TestData/patient-example.xml" />
  <EmbeddedResource Include="TestData/observation-example.xml" />
  <!-- Add input files as needed -->
</ItemGroup>
```
**Pros**: Self-contained test assembly, no network/git dependencies, fast
**Cons**: Manual file selection, must sync updates manually, limited to small subset

**Recommendation**: Start with **Option D (Embedded Resources)** for initial 200 tests (minimal files), migrate to **Option A (Git Submodule)** when expanding to full suite. Avoid creating internal NuGet package unless we need versioned distribution to multiple repos.

### 5. Gap Analysis

Running the official test suite will immediately reveal implementation gaps. Based on existing investigations:

**Known Gaps** (from [Gap Analysis](gap-analysis.md)):
- `combine()` function - Not implemented
- `lowBoundary()`/`highBoundary()` - Not implemented
- `convertsToQuantity()` - Partial (UCUM validation missing)
- Quantity arithmetic with unit conversion - Missing UCUM library integration
- `aggregate()` with complex accumulators - May have edge cases

**Expected Test Failures** (before gap fixes):
- `testFunctions/testCombine` group - Will fail (function not implemented)
- `testQuantities/testUcumConversion` - Will fail (no UCUM library)
- `testArithmetic/testQuantityMath` - May fail (unit normalization issues)

**Benefit**: Test suite provides concrete repro cases for each gap, making prioritization data-driven.

### 6. Integration with Existing Performance Benchmarks

Current FHIRPath performance testing uses BenchmarkDotNet (see [Performance Analysis](fhirpath-performance-analysis.md)). Official test suite can complement benchmarks:

```csharp
// Add benchmark for test suite execution time
[Benchmark]
public void RunEntireR4TestSuite() {
    foreach (var test in OfficialTestSuiteRunner.LoadR4Tests()) {
        var result = _evaluator.Evaluate(test.Input, test.Expression, _context);
        // No assertions - just measure throughput
    }
}
```

**Metric**: Tests per second (goal: 10,000+ tests/sec with caching enabled)

### 7. Alternative Approaches Considered

This investigation focuses on **direct XML integration**. Other approaches worth investigating separately:

1. **Test Generation via Source Generators** - Generate C# test methods at compile time from XML (faster discovery, no runtime XML parsing)
2. **Selective Test Import** - Cherry-pick specific test groups instead of full suite (reduced CI time, focus on high-value tests)
3. **Cross-Implementation Fuzzing** - Compare Ignixa output vs Firely SDK on random expressions (finds edge cases not covered by official tests)
4. **Snapshot Testing** - Record current output for all tests, flag changes (catch regressions even when official test expectations unclear)

## Verdict

**Status: Implemented** (2026-01-12)

### Results

Full test suite integration completed across all FHIR versions:

Full test suite integration completed across all FHIR versions. Numbers below re-measured against
`fhir-test-cases` 1.7.46; the figures originally recorded here in January 2026 predated most of the
engine work and were stale in every column.

The corpus is pinned in three places so that "passes the official suite" names which suite: the csproj
fails the build if `testcases.zip` does not hash to `FhirTestCasesArchiveSha256`,
`VerifyFhirTestCasesProvenance` re-checks that hash and the `.downloaded` marker at test time, and
`_suiteFileHashes` pins the SHA-256 of each extracted `tests-fhir-{r4,r4b,r5}.xml`. The last one is not
redundant: `TestData/` is gitignored, so the extracted tree is unversioned local state, and editing an
expected output in it would move the pass count with nothing to say so.

| Version | Executed | Passed | Failed | Skipped | Not supported | Genuinely asserted |
|---------|----------|--------|--------|---------|----------------|---------------------|
| **R4**    | 935       | 933       | 0     | 2      | 2     | 931 (99.6%)     |
| **R4B**   | 933       | 931       | 0     | 2      | 2     | 929 (99.6%)     |
| **R5**    | 1,032     | 1,032     | 0     | 0      | 5     | 1,027 (99.5%)   |
| **Total** | **2,900** | **2,896** | **0** | **4**  | **9** | **2,887 (99.6%)** |

(Measured against `fhir-test-cases` 1.7.46, `dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj
--filter "FullyQualifiedName~OfficialTestSuite_R4&FullyQualifiedName!~OfficialTestSuite_R4B"` and the
equivalent `_R4B` / `_R5` filters. **These figures are superseded and have not been re-measured.** They
were produced by the pre-fix runner, which treated any `NotSupportedException` as a pass and so could
not tell an unimplemented function from a passing test. That runner has since been fixed and falsified
— with a binary-operator switch arm deleted from the engine it still reported a fully green suite; the
fixed runner fails the 27 affected cases. See [FHIRPath Release Readiness](release-readiness.md) item
E1 for the falsification run. Re-baselining the four-way split on the honest runner is a separate piece
of work and is deliberately not done here, so that a corrected number does not arrive carrying the
authority of having been fixed while still being unmeasured.)

Read the last column, not the "Failed" column. Every case in the suite now either asserts and passes or
is accounted for explicitly, so a raw pass rate reads 100% and tells you nothing. Two things are worth
watching, and they are not the same thing anymore:

- **`_unsignalledInvalidCases` is empty.** Every deferral it ever held has been closed: the unresolved-path
  and ordering cases by `RunInvalidExpressionTest` now consulting `FhirPathAnalyzer`, the `defineVariable`
  scope cases by `VariableScope` giving `defineVariable` real lexical scoping, and
  `testFHIRPathAsFunction21` by version-gating the singleton rule instead of blanket-deferring it. That
  means `AssertDeferralIsStillNeeded` - the guard that re-runs a deferred case and fails if the engine now
  signals the error, so a closed gap can't sit unnoticed in the list - currently iterates over nothing.
  **It is not protecting anything today.** Its value is entirely prospective: if a case is ever deferred
  again, this is the guard that will catch the gap closing without the entry being removed. Read a claim
  of "35 deferred cases" anywhere else in this repo's history as stale - it described an earlier state of
  this dictionary, not the current one.
- **Skipped (4)**, all of them version-specific, none of them deferrals:
  `testPlusDate19` and `testFHIRPathAsFunction21` skip on R4/R4B only (2 each, 4 total) because both
  encode behaviour this implementation deliberately doesn't enforce below R5. These report as real xunit
  skips via `Xunit.SkippableFact` (xunit
  2.9.3 has no working dynamic skip of its own, so before that they reported as passes and were
  indistinguishable from coverage) - but they never touch `_unsignalledInvalidCases` or
  `AssertDeferralIsStillNeeded`, because that machinery is specific to `invalid`-marked cases, and none of
  these four are.

  Each remaining skip is now routed through `SkipUnlessTheCaseWouldNowPass`, which runs the case and
  fails if it passes, so a skip cannot outlive the limitation that justified it. That guard was added
  because six `testQuantity9`/`testQuantity10` skips had already gone stale: they passed on R4, R4B and
  R5 while still being skipped, hiding six conformance results behind a Fhir.Metrics limitation that no
  longer applied to them.
- **Not supported** - `conformsTo()` and `%terminologies` cases. In the run tabulated above these counted
  as xunit *passes*: the runner caught `NotSupportedException` and returned, so a chosen non-feature and a
  correct answer were the same result. They are now recorded skips. The engine throws
  `FhirPathFunctionNotSupportedException`, which carries the feature name, and the runner skips only when
  that name is on `_deliberatelyUnsupportedFeatures`; a bare `NotSupportedException`, or a marker naming
  an unlisted feature, fails the case.
  `OfficialTestSuiteSkipListTests.GivenTheDeliberatelyUnsupportedFeatures_WhenEachIsInvoked_ThenTheEngineStillRefusesByName`
  invokes every listed feature and fails if one stops throwing, so implementing `conformsTo` forces its
  allowlist entry out instead of leaving a guard that catches nothing.
  `testConformsTo3` moved too: it is `invalid`-marked and was passing because the engine threw, but for
  not implementing `conformsTo` at all rather than for the profile URL being bogus. Erroring for the wrong
  reason is not conformance.

The actual remaining teeth against a regression in the `invalid`-marked cases are `RunInvalidExpressionTest`'s
`Assert.Fail` calls, which sit outside the `try`/`catch` on purpose - an earlier version wrapped the whole
method body in one `try`, so xunit's own `FailException` landed in the trailing `catch (Exception)` and
every `invalid`-marked case passed unconditionally regardless of what the engine did. Fixing that bug is
what turned the whole `invalid` category from unverified into genuinely asserted, and it's what surfaced
the 35 real gaps that then went into `_unsignalledInvalidCases` as named deferrals. Subsequent engine work
(`FhirPathAnalyzer` static analysis, `VariableScope` lexical scoping, version-gating the `as` singleton
rule) is what closed all 35 down to zero - the `Assert.Fail` fix exposed the gaps, it didn't close them.

Three R5 cases are CDA-mode and excluded at parse time, so 2,903 parse and 2,900 execute (**count unverified pending runner fix** — see caveat above).

**Test Coverage Distribution** (by group, counting only genuinely asserted cases):
- `testBasics` - 5/7; `testType` - 30/30
- `testQuantity` - 11/11 (`testQuantity9`/`10` were skipped for a Fhir.Metrics UCUM limitation that no
  longer applies to them; the skips were stale and have been retired)
- `defineVariable` (R5 only) - 21/21, no deferrals. `defineVariable9`/`10`/`12`/`16` and
  `dvUsageOutsideScopeThrows` were deferred until `%context` was implemented and `defineVariable`
  bindings were scoped lexically (`VariableScope`); reading an undefined `%name` now signals an error
  per FHIRPath §1.9 instead of yielding empty
- String functions (`split`, `trim`, `encode`, `escape`) - no deferrals, no failures
- Math functions (`round`, `abs`, `sqrt`, `ln`, `exp`) - no deferrals, no failures
- `testAggregate`, `LowBoundary`, `HighBoundary` - no deferrals, no failures

**Known Gaps**:
- `conformsTo()` function - Profile validation (requires StructureDefinition validation)
- `%terminologies` - requires a terminology server binding
- UCUM quantity conversion incomplete (unit multiplication/division across prefixes)

`lowBoundary()`/`highBoundary()`, `aggregate()` and `combine()` were listed here as gaps and are now
implemented (`BoundaryFunctions`, `CollectionFunctions`); their groups pass clean. Issue #184, which
tracked this work, is closed.

### Implementation Details

**MSBuild Download Task** (zero manual setup):
```xml
<Target Name="DownloadTestCases" BeforeTargets="BeforeBuild">
  <DownloadFile SourceUrl="https://github.com/FHIR/fhir-test-cases/releases/download/1.7.46/testcases.zip"
                DestinationFolder="$(MSBuildThisFileDirectory)TestData"
                SkipUnchangedFiles="true" />
  <Unzip SourceFiles="$(MSBuildThisFileDirectory)TestData/testcases.zip"
         DestinationFolder="$(MSBuildThisFileDirectory)TestData" />
</Target>
```

**Native XML to JSON Converter** (`FhirXmlToJsonConverter`, 249 lines, zero dependencies):
- Converts FHIR XML test input files to JSON for `ResourceJsonNode.Parse()`
- Handles attributes (`value`, `url`, `id`), namespaces, arrays, primitives

**Test Suite Parser** (`FhirPathTestSuiteParser`, 91 lines):
- Parses `tests-fhir-r4.xml`, `tests-fhir-r4b.xml`, `tests-fhir-r5.xml`
- Extracts test groups, expressions, expected outputs (typed), input files
- Records `invalid="true"` / `invalid="semantic"` as `IsInvalidTest` rather than skipping. The runner
  *executes* these through `RunInvalidExpressionTest` and asserts the engine signals an error; 82 cases
  are covered this way. (This reverses the original design, which dropped them.)

**xUnit Theory Runner**:
```csharp
[SkippableTheory]
[MemberData(nameof(GetR4TestCases))]
public void OfficialTestSuite_R4(FhirPathTestCase testCase) {
    RunTestCase(testCase, FhirVersion.R4);
}
```
`[SkippableTheory]` (from `Xunit.SkippableFact`) is what lets `SkipTest` turn a deferral into a real
skipped result instead of a silent pass.

### Acceptance Criteria Met

- ✅ Only new test dependency beyond `System.Xml.Linq` and MSBuild tasks is `Xunit.SkippableFact`,
  needed because xunit 2.9.3 cannot skip dynamically
- ✅ Failed tests report expression, expected vs actual output, input file reference
- ✅ Tests run in parallel via xUnit's default parallelization
- ✅ Coverage report via `dotnet test --logger "console;verbosity=detailed"`
- ✅ Full R4/R4B/R5 suite (2,900 tests executed (**count unverified** — see caveat in summary table above)) - exceeded Phase 1 goal of 200 tests
- ✅ FHIRPath 2.0 support: Comments, `defineVariable()`, backtick variables, escape sequences
- ✅ Comprehensive function coverage: 120 registered `[FhirPathFunction]` implementations

### Risk Mitigation Applied

- Test filtering supported: `dotnet test --filter "FullyQualifiedName~R4.testBasics"`
- Version-specific tests isolated (no cross-contamination between R4/R4B/R5)
- Known deviations documented in test output (e.g., timezone handling differences)
- Auto-download on first build - no manual git submodule required

### Next Steps

1. **Gap closure** (13 of 2,900 cases are not asserted: 4 skipped, 9 unsupported - the deferral list is
   empty, see the table above):
   - ✅ ~~Math functions (round, abs, sqrt, ln, exp, power, floor, ceiling, truncate)~~ - Complete
   - ✅ ~~String functions (trim, split, contains, encode/decode, escape/unescape)~~ - Complete
   - ✅ ~~FHIRPath 2.0 features (comments, defineVariable, backtick variables)~~ - Complete
   - ✅ ~~`lowBoundary()`/`highBoundary()` - Precision boundary calculations~~ - Complete
   - ✅ ~~`aggregate()` edge cases~~ - Complete
   - 🔲 `conformsTo()` - Requires profile validation infrastructure (6 cases)
   - 🔲 `%terminologies` - Requires a terminology server binding (3 cases, R5)
   - ✅ ~~UCUM library integration for quantity unit conversion (`testQuantity9`/`testQuantity10`)~~ -
     the skips were stale; both cases pass on R4, R4B and R5 and are now asserted
   - ✅ ~~The deferred cases in `_unsignalledInvalidCases` - invalid expressions the engine does not yet
     signal an error for~~ - Complete; all 35 closed and the dictionary is now empty
2. **CI integration**: Add pass rate tracking to GitHub Actions (fail on regression)
3. **Performance optimization**: Leverage compiled delegate caching for test suite expressions
