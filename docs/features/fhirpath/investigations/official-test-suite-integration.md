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

Full test suite integration is complete across all FHIR versions, and the counts below are the
first conformance figures this project has published from a runner that can fail.

**Measured 2026-08-24** at `8183b284` on branch `runner-honesty`, `net10.0`, against
`fhir-test-cases` **1.7.46**.

#### What makes this figure different from the three it replaces

Three numbers were previously in circulation — 2,881, 2,887 and "2906 of 2906" — and all three came
from the same runner. Two things had to be true before any of them could be replaced:

1. **The runner can fail, and CI keeps it that way.** Until `107480e5` the runner caught
   `NotSupportedException` and `return`ed, which xunit records as a *pass*. The catch was scoped by
   exception type, not by the six functions its comment named, so an unimplemented binary operator
   was laundered into green. Demonstrated, not asserted: with the `xor` arm deleted from
   `FhirPathEvaluator`'s binary-operator switch, the pre-fix runner reported
   `Failed: 0, Passed: 2902` — a fully green suite with an operator missing from the engine — while
   the fixed runner reports `Failed: 27`. Both edges of the discriminator are now pinned by CI
   rather than by hand: `GivenAnUnregisteredFunction_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseFails`
   requires a bare `NotSupportedException` to fail the case, and
   `GivenAnAllowlistedFeature_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseSkips` requires an
   allowlisted marker to skip it. Either guard alone is satisfiable by a runner that has stopped
   discriminating; together they are not. See [FHIRPath Release Readiness](release-readiness.md) E1
   for the falsification run.
2. **The corpus is pinned per file, so the figure names *which* suite.** `_suiteFileHashes` pins the
   SHA-256 of each extracted `tests-fhir-{r4,r4b,r5}.xml` and `VerifyFhirTestCasesProvenance` checks
   all three before a single case is parsed. This is not redundant with the archive hash: `TestData/`
   is gitignored, so the extracted tree is unversioned local state that a hand edit or a
   half-finished corpus bump can change without touching `testcases.zip` or the `.downloaded` marker.
   Editing an expected output would otherwise move the pass count with nothing to say so.

| Suite file | SHA-256 |
|---|---|
| `r4/fhirpath/tests-fhir-r4.xml` | `BE78B5237322AB0EEFC628676A28FC503F67AFF2524CAF60F8FF5A7ABAE0E570` |
| `r4b/fhirpath/tests-fhir-r4b.xml` | `F812D45BCABB7D90C1BAF9CBF7FC461C7E832BB6AC73A290BB0C12740956F4F3` |
| `r5/fhirpath/tests-fhir-r5.xml` | `74DF53B7671C2C2B9E5100816BF2A70409F7AD00C0927AC6422C9BEC3B3AA366` |

The archive itself is pinned twice more: the csproj fails the build if `testcases.zip` does not hash
to `FhirTestCasesArchiveSha256`, and `VerifyFhirTestCasesProvenance` re-checks that hash and the
`.downloaded` marker at test time.

#### The four-way split

| Version | Cases in corpus | Excluded by scope | Executed | **Passed** | **Failed** | **Skipped** |
|---------|----------------:|------------------:|---------:|-----------:|-----------:|------------:|
| **R4**    | 935       | 0     | 935       | 930       | **0** | 5      |
| **R4B**   | 933       | 0     | 933       | 928       | **0** | 5      |
| **R5**    | 1,035     | 3     | 1,032     | 1,026     | **0** | 6      |
| **Total** | **2,903** | **3** | **2,900** | **2,884** | **0** | **16** |

Every figure carries the `--filter` that produced it. That is not pedantry. On this commit, against
this corpus, three different *passed* counts — **2,884**, **2,890** and **2,896** — and three
different *totals* — **2,900**, **2,906** and **2,912** — are all reproducible, and the only thing
that varies between them is the `--filter` argument. (A fourth number, `2,902`, is not reproducible
here at all: it was the *pre-fix* runner's passed count under the broad runner filter, and appears in
the E1 falsification record.) The filter was the variable, not the measurement, and a figure
published without one is the defect this baseline exists to retire.

| Scope | `--filter` argument | Result |
|---|---|---|
| **All three versions (canonical)** | `Category=OfficialTestSuite` | `Failed: 0, Passed: 2884, Skipped: 16, Total: 2900` |
| R4 only | `FullyQualifiedName~OfficialTestSuite_R4&FullyQualifiedName!~OfficialTestSuite_R4B` | `Failed: 0, Passed: 930, Skipped: 5, Total: 935` |
| R4B only | `FullyQualifiedName~OfficialTestSuite_R4B` | `Failed: 0, Passed: 928, Skipped: 5, Total: 933` |
| R5 only | `FullyQualifiedName~OfficialTestSuite_R5` | `Failed: 0, Passed: 1026, Skipped: 6, Total: 1032` |

