# Investigation: Official Test Suite Integration

**Feature**: fhirpath
**Status**: Implemented
**Created**: 2026-01-12
**Completed**: 2026-01-12
**Conformance baseline last measured**: 2026-08-24 (see [Results](#results); the January figures in
this document's history predated the engine work and the runner fix, and are superseded)

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
`fhir-test-cases` **1.7.46**, and **re-measured unchanged at `18797b52`**, the commit that turned the
`invalid`-marked path's error filter into an allowlist. That second measurement is not a formality:
until `18797b52` the same defect this figure was published to retire was still open on the other of
the runner's two arms, so the figure was being carried by a runner that could still launder an engine
gap on 114 of these cases. It did not move, because none of those 114 was passing on an exception
type the allowlist excludes — see the census under "What makes this figure different" below.
Reproduce it with, from the repository root:

```bash
# `Platform=x86` is exported in some shells here and makes a bare `dotnet test <csproj>` fail CS8034.
unset Platform

dotnet test test/Ignixa.FhirPath.Tests/Ignixa.FhirPath.Tests.csproj -f net10.0 \
  --filter "Category=OfficialTestSuite"
# => Failed: 0, Passed: 2884, Skipped: 16, Total: 2900
```

The corpus downloads and hash-verifies on first build, so no manual setup is needed. Swap the
`--filter` for any row in the table below to reproduce that row instead.

#### What makes this figure different from the three it replaces

Three numbers were previously in circulation — 2,881, 2,887 and "2906 of 2906" — and all three came
from the same runner. Two things had to be true before any of them could be replaced:

1. **The runner can fail, and CI keeps it that way.** Until `107480e5` the runner caught
   `NotSupportedException` and `return`ed, which xunit records as a *pass*. The catch was scoped by
   exception type, not by the six functions its comment named, so an unimplemented binary operator
   was laundered into green. Demonstrated, not asserted: with the `xor` arm deleted from
   `FhirPathEvaluator`'s binary-operator switch, the pre-fix runner reported
   `Failed: 0, Passed: 2902, Skipped: 4, Total: 2906` — a fully green suite with an operator missing
   from the engine — while the fixed runner, same mutation and same filter
   (`--filter "FullyQualifiedName~OfficialTestSuiteRunner"`, the filter that run used), reports
   `Failed: 27, Passed: 2863, Skipped: 16, Total: 2906`. All four edges of the discriminator are now
   pinned by CI rather than by hand. On the normal path,
   `GivenAnUnregisteredFunction_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseFails` requires a bare
   `NotSupportedException` to fail the case and
   `GivenAnAllowlistedFeature_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseSkips` requires an
   allowlisted marker to skip it; `...WhenRunAsAnInvalidMarkedCase_ThenTheCaseFails` and
   `...WhenRunAsAnInvalidMarkedCase_ThenTheCaseSkips` require the same two outcomes on the
   `invalid`-marked path. Either guard of a pair alone is satisfiable by a runner that has stopped
   discriminating; together they are not.

   The second pair exists because the first fix left the identical hole open one method over, and no
   guard could see it: both original guards set `IsInvalidTest: false`, so both entered
   `ExecuteTestCase` and neither ever reached `RunInvalidExpressionTest`, whose filter was still a
   two-type denylist that a bare `NotSupportedException` satisfied. Demonstrated the same way, on a
   different arm: with the `"&"` arm deleted from the binary-operator switch, the runner logged
   `[INVALID-OK] testConcatenate4: NotSupportedException: Binary operator '&' is not yet implemented`
   and recorded a pass on all three versions — an engine with no string concatenation at all, counted
   as conformant. With the allowlist in place the same deletion fails `testConcatenate4` on all three
   versions (`Failed: 17, Passed: 2867, Skipped: 16, Total: 2900`, canonical filter) alongside
   `testConcatenate1/2/3`. See [FHIRPath Release Readiness](release-readiness.md) E1 for the original
   falsification run.
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
| `FullyQualifiedName~OfficialTestSuite` | 2,914 | + those 6, + 8 `OfficialTestSuiteSkipListTests` guard tests |

Neither of those 14 tests is an official-suite case. They are this repository's own tests *about*
the runner, and counting them inflates a conformance figure with the harness that produces it. The
second number moves whenever a guard is added — it was 2,912 before the two `invalid`-path probes —
which is a further reason the canonical figure is taken by trait and not by name prefix.

**Cross-check against the runner's own accounting.** `GetTestCasesForVersion` prints a census line at
discovery time, independent of the xunit result tally. It goes to `Console`, which the default
`dotnet test` logger does not surface — add `--logger "console;verbosity=detailed"` to see it:

```text
[OfficialTestSuite-r4] Total: 935, CDA excluded: 0, Predicate included: 1, Running: 935, Untyped outputs (asserted leniently): 53
[OfficialTestSuite-r4b] Total: 933, CDA excluded: 0, Predicate included: 1, Running: 933, Untyped outputs (asserted leniently): 53
[OfficialTestSuite-r5] Total: 1035, CDA excluded: 3, Predicate included: 1, Running: 1032, Untyped outputs (asserted leniently): 0
```

`Total - CDA excluded = Running` in all three versions, and `Running = Passed + Skipped` in all
three. The corpus, the discovery filter and the result tally agree.

**106 of the 2,884 passes are asserted under a weaker rule, and the census line now says so.** An
`<output>` element with no `type` attribute is parsed as type `"unknown"`, which makes `TypesMatch`
return `true` unconditionally — the type assertion is skipped entirely — and routes `ValuesMatch`
into three fallbacks that exist only in that branch: case-insensitive boolean comparison, a numeric
re-parse, and `@`-prefix stripping for temporals. The corpus has 53 such outputs in R4, 53 in R4B and
none in R5, so the *same expressions* are asserted strictly on R5 and leniently on R4/R4B, decided by
nothing but a missing XML attribute.

The leniency is load-bearing and is deliberately left alone: making only the `ValuesMatch` unknown
branch strict fails 28 currently-passing cases — `Comparable1-3`, the `HighBoundary*` and
`LowBoundary*` families, 14 cases across two versions. The defect was never the leniency; it was that
roughly 1% of a figure published to be defensible was measured under a different rule from the other
99% and nothing in the output said which cases those were. It does now.

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
It currently excludes **zero** cases in all three versions, which is why "excluded by scope" is CDA
and only CDA. Note how that is known: not from anything the runner reports, but from
`Total - CDA excluded = Running` holding exactly. The clause has no counter and no census field, so
a corpus bump that renamed an input file would shrink this denominator with nothing printed and
nothing failing. Tracked as **E8** in [FHIRPath Release Readiness](release-readiness.md), with a
pointer comment at the clause itself. Deliberately not fixed in the commit that publishes this
figure — changing the runner and publishing its number together is how an unfalsifiable number gets
made.

#### Skipped, with recorded reason (16)

Not aggregated into a number. Each skip is named, carries its reason, and carries a guard that
retires it when the reason stops holding. Read that guarantee as **one-directional**: it catches a
skip that has outlived its justification, and it does not verify that the skip is happening for the
stated reason. `SkipUnlessTheCaseWouldNowPass` converts *any* non-`SkipException` throw into a skip,
naming the exception type in the reason, so a harness bug would be recorded as a version-policy skip
with its type attached rather than failing. The XML docs on that method say so; the published claim
should too. The exception type in each recorded reason is the compensating control — for the two
version-policy skips it currently reads `InvalidOperationException` (a value mismatch) and
`FailException` (an expected-error assertion), which is what those limitations should produce.

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
neither the `conformsTo()` **function** nor the `%terminologies` variable appears in any shipped
SearchParameter expression in any supported version. Implementing them for a FHIRPath package release
would be scope creep into validation and terminology subsystems.

State the grep precisely, because the obvious one appears to falsify the claim:

```bash
grep -c 'conformsTo(' src/Core/Ignixa.Search/Generated/*SearchParameterDefinitions.g.cs   # 0 in all five
grep -c 'terminologies' src/Core/Ignixa.Search/Generated/*SearchParameterDefinitions.g.cs # 0 in all five
```

Searching for the bare string `conformsTo` instead returns **5 hits in R5 and 5 in R6** —
`Device.conformsTo`, `DeviceDefinition.conformsTo`, `.conformsTo.specification`,
`.conformsTo.category`. Those are R5 *element paths* on `Device`/`DeviceDefinition` that happen to
share the function's name; they are property navigation, never an invocation. The claim is about the
function, and the trailing `(` is what distinguishes them.

#### The two version-policy skips, settled against HAPI

These are the only two skips where the question "is the engine wrong, or is the published expectation
wrong?" has a non-obvious answer. They resolve differently, and collapsing them into one category
would misrepresent both.

**`testFHIRPathAsFunction21` (R4/R4B) — the corpus contradicts both engines.** The suite marks
`Patient.name.as(HumanName).use` invalid in all three versions, but the singleton rule is only
enforceable from R5: HL7's own R4/R4B SearchParameter definitions lean heavily on the `as` cast, many
over `0..*` paths, and R5 rewrote almost all of them to `ofType()`. Enforcing below R5 would make the
indexer throw on HL7's own shipped expressions.

Counted over the five generated `*SearchParameterDefinitions.g.cs` files, taking each `expression:`
string literal and matching `as <Type>` or `.as(` within it (occurrences, then the number of
expression strings containing at least one):

| | STU3 | R4 | R4B | R5 | R6 |
|---|---:|---:|---:|---:|---:|
| `as`-cast occurrences | 57 | 145 | 146 | 8 | 7 |
| expression strings containing one | 42 | 69 | 69 | 8 | 7 |
| `ofType(` occurrences | 0 | 1 | 1 | 185 | 192 |

The R4→R5 collapse from 145 occurrences to 8, against `ofType(` going from 1 to 185, is the whole
argument and it does not depend on the exact rule used to count. An earlier revision of this
paragraph cited "58 and 59 casts", which does not reproduce under either reading above; the figure
came from a runner comment and should not have been promoted into a published document without a
stated method. **HAPI draws the line in the same
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
- **Skips went 4 → 16.** Twelve cases moved Passed → Skipped: `testConformsTo1`, `testConformsTo2`
  and `testConformsTo3` on each of R4, R4B and R5 (9), plus `txTest01`, `txTest02` and `txTest03` on
  R5 (3). That is the same set enumerated in the skip table above, and no other reading of it reaches
  12. **Newly-red from the fix itself: zero.** Nothing broke; the honest runner simply stopped calling
  twelve non-results results.
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

#### Coverage by group

Re-derived from the published run's TRX, not carried forward from earlier prose. Same run, same
filter, same date as the totals above.

The 2,900 executed cases fall into **101 groups**. **97 of them pass 100% on every version in which
they appear.** Exactly four contain a skipped case, and they are the same four the skip table above
already accounts for. None contains a failure, because `Failed` is zero:

| Group | R4 | R4B | R5 | Passed / executed | Why not 100% |
|---|---|---|---|---|---|
| `testConformsTo` | 0/3 | 0/3 | 0/3 | 0/9 | all three cases exercise `conformsTo` |
| `TerminologyTests` | — | — | 0/3 | 0/3 | R5-only; all three exercise `%terminologies` |
| `testInheritance` | 23/24 | 23/24 | 24/24 | 70/72 | `testFHIRPathAsFunction21` on R4/R4B |
| `testPlus` | 26/27 | 26/27 | 34/34 | 86/88 | `testPlusDate19` on R4/R4B |

Those four plus the 97 clean groups sum to 2,884 passed and 16 skipped, which closes against the
four-way split. Spot-checks from the same TRX: `testTypes` 297/297, `testLiterals` 246/246,
`testType` 90/90, `testEquality` 84/84, `LowBoundary` 84/84, `HighBoundary` 72/72,
`testQuantity` 33/33, `testBasics` 21/21, `defineVariable` 21/21 (R5 only), `testAggregate` 12/12.

Notes that survive from the earlier version of this list, now that the numbers behind them are
measured: `testQuantity` is clean because the `testQuantity9`/`10` skips were found stale and
retired; `defineVariable` is clean because `%context` was implemented and `VariableScope` gave
`defineVariable` real lexical scoping, so reading an undefined `%name` signals an error per
FHIRPath §1.9 instead of yielding empty.

**A laundered figure was published in this list, and is recorded rather than quietly corrected.**
It previously read "`testBasics` - 5/7". `testBasics` is **7/7 on R4, R4B and R5**, and was so in the
run being published — arithmetically forced, since `Failed` is zero and no `testBasics` case appears
among the 16 skips. The 5/7 predated the engine work, was never re-measured, and was carried through
a header change (from "counting only genuinely asserted cases" to "counting only cases that assert
and pass") that re-certified it under stronger semantics without measuring it. That is precisely the
defect this document exists to retire, committed inside the document. Every row above is now derived
from the TRX of the run whose totals head this section.

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
  *executes* these through `RunInvalidExpressionTest` and asserts the engine signals an error.
  (This reverses the original design, which dropped them.) In the published run — same date and
  filter as the totals above — **114 invalid-marked cases execute this way (R4 34, R4B 34, R5 46)
  across 46 distinct case names; 109 pass and 5 are skipped.** An earlier revision said "82 cases",
  undated and without a filter; it did not reproduce.

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