`Category=OfficialTestSuite` is the canonical filter because it selects exactly the three suite
entry points by trait — one xunit case per official-suite case, and nothing else.
`FullyQualifiedName~OfficialTestSuiteRunner.OfficialTestSuite_` selects the identical set and is a
useful cross-check. The two broader filters seen in earlier write-ups do not:

| Broader filter | Total | What it adds |
|---|---|---|
| `FullyQualifiedName~OfficialTestSuiteRunner` | 2,906 | + 6 `OfficialTestSuiteRunnerPredicateTests` harness tests |
| `FullyQualifiedName~OfficialTestSuite` | 2,912 | + those 6, + 6 `OfficialTestSuiteSkipListTests` guard tests |

Neither of those 12 tests is an official-suite case. They are this repository's own tests *about*
the runner, and counting them inflates a conformance figure with the harness that produces it.

**Cross-check against the runner's own accounting.** `GetTestCasesForVersion` prints a census line at
discovery time, independent of the xunit result tally:

```text
[OfficialTestSuite-r4]  Total: 935,  CDA excluded: 0, Predicate included: 1, Running: 935
[OfficialTestSuite-r4b] Total: 933,  CDA excluded: 0, Predicate included: 1, Running: 933
[OfficialTestSuite-r5]  Total: 1035, CDA excluded: 3, Predicate included: 1, Running: 1032
```

`Total - CDA excluded = Running` in all three versions, and `Running = Passed + Skipped` in all
three. The corpus, the discovery filter and the result tally agree.

One discrepancy did surface and is reconciled rather than papered over: a raw `grep -c '<test '`
over each suite file returns **937 / 935 / 1037**, two more per version than the parser reports.
Those two per file sit inside XML comments — cases the upstream corpus has commented out. The
parser's `Total` is the live-case count and is the correct denominator.

#### Excluded by scope (3)

Three R5 cases carry `mode="cda"` and are filtered out at test discovery, not at parse time — the
parser reads them, `GetTestCasesForVersion` drops them. This is a deliberate scope decision for a
FHIR server, matching the Firely validator's own exclusion and consistent with `hasTemplateIdOf`
being a recorded non-feature. It is not a conformance gap and is not counted as one. R4 and R4B
contain no CDA-mode cases at all.

The discovery filter has a second clause — a case whose `inputFile` cannot be found is also dropped.
It currently excludes **zero** cases in all three versions (`Total - CDA excluded = Running`
exactly), which is why "excluded by scope" is CDA and only CDA. That clause is worth watching: it
has no census line of its own, so a corpus bump that added a case with a missing input file would
remove it from the denominator silently.

#### Skipped, with recorded reason (16)

Not aggregated into a number. Each skip is named, carries its reason, and carries a guard that
retires it automatically when the reason stops holding.

| Case | Versions | n | Reason | Self-retiring guard |
|---|---|---:|---|---|
| `testConformsTo1` | R4, R4B, R5 | 3 | `conformsTo` is deliberately not implemented — profile validation infrastructure belongs to `Ignixa.Validation`, not the FHIRPath engine | `GivenTheDeliberatelyUnsupportedFeatures_WhenEachIsInvoked_ThenTheEngineStillRefusesByName` |
| `testConformsTo2` | R4, R4B, R5 | 3 | same | same |
| `testConformsTo3` | R4, R4B, R5 | 3 | same. This one is `invalid`-marked and used to *pass*: it expects `conformsTo('http://trash')` to be refused for naming a profile that does not exist, and the engine refused it for not implementing `conformsTo` at all. Erroring for the wrong reason is not conformance | same |
| `txTest01` | R5 | 1 | `%terminologies` is deliberately not implemented — requires a terminology server this engine does not depend on | same |
| `txTest02` | R5 | 1 | same | same |
| `txTest03` | R5 | 1 | same | same |
| `testFHIRPathAsFunction21` | R4, R4B | 2 | The `as` singleton rule is enforced from R5 only. **The corpus is arguably wrong here** — see below | `SkipUnlessTheCaseWouldNowPass` |
| `testPlusDate19` | R4, R4B | 2 | R4/R4B expect fractional-second truncation; Ignixa follows R5 behaviour. **Ignixa is deliberately more R5-spec-compliant than the R4 expectation** — see below | `SkipUnlessTheCaseWouldNowPass` |

The two guards work in opposite directions and both are load-bearing:

- `_deliberatelyUnsupportedFeatures` is keyed on the *feature name* carried by
  `FhirPathFunctionNotSupportedException`, not on the exception type. A marker naming an unlisted
  feature fails the case exactly as a bare `NotSupportedException` does — catching the marker type
  alone would be the original defect in a narrower form. The list retires itself because
  `GivenTheDeliberatelyUnsupportedFeatures_WhenEachIsInvoked_ThenTheEngineStillRefusesByName`
  invokes every listed feature and fails if one stops throwing: implementing `conformsTo` turns that
  guard red and forces the entry out, rather than leaving an allowlist entry that quietly catches
  nothing.
- `SkipUnlessTheCaseWouldNowPass` runs the case and **fails if it passes**, so a version-policy skip
  cannot outlive the limitation that justified it. That guard exists because six
  `testQuantity9`/`testQuantity10` skips had already gone stale exactly that way — passing on all
  three versions while still being skipped for a `Fhir.Metrics` limitation that no longer applied.

`conformsTo` and `%terminologies` gate the **conformance claim, not fhir-server integration**:
neither appears in any shipped SearchParameter expression in any supported version, verified by grep
across all five generated definition files. Implementing them for a FHIRPath package release would
be scope creep into validation and terminology subsystems.

#### The two version-policy skips, settled against HAPI

These are the only two skips where the question "is the engine wrong, or is the published expectation
wrong?" has a non-obvious answer. They resolve differently, and collapsing them into one category
would misrepresent both.

**`testFHIRPathAsFunction21` (R4/R4B) — the corpus contradicts both engines.** The suite marks
`Patient.name.as(HumanName).use` invalid in all three versions, but the singleton rule is only
enforceable from R5: HL7's own R4/R4B SearchParameter definitions spell 58 and 59 casts with the `as`
operator, many over `0..*` paths, and rewrote almost all of them to `ofType()` in R5. Enforcing below
R5 would make the indexer throw on HL7's own shipped expressions. **HAPI draws the line in the same
place**: `initFlags()` sets `doNotEnforceAsSingletonRule = true` when
`!VersionUtilities.isR5Plus(worker.getVersion())`
([`org.hl7.fhir.r5/.../fhirpath/FHIRPathEngine.java:237-242`](https://github.com/hapifhir/org.hl7.fhir.core/blob/master/org.hl7.fhir.r5/src/main/java/org/hl7/fhir/r5/fhirpath/FHIRPathEngine.java)).
The published R4/R4B expectation contradicts both engines' reading of R4. Worth filing upstream at
FHIR/fhir-test-cases.

**`testPlusDate19` (R4/R4B) — the corpus is right, and Ignixa deviates deliberately.** This one had
not been checked against HAPI; it has been now, and the answer is the opposite of the case above.
The corpus expects `@1973-12-25T00:00:00.000+10:00 + 0.1 's'` to yield `.000` on R4/R4B and `.100` on
R5. HAPI ships three separate version-specific engines rather than a runtime flag, and its `dateAdd`
differs between them exactly along that line:

| HAPI engine | `dateAdd` seconds arm | Result for `+ 0.1 's'` |
|---|---|---|
| [`org.hl7.fhir.r4/.../fhirpath/FHIRPathEngine.java:2752-2756`](https://github.com/hapifhir/org.hl7.fhir.core/blob/master/org.hl7.fhir.r4/src/main/java/org/hl7/fhir/r4/fhirpath/FHIRPathEngine.java) | `result.add(Calendar.SECOND, value)` only, where `value = q.getValue().intValue()` | `.000` — **truncates** |
| `org.hl7.fhir.r4b/.../fhirpath/FHIRPathEngine.java` | identical to R4 | `.000` — **truncates** |
| [`org.hl7.fhir.r5/.../fhirpath/FHIRPathEngine.java:2906-2916`](https://github.com/hapifhir/org.hl7.fhir.core/blob/master/org.hl7.fhir.r5/src/main/java/org/hl7/fhir/r5/fhirpath/FHIRPathEngine.java) | adds the integer seconds, then re-adds the fractional remainder as `(int)(decValue * 1000)` milliseconds | `.100` — preserves |

So HAPI **does** truncate on R4/R4B and satisfies the published R4/R4B expectation. The corpus is not
wrong here. Ignixa ships one engine following R5 semantics, and that single-engine choice — not a
defect — is why the R4/R4B case cannot pass. Recorded as a deliberate deviation toward R5
spec-compliance, not as an upstream corpus bug. Note also that `dateAdd` carries no version flag in
HAPI's R5 engine, unlike the `as` singleton rule: the split is realised by shipping separate engine
copies, which is the architectural choice Ignixa declined to make.

#### What changed, and why

The figures previously recorded here (`2,900` executed / `2,896` passed / `4` skipped /
`9` "not supported" / `2,887` genuinely asserted) were produced by the pre-fix runner and are
superseded in every column. The differences are not measurement noise:

- **The 9 "not supported" pass-throughs are gone as a category.** They were xunit *passes*: a chosen
  non-feature and a correct answer were the same result. They are now 9 of the 16 recorded skips.
- **Skips went 4 → 16.** Twelve cases moved Passed → Skipped (the six `conformsTo` cases on R4/R4B
  plus `testConformsTo3` on all three versions, and the R5 `txTest01/02/03`, which the earlier
  tabulation counted under "not supported" rather than as skips). **Newly-red from the fix itself:
  zero.** Nothing broke; the honest runner simply stopped calling twelve non-results results.
- **Executed stayed at 2,900, but the denominator is now stated and verified.** 2,903 cases parse, 3
  are excluded by scope, 2,900 execute. The earlier text asserted "2,903 parse and 2,900 execute"
  and then caveated it as unverified; it now reconciles against the runner's own census line.
- **"Genuinely asserted" as a derived column is retired.** It was a subtraction performed by hand on
  top of a number the instrument could not produce. `Passed` now means passed.

Do not restate any of this as "N of M passed". The shape of the claim is the point: **2,884 asserted
and passed, 0 failed, 16 skipped with named reasons, 3 excluded by scope, against
`fhir-test-cases` 1.7.46 pinned by per-file SHA-256, under `--filter "Category=OfficialTestSuite"`,
measured 2026-08-24.**

#### Machinery notes

A raw pass rate reads 100% and tells you nothing, which is exactly why the split above has four
columns rather than two. One further piece of machinery is worth recording because it is *not*
currently protecting anything:

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
The skip mechanism itself is `Xunit.SkippableFact`: xunit 2.9.3 has no working dynamic skip of its
own, so before that was wired in these cases reported as passes and were indistinguishable from
coverage. The 16 skips, their reasons and their guards are enumerated in the four-way split above;
they are not repeated here.

The actual remaining teeth against a regression in the `invalid`-marked cases are `RunInvalidExpressionTest`'s
`Assert.Fail` calls, which sit outside the `try`/`catch` on purpose - an earlier version wrapped the whole
method body in one `try`, so xunit's own `FailException` landed in the trailing `catch (Exception)` and
every `invalid`-marked case passed unconditionally regardless of what the engine did. Fixing that bug is
what turned the whole `invalid` category from unverified into genuinely asserted, and it's what surfaced
the 35 real gaps that then went into `_unsignalledInvalidCases` as named deferrals. Subsequent engine work
(`FhirPathAnalyzer` static analysis, `VariableScope` lexical scoping, version-gating the `as` singleton
rule) is what closed all 35 down to zero - the `Assert.Fail` fix exposed the gaps, it didn't close them.

**Test Coverage Distribution** (by group, counting only cases that assert and pass):
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
- ✅ Full R4/R4B/R5 suite (2,900 cases executed of 2,903 in the corpus, under
  `--filter "Category=OfficialTestSuite"`, measured 2026-08-24) - exceeded Phase 1 goal of 200 tests
- ✅ FHIRPath 2.0 support: Comments, `defineVariable()`, backtick variables, escape sequences
- ✅ Comprehensive function coverage: 120 registered `[FhirPathFunction]` implementations

### Risk Mitigation Applied

- Test filtering supported: `dotnet test --filter "FullyQualifiedName~R4.testBasics"`
- Version-specific tests isolated (no cross-contamination between R4/R4B/R5)
- Known deviations documented in test output (e.g., timezone handling differences)
- Auto-download on first build - no manual git submodule required

### Next Steps

1. **Gap closure** (16 of 2,900 executed cases are skipped rather than asserted, each named with its
   reason in the four-way split above; a further 3 are excluded by scope - the deferral list is
   empty, see the table above):
   - ✅ ~~Math functions (round, abs, sqrt, ln, exp, power, floor, ceiling, truncate)~~ - Complete
   - ✅ ~~String functions (trim, split, contains, encode/decode, escape/unescape)~~ - Complete
   - ✅ ~~FHIRPath 2.0 features (comments, defineVariable, backtick variables)~~ - Complete
   - ✅ ~~`lowBoundary()`/`highBoundary()` - Precision boundary calculations~~ - Complete
   - ✅ ~~`aggregate()` edge cases~~ - Complete
   - 🔲 `conformsTo()` - Requires profile validation infrastructure (3 cases × R4/R4B/R5 = 9 skips)
   - 🔲 `%terminologies` - Requires a terminology server binding (3 cases, R5 only = 3 skips)
   - ✅ ~~UCUM library integration for quantity unit conversion (`testQuantity9`/`testQuantity10`)~~ -
     the skips were stale; both cases pass on R4, R4B and R5 and are now asserted
   - ✅ ~~The deferred cases in `_unsignalledInvalidCases` - invalid expressions the engine does not yet
     signal an error for~~ - Complete; all 35 closed and the dictionary is now empty
2. **CI integration**: Add pass rate tracking to GitHub Actions (fail on regression)
3. **Performance optimization**: Leverage compiled delegate caching for test suite expressions
