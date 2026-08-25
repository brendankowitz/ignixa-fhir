using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Analysis;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;
using Ignixa.FhirPath.Visitors;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Ignixa.FhirPath.Tests;

/// <summary>
/// Element resolver for FHIRPath resolve() function in test context.
/// Supports contained resources (#id) and bundle entry resolution.
/// </summary>
internal static class TestElementResolver
{
    /// <summary>
    /// Creates a resolver function for the given root element.
    /// </summary>
    public static Func<string, IElement?> Create(IElement root)
    {
        return reference => Resolve(root, reference);
    }

    private static IElement? Resolve(IElement root, string reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        // Contained reference: #id
        if (reference.StartsWith('#'))
        {
            var containedId = reference.Substring(1);
            return ResolveContained(root, containedId);
        }

        // Bundle entry resolution: Type/id
        if (root.InstanceType == "Bundle")
        {
            return ResolveBundleEntry(root, reference);
        }

        // Relative or absolute references without server: return null
        return null;
    }

    private static IElement? ResolveContained(IElement root, string containedId)
    {
        var containedResources = root.Children("contained");
        foreach (var contained in containedResources)
        {
            var idChildren = contained.Children("id");
            if (idChildren.Count > 0)
            {
                var id = idChildren[0].Value?.ToString();
                if (id == containedId)
                {
                    return contained;
                }
            }
        }

        return null;
    }

    private static IElement? ResolveBundleEntry(IElement bundle, string reference)
    {
        // Reference format: Type/id or full URL
        var entries = bundle.Children("entry");
        foreach (var entry in entries)
        {
            // Check fullUrl
            var fullUrlChildren = entry.Children("fullUrl");
            if (fullUrlChildren.Count > 0)
            {
                var fullUrl = fullUrlChildren[0].Value?.ToString();
                if (fullUrl != null && (fullUrl == reference || fullUrl.EndsWith("/" + reference, StringComparison.Ordinal)))
                {
                    var resource = entry.Children("resource");
                    if (resource.Count > 0)
                    {
                        return resource[0];
                    }
                }
            }

            // Check resource type/id
            var resourceChildren = entry.Children("resource");
            if (resourceChildren.Count > 0)
            {
                var resource = resourceChildren[0];
                var resourceType = resource.InstanceType;
                var idChildren = resource.Children("id");
                if (idChildren.Count > 0)
                {
                    var id = idChildren[0].Value?.ToString();
                    if ($"{resourceType}/{id}" == reference)
                    {
                        return resource;
                    }
                }
            }
        }

        return null;
    }
}

public class OfficialTestSuiteRunner(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private static readonly string _projectRoot = FindProjectRoot();

    private static string FindProjectRoot()
    {
        // Navigate up from base directory until we find TestData folder
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var testDataPath = Path.Combine(current, "TestData", "fhir-test-cases");
            if (Directory.Exists(testDataPath))
            {
                return current;
            }
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break; // Reached root
            current = parent;
        }
        // Fallback to old calculation (3 levels up)
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }

    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r4TestCases = new(() => LoadTestCases("r4"));
    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r4bTestCases = new(() => LoadTestCases("r4b"));
    private static readonly Lazy<IReadOnlyList<FhirPathTestCase>> _r5TestCases = new(() => LoadTestCases("r5"));
    private static readonly Lazy<bool> _fhirTestCasesProvenanceVerified = new(VerifyFhirTestCasesProvenance);

    /// <summary>
    /// Official <c>invalid</c>-marked cases the engine does not yet signal an error for, keyed by test name
    /// with the specific gap that defers each one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every entry here used to pass vacuously: <see cref="RunInvalidExpressionTest"/> caught its own
    /// <c>Assert.Fail</c> and logged it as a success, so the whole <c>invalid</c> category was unverified.
    /// With that fixed, these are the cases that genuinely fail, and each is skipped by name rather than
    /// suppressed as a category - a skip that names its gap is auditable, a silent pass is not.
    /// </para>
    /// <para>
    /// Adding a name here is a deferral, never a fix. Removing one should follow from making the engine
    /// signal the error, not from relaxing the assertion.
    /// </para>
    /// <para>
    /// "The engine" here means the evaluator specifically. The cases where the spec mandates an empty
    /// result but the suite encodes the stricter option - unresolved paths, choice-element shorthand,
    /// positional access over an unordered collection - are no longer deferred: <see cref="RunInvalidExpressionTest"/>
    /// consults <see cref="FhirPathAnalyzer"/> after evaluation, which is where this codebase's opt-in
    /// strict typing lives. Note that <see cref="AssertDeferralIsStillNeeded"/> still only re-runs the
    /// evaluator, so it will not notice a gap that the analyzer alone has closed.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<string, string> _unsignalledInvalidCases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Empty by design, not by omission. Every case that was deferred here has since been closed:
        // the unresolved-path and ordering cases by RunInvalidExpressionTest consulting FhirPathAnalyzer
        // (the evaluator was always conformant - the spec mandates empty and makes strict typing opt-in),
        // the defineVariable scope cases by VariableScope giving defineVariable real lexical scoping, and
        // testFHIRPathAsFunction21 by version-gating the singleton rule, which moved it to the
        // version-conditional skip in RunTestCase rather than a blanket deferral.
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The deferred case names, exposed so <see cref="OfficialTestSuiteSkipListTests"/> can prove each one
    /// still names a real, still-invalid case in the official suites.
    /// </summary>
    public static IEnumerable<string> DeferredInvalidCaseNames => _unsignalledInvalidCases.Keys;

    /// <summary>
    /// The deferred cases with their justifications, exposed for the same guard tests.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DeferredInvalidCaseReasons => _unsignalledInvalidCases;

    /// <summary>
    /// The resolved directory containing <c>TestData/fhir-test-cases</c>, exposed for the guard tests.
    /// </summary>
    public static string ProjectRoot => _projectRoot;

    /// <summary>
    /// Runs the corpus provenance and per-file hash checks, exposed so a test that parses the suite files
    /// through <see cref="ProjectRoot"/> is held to the same pin as the three suite entry points.
    /// </summary>
    /// <remarks>
    /// <c>TestData/</c> is gitignored, so nothing but these checks stands between an edited expected
    /// output and a guard that reports green against it. <see cref="LoadTestCases"/> touches the pin for
    /// the suite itself; a guard that reaches the same XML by its own path would otherwise make its claim
    /// against an unverified corpus, and a targeted <c>--filter</c> run would never notice - measured,
    /// <see cref="OfficialTestSuiteSkipListTests"/> reported all green against a tampered file.
    /// </remarks>
    public static void EnsureCorpusProvenance() => _ = _fhirTestCasesProvenanceVerified.Value;

    /// <summary>
    /// The deliberately unimplemented FHIR-specific features that report a
    /// <see cref="FhirPathFunctionNotSupportedException"/>, keyed by the
    /// <see cref="FhirPathFunctionNotSupportedException.FeatureName"/> the engine reports, with the
    /// reason each one is a choice rather than a gap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not an inventory of everything this engine chose not to build. <c>htmlChecks</c>,
    /// <c>subsumes</c> and <c>subsumedBy</c> are also deliberate omissions, but they carry no
    /// <c>[FhirPathFunction]</c> registration at all, so they fall through the generated dispatcher's
    /// default arm as a bare <see cref="NotSupportedException"/> and this runner reports them as engine
    /// gaps. That is the safe direction on every path - <see cref="IsParserSignalledError"/> and
    /// <see cref="IsEvaluatorSignalledError"/> name the types they accept, so a bare
    /// <see cref="NotSupportedException"/> fails an <c>invalid</c>-marked case exactly as it fails a
    /// normal one. No current case reaches them, but a corpus bump that adds one would produce a red
    /// suite with a misleading cause; the fix then is to give those three a marker and list them here,
    /// not to widen the allowlists.
    /// </para>
    /// <para>
    /// This list is what makes the type marker discriminating. Catching
    /// <see cref="FhirPathFunctionNotSupportedException"/> alone would still be scoping by exception type,
    /// which is the defect being fixed here in a narrower form: a seventh feature could start throwing the
    /// marker and be recorded as deliberate without anyone having decided it was. A marker whose name is
    /// not listed here fails the case, exactly as a bare <see cref="NotSupportedException"/> now does.
    /// </para>
    /// <para>
    /// None of these appears in any shipped SearchParameter expression in any supported version, so they
    /// gate the conformance claim rather than resource indexing. Each is a recorded skip, never a pass:
    /// "the suite exercised a feature we chose not to build" and "the engine computed the right answer"
    /// are different results, and a published count has to be able to say which.
    /// </para>
    /// <para>
    /// The list retires itself through
    /// <see cref="OfficialTestSuiteSkipListTests.GivenTheDeliberatelyUnsupportedFeatures_WhenEachIsInvoked_ThenTheEngineStillRefusesByName"/>,
    /// which invokes every listed feature and fails if one no longer throws. Implementing
    /// <c>conformsTo</c> therefore turns that guard red and forces its entry out, rather than leaving an
    /// allowlist entry that quietly catches nothing - the staleness
    /// <see cref="AssertDeferralIsStillNeeded"/> exists to prevent on the other list.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<string, string> _deliberatelyUnsupportedFeatures = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["conformsTo"] = "profile validation infrastructure is out of scope for the FHIRPath engine; it belongs to Ignixa.Validation",
        ["memberOf"] = "requires a terminology server, which this engine deliberately does not depend on",
        ["validateVS"] = "requires a terminology server, which this engine deliberately does not depend on",
        ["translate"] = "requires a terminology server and ConceptMap resolution, which this engine deliberately does not depend on",
        ["hasTemplateIdOf"] = "CDA support is out of scope for a FHIR server, consistent with the suite's cda-mode exclusion",
        ["%terminologies"] = "requires a terminology server, which this engine deliberately does not depend on",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The deliberately unsupported feature names with their justifications, exposed so
    /// <see cref="OfficialTestSuiteSkipListTests"/> can prove each one is still refused by the engine.
    /// </summary>
    public static IReadOnlyDictionary<string, string> DeliberatelyUnsupportedFeatures => _deliberatelyUnsupportedFeatures;

    // Default patient resource for tests without input files (matches Firely validator behavior)
    private const string DefaultPatientXml = "<Patient xmlns=\"http://hl7.org/fhir\"><id value=\"pat1\"/></Patient>";


    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private static IReadOnlyList<FhirPathTestCase> LoadTestCases(string version)
    {
        _ = _fhirTestCasesProvenanceVerified.Value;
        var testSuiteFilePath = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", version, "fhirpath", $"tests-fhir-{version}.xml");

        if (!File.Exists(testSuiteFilePath))
        {
            throw new FileNotFoundException($"Test suite file not found. Ensure FHIR test cases are downloaded: {testSuiteFilePath}");
        }

        return FhirPathTestSuiteParser.ParseTestSuite(testSuiteFilePath);
    }

    private static bool VerifyFhirTestCasesProvenance()
    {
        var expectedVersion = GetFhirTestCasesMetadata("FhirTestCasesVersion");
        var expectedHash = GetFhirTestCasesMetadata("FhirTestCasesArchiveSha256");
        var testDataDirectory = Path.Combine(_projectRoot, "TestData");
        var markerPath = Path.Combine(testDataDirectory, "fhir-test-cases", ".downloaded");
        string[] expectedMarker =
        [
            $"packageVersion={expectedVersion}",
            $"archiveSha256={expectedHash}",
        ];

        if (!File.Exists(markerPath) || !File.ReadAllLines(markerPath).SequenceEqual(expectedMarker, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"FHIR test cases provenance marker is missing or mismatched: {markerPath}. Delete TestData and rebuild to download fhir-test-cases {expectedVersion}.");
        }

        var archivePath = Path.Combine(testDataDirectory, "testcases.zip");
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                $"FHIR test cases archive is missing: {archivePath}. Delete TestData and rebuild to download fhir-test-cases {expectedVersion}.",
                archivePath);
        }

        using (var archive = File.OpenRead(archivePath))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(archive));
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"FHIR test cases archive hash mismatch. Expected {expectedHash}, got {actualHash}. Delete TestData and rebuild.");
            }
        }

        VerifySuiteFileHashes(expectedVersion);
        return true;
    }

    /// <summary>
    /// The SHA-256 of each per-version suite file as extracted from fhir-test-cases 1.7.46, so a
    /// conformance claim names the corpus it was measured against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The archive hash above pins what was downloaded. These pin what is actually parsed, which is not
    /// the same claim: <c>TestData/</c> is gitignored, so the extracted tree is unversioned local state
    /// that a hand edit, a partial extraction, or a half-finished corpus bump can change without touching
    /// <c>testcases.zip</c> or the <c>.downloaded</c> marker. Editing an expected output in one of these
    /// files would move the pass count with nothing to say so.
    /// </para>
    /// <para>
    /// Bytes, not parsed content: extraction does no newline translation, so these are stable across
    /// machines, and a diff in either direction should be a deliberate corpus bump that updates
    /// <c>FhirTestCasesVersion</c>, the archive hash, and these three values together.
    /// </para>
    /// </remarks>
    private static readonly FrozenDictionary<string, string> _suiteFileHashes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["r4"] = "BE78B5237322AB0EEFC628676A28FC503F67AFF2524CAF60F8FF5A7ABAE0E570",
        ["r4b"] = "F812D45BCABB7D90C1BAF9CBF7FC461C7E832BB6AC73A290BB0C12740956F4F3",
        ["r5"] = "74DF53B7671C2C2B9E5100816BF2A70409F7AD00C0927AC6422C9BEC3B3AA366",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static void VerifySuiteFileHashes(string expectedVersion)
    {
        foreach (var (version, expectedHash) in _suiteFileHashes)
        {
            var suitePath = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", version, "fhirpath", $"tests-fhir-{version}.xml");
            if (!File.Exists(suitePath))
            {
                throw new FileNotFoundException($"FHIR test suite file is missing: {suitePath}. Delete TestData and rebuild to download fhir-test-cases {expectedVersion}.", suitePath);
            }

            using var suiteFile = File.OpenRead(suitePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(suiteFile));
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"FHIR test suite file hash mismatch for {version}. Expected {expectedHash}, got {actualHash} at {suitePath}. The extracted corpus no longer matches fhir-test-cases {expectedVersion}; delete TestData and rebuild, or update the pin deliberately.");
            }
        }
    }

    private static string GetFhirTestCasesMetadata(string key) =>
        typeof(OfficialTestSuiteRunner).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))?.Value
        ?? throw new InvalidOperationException($"Missing {key} assembly metadata.");

    public static IEnumerable<object[]> GetR4TestCases() => GetTestCasesForVersion("r4", _r4TestCases);
    public static IEnumerable<object[]> GetR4BTestCases() => GetTestCasesForVersion("r4b", _r4bTestCases);
    public static IEnumerable<object[]> GetR5TestCases() => GetTestCasesForVersion("r5", _r5TestCases);

    private static IEnumerable<object[]> GetTestCasesForVersion(string versionLabel, Lazy<IReadOnlyList<FhirPathTestCase>> testCasesLazy)
    {
        var testCases = testCasesLazy.Value;
        var versionDirectory = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", versionLabel);
        var examplesDirectory = Path.Combine(versionDirectory, "examples");

        // Filter like the Firely validator: exclude only CDA mode.
        // We include:
        // - Predicate tests (converted to a boolean assertion after evaluation)
        // - Invalid expression tests (to test error handling)
        // - Tests without input files (use default patient)
        // - All function tests (NotImplementedException is thrown at runtime)
        // Note: Check version directory first, then examples (version may have modified files for tests)
        var filteredTests = testCases
            .Where(tc => tc.Mode != "cda")
            // KNOWN GAP (release-readiness E8): unlike the CDA clause above, this one has no counter.
            // A case whose inputFile resolves to no file on disk leaves the denominator with nothing
            // printed and nothing failing, so a published conformance count would silently shrink.
            // It excludes zero cases on all three versions today, which is only knowable because
            // Total - CDA excluded == Running in the census line below. Do not remove this clause
            // without giving it a counter first.
            .Where(tc => tc.InputFile is null ||
                         File.Exists(Path.Combine(versionDirectory, tc.InputFile)) ||
                         File.Exists(Path.Combine(examplesDirectory, tc.InputFile)));

        var totalTests = testCases.Count;
        var cdaTests = testCases.Count(tc => tc.Mode == "cda");
        var predicateTests = testCases.Count(tc => tc.Predicate);
        var runningCount = filteredTests.Count();

        // Untyped outputs are asserted under a weaker rule than the rest, and the count says how much of
        // the figure that covers. An <output> with no type attribute becomes "unknown", which makes
        // TypesMatch return true unconditionally - the type assertion is skipped entirely - and routes
        // ValuesMatch into three fallbacks that exist only in that branch (case-insensitive booleans,
        // numeric re-parse, @-prefix stripping). The leniency is load-bearing and deliberately left
        // alone: made strict, 28 currently-passing cases fail, all of them Comparable/HighBoundary/
        // LowBoundary on R4 and R4B. What was not acceptable was leaving it unstated, so that the same
        // expressions being asserted strictly on R5 and leniently on R4/R4B - decided by nothing but a
        // missing XML attribute - showed up nowhere in the numbers this suite publishes.
        var untypedOutputCases = filteredTests.Count(tc => tc.ExpectedOutputs.Any(output => output.Type == "unknown"));

        Console.WriteLine($"[OfficialTestSuite-{versionLabel}] Total: {totalTests}, CDA excluded: {cdaTests}, Predicate included: {predicateTests}, Running: {runningCount}, Untyped outputs (asserted leniently): {untypedOutputCases}");

        foreach (var testCase in filteredTests)
        {
            yield return [testCase];
        }
    }

    // No longer used - we run all tests and let them fail/pass naturally
    // private static bool ShouldSkipTest(FhirPathTestCase testCase) { ... }

    [SkippableTheory]
    [MemberData(nameof(GetR4TestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R4")]
    public void OfficialTestSuite_R4(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R4);
    }

    [SkippableTheory]
    [MemberData(nameof(GetR4BTestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R4B")]
    public void OfficialTestSuite_R4B(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R4B);
    }

    [SkippableTheory]
    [MemberData(nameof(GetR5TestCases))]
    [Trait("Category", "OfficialTestSuite")]
    [Trait("FhirVersion", "R5")]
    public void OfficialTestSuite_R5(FhirPathTestCase testCase)
    {
        RunTestCase(testCase, FhirVersion.R5);
    }

    private void RunTestCase(FhirPathTestCase testCase, FhirVersion fhirVersion)
    {
        // Arrange
        ArgumentNullException.ThrowIfNull(testCase);

        // Version-specific behaviour: R4/R4B truncate fractional seconds to integers, R5 preserves them,
        // and our implementation follows R5 (sub-second precision preserved).
        //
        // Unlike testFHIRPathAsFunction21 below, the corpus is right here and HAPI agrees with it.
        // HAPI ships three version-specific engine copies rather than a runtime flag, and dateAdd's
        // seconds arm differs between them exactly along this line: org.hl7.fhir.r4 (and r4b)
        // fhirpath/FHIRPathEngine.java:2752-2756 is result.add(Calendar.SECOND, q.getValue().intValue())
        // and nothing more, so + 0.1 's' truncates; org.hl7.fhir.r5 :2906-2916 adds the integer seconds
        // and then re-adds the remainder as (int)(decValue * 1000) milliseconds. So this is not the
        // "corpus contradicts both engines" case - it is a deliberate choice to ship one engine with R5
        // semantics rather than three. See docs/features/fhirpath/investigations/
        // official-test-suite-integration.md for the citation.
        if (fhirVersion is FhirVersion.R4 or FhirVersion.R4B && testCase.Name == "testPlusDate19")
        {
            SkipUnlessTheCaseWouldNowPass(
                testCase,
                fhirVersion,
                "testPlusDate19: R4/R4B expect truncation of fractional seconds; this implementation follows R5 behaviour");
            return;
        }

        // Singleton cardinality for 'as'/'as()'. The suite marks Patient.name.as(HumanName) invalid in
        // all three versions, but the rule is only enforceable from R5: HL7's own R4/R4B SearchParameter
        // definitions spell 58 and 59 casts with the 'as' operator, many of them over 0..* paths, and
        // rewrote almost all of them to ofType() in R5. Enforcing below R5 would make the indexer throw on those
        // shipped expressions, so TypeMatcher.EnsureSingletonInput gates on the schema version and this
        // case is genuinely not signalled on R4/R4B. HAPI draws the line in the same place and for the
        // same reason (doNotEnforceAsSingletonRule below R5).
        if (fhirVersion is FhirVersion.R4 or FhirVersion.R4B && testCase.Name == "testFHIRPathAsFunction21")
        {
            SkipUnlessTheCaseWouldNowPass(
                testCase,
                fhirVersion,
                "testFHIRPathAsFunction21: the 'as' singleton rule is enforced from R5 onwards, because HL7's own R4/R4B SearchParameters violate it - see TypeMatcher.EnsureSingletonInput");
            return;
        }

        ExecuteTestCase(testCase, fhirVersion);
    }

    /// <summary>
    /// Runs a version-policy skip's case and fails when it now passes, so the skip retires itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A skip with no such guard stays green forever whether or not it is still needed. Six quantity-algebra
    /// skips in this file went stale exactly that way: their cases passed on all three versions and nothing
    /// said so, which also meant unit multiplication and division could regress without failing anything.
    /// <see cref="AssertDeferralIsStillNeeded"/> gives <see cref="_unsignalledInvalidCases"/> the same
    /// protection; this gives it to the two version-policy skips, which are not deferrals and so do not go
    /// through that list.
    /// </para>
    /// <para>
    /// The skip carries the failure that produced it. Any throw out of <see cref="ExecuteTestCase"/> is
    /// read here as "the limitation still applies", and a harness bug throws exactly the same way a real
    /// version-policy mismatch does, so a <see cref="NullReferenceException"/> would otherwise report as a
    /// legitimate skip with its cause discarded. Naming the exception in the reason makes the skip its own
    /// evidence: when it later turns out to have been wrong, the record of why it skipped is still there.
    /// <see cref="OperationCanceledException"/> is re-thrown so an abandoned run cannot be recorded as a
    /// version-policy skip.
    /// </para>
    /// </remarks>
    private void SkipUnlessTheCaseWouldNowPass(FhirPathTestCase testCase, FhirVersion fhirVersion, string reason)
    {
        try
        {
            ExecuteTestCase(testCase, fhirVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not Xunit.SkipException)
        {
            SkipTest($"{reason} [{ex.GetType().Name}: {ex.Message}]");
            return;
        }

        Assert.Fail($"""
            '{testCase.Name}' is skipped on {fhirVersion} but the case now passes, so the skip is stale and must be removed.
            Expression: {testCase.Expression}
            Skip reason on file: {reason}
            """);
    }

    private void ExecuteTestCase(FhirPathTestCase testCase, FhirVersion fhirVersion)
    {
        var versionString = fhirVersion switch
        {
            FhirVersion.R4 => "r4",
            FhirVersion.R4B => "r4b",
            FhirVersion.R5 => "r5",
            _ => throw new ArgumentOutOfRangeException(nameof(fhirVersion))
        };

        var versionDirectory = Path.Combine(_projectRoot, "TestData", "fhir-test-cases", versionString);
        var examplesDirectory = Path.Combine(versionDirectory, "examples");

        // Load resource - use default patient if no input file specified
        var schemaProvider = fhirVersion.GetSchemaProvider();
        IElement element;

        if (testCase.InputFile is not null)
        {
            // Try version directory first (may have modified files for tests), then fall back to examples
            var inputFilePath = Path.Combine(versionDirectory, testCase.InputFile);
            if (!File.Exists(inputFilePath))
            {
                inputFilePath = Path.Combine(examplesDirectory, testCase.InputFile);
            }

            var resourceJson = FhirXmlToJsonConverter.LoadResourceAsJson(inputFilePath);
            var resource = ResourceJsonNode.Parse(resourceJson);
            element = resource.ToElement(schemaProvider);
        }
        else
        {
            // Use default patient for tests without input file (matches Firely validator)
            var defaultJson = FhirXmlToJsonConverter.ConvertXmlToJson(DefaultPatientXml);
            var resource = ResourceJsonNode.Parse(defaultJson);
            element = resource.ToElement(schemaProvider);
        }

        // Handle invalid expression tests - they should fail at parse or evaluation time
        if (testCase.IsInvalidTest)
        {
            RunInvalidExpressionTest(testCase, element, schemaProvider);
            return;
        }

        // Act - parse expression
        Expression expression;
        try
        {
            expression = _parser.Parse(testCase.Expression);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse FHIRPath expression '{testCase.Expression}' in test '{testCase.Name}' (group: {testCase.GroupName})", ex);
        }

        // Evaluate expression and enumerate results (lazy evaluation means exceptions can occur during ToList)
        List<IElement> resultList;
        try
        {
            resultList = _evaluator.Evaluate(element, expression, BuildContext(element, schemaProvider)).ToList();
        }
        catch (FhirPathFunctionNotSupportedException ex) when (_deliberatelyUnsupportedFeatures.ContainsKey(ex.FeatureName))
        {
            SkipDeliberatelyUnsupportedFeature(testCase, ex);
            return;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to evaluate FHIRPath expression '{testCase.Expression}' in test '{testCase.Name}' (group: {testCase.GroupName}, input: {testCase.InputFile})", ex);
        }

        // Assert
        if (testCase.Predicate)
        {
            ValidatePredicateResult(testCase, resultList);
            return;
        }

        ValidateResults(testCase, resultList);
    }

    /// <summary>
    /// Runs a test case that expects an invalid expression (syntax, semantic, or execution error).
    /// The test passes only if the engine itself signals an error - a <c>syntax</c> case must fail at
    /// parse time, a <c>semantic</c>/<c>execution</c> case at parse time, at evaluation time, or under
    /// static analysis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every <c>Assert.Fail</c> below sits outside a <c>try</c> on purpose. An earlier version wrapped the
    /// whole method body in one, so xunit's own <c>FailException</c> landed in the trailing
    /// <c>catch (Exception)</c> and was logged as <c>[INVALID-OK]</c> - which made every single
    /// <c>invalid</c>-marked case pass unconditionally. The <see cref="IsParserSignalledError"/> and
    /// <see cref="IsEvaluatorSignalledError"/> filters keep that from coming back if this method is ever
    /// restructured: both are allowlists of engine error types, and <c>FailException</c> is on neither.
    /// </para>
    /// <para>
    /// The marker catch below the evaluation call is what turns <c>testConformsTo3</c> from a pass into a
    /// skip. The case is marked <c>invalid</c> and expects <c>conformsTo('http://trash')</c> to be refused
    /// for naming a profile that does not exist; this engine refuses it for not implementing
    /// <c>conformsTo</c> at all. Both throw, so the case used to pass - on an error it was not testing for.
    /// Erroring for the wrong reason is not conformance, so it routes to a recorded skip. The allowlists
    /// would reject the marker on their own, but rejecting it produces a failure, not the skip; only that
    /// catch produces the skip.
    /// </para>
    /// <para>
    /// The analyzer is consulted last, not first, even though HAPI's runner calls <c>check()</c> before
    /// <c>evaluate()</c>. Ordering it after evaluation keeps the existing assertion path exactly as strict
    /// as it was: every case the evaluator already signals still passes because the evaluator signalled it,
    /// so a later analyzer regression cannot mask an evaluator regression. The acceptance set is the same
    /// either way - a case passes if either layer rejects it.
    /// </para>
    /// </remarks>
    private void RunInvalidExpressionTest(FhirPathTestCase testCase, IElement element, IFhirSchemaProvider schemaProvider)
    {
        if (_unsignalledInvalidCases.TryGetValue(testCase.Name, out var deferralReason))
        {
            AssertDeferralIsStillNeeded(testCase, element, schemaProvider, deferralReason);
            return;
        }

        var invalidType = testCase.InvalidType ?? "syntax";

        Expression expression;
        try
        {
            expression = _parser.Parse(testCase.Expression);
        }
        catch (Exception ex) when (IsParserSignalledError(ex))
        {
            _output.WriteLine($"[INVALID-OK] {testCase.Name}: parse-time error as expected ({invalidType}): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (invalidType == "syntax")
        {
            Assert.Fail($"Expected syntax error but expression parsed successfully in test '{testCase.Name}' (group: {testCase.GroupName}): {testCase.Expression}");
        }

        List<IElement> results;
        try
        {
            // Force evaluation by iterating results - the evaluator is lazy
            results = _evaluator.Evaluate(element, expression, BuildContext(element, schemaProvider)).ToList();
        }
        catch (FhirPathFunctionNotSupportedException ex) when (_deliberatelyUnsupportedFeatures.ContainsKey(ex.FeatureName))
        {
            SkipDeliberatelyUnsupportedFeature(testCase, ex);
            return;
        }
        catch (Exception ex) when (IsEvaluatorSignalledError(ex))
        {
            _output.WriteLine($"[INVALID-OK] {testCase.Name}: evaluation error as expected ({invalidType}): {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (DescribeAnalyzerRejection(testCase, element, schemaProvider) is { } analyzerDiagnostics)
        {
            _output.WriteLine($"[INVALID-OK] {testCase.Name}: static analysis error as expected ({invalidType}): {analyzerDiagnostics}");
            return;
        }

        Assert.Fail($"""
            Expected {invalidType} error but neither evaluation nor static analysis rejected the expression in test '{testCase.Name}' (group: {testCase.GroupName})
            Expression: {testCase.Expression}
            Input file: {testCase.InputFile}
            Actual outputs: {FormatActualOutputs(results)}
            """);
    }

    /// <summary>
    /// Runs <see cref="FhirPathAnalyzer"/> over the case and returns its rejection diagnostics joined into
    /// one line, or <see langword="null"/> when the analyzer accepts the expression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the layer the suite's schema-aware <c>invalid</c> cases actually belong to. FHIRPath 1.4.4
    /// mandates an EMPTY collection for a path that does not resolve ("<c>Patient.name</c> will return an
    /// empty collection (not null)"), and 1.3 makes strict typing explicitly opt-in ("implementations may
    /// choose to protect against such cases by employing strict typing"). Making the evaluator throw would
    /// contradict the base spec, so the opt-in lives in the analyzer and the harness runs it - the same
    /// split as HAPI's runner, which calls <c>check()</c> alongside <c>evaluate()</c>.
    /// </para>
    /// <para>
    /// Two diagnostic kinds are filtered out rather than read as a rejection. <c>Analysis failed:</c> is
    /// <see cref="FhirPathAnalyzer.Analyze(Expression, string)"/>'s own crash-catch, and a <c>Parse error:</c>
    /// would mean the analyzer's parser rejected an expression this method already parsed successfully a few
    /// lines above. Both are engine defects; letting either stand in for "the expression was correctly
    /// refused" would turn a bug into a green test. Everything that does count is echoed to the test output
    /// so the reason a case passed stays auditable rather than being reduced to a boolean.
    /// </para>
    /// </remarks>
    private static string? DescribeAnalyzerRejection(FhirPathTestCase testCase, IElement element, IFhirSchemaProvider schemaProvider)
    {
        if (string.IsNullOrEmpty(element.InstanceType))
        {
            return null;
        }

        var analysis = new FhirPathAnalyzer(schemaProvider).Analyze(testCase.Expression, element.InstanceType);

        var diagnostics = analysis.Issues
            .Where(issue => issue.Severity == ValidationIssueSeverity.Error)
            .Select(issue => issue.Message)
            .Where(IsDiagnosisRatherThanAnalyzerFailure)
            .ToList();

        return diagnostics.Count == 0 ? null : string.Join(" | ", diagnostics);
    }

    private static bool IsDiagnosisRatherThanAnalyzerFailure(string message) =>
        !message.StartsWith("Analysis failed:", StringComparison.Ordinal) &&
        !message.StartsWith("Parse error:", StringComparison.Ordinal);

    /// <summary>
    /// Fails when a deferred case is no longer a gap. <see cref="SkipTest"/> only reports a skip, and
    /// <see cref="OfficialTestSuiteSkipListTests"/> only proves the named case still exists upstream and is
    /// still marked invalid - which stays true forever - so neither of them notices when the engine starts
    /// signalling the error and the entry goes stale. This does: it runs the deferred case and fails if the
    /// engine now signals, so closing a gap forces the list entry to be removed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately re-runs the evaluator only, not <see cref="DescribeAnalyzerRejection"/>. Every remaining
    /// entry is deferred on an evaluator gap, and a gap closed in the analyzer instead has to be retired by
    /// hand - checking both here would fail the entries currently being worked on in parallel rather than
    /// reporting on the one that was fixed.
    /// </para>
    /// <para>
    /// The marker branch mirrors the one in <see cref="ExecuteTestCase"/> and
    /// <see cref="RunInvalidExpressionTest"/>. It is kept for that symmetry and not described further:
    /// <see cref="_unsignalledInvalidCases"/> is empty by design, so this whole method is unreachable and
    /// prose about how its branches interact would document behaviour nothing can execute.
    /// </para>
    /// </remarks>
    private void AssertDeferralIsStillNeeded(FhirPathTestCase testCase, IElement element, IFhirSchemaProvider schemaProvider, string deferralReason)
    {
        try
        {
            var expression = _parser.Parse(testCase.Expression);

            _ = _evaluator.Evaluate(element, expression, BuildContext(element, schemaProvider)).ToList();
        }
        catch (FhirPathFunctionNotSupportedException ex) when (_deliberatelyUnsupportedFeatures.ContainsKey(ex.FeatureName))
        {
            SkipDeliberatelyUnsupportedFeature(testCase, ex);
            return;
        }
        catch (Exception ex) when (IsParserSignalledError(ex) || IsEvaluatorSignalledError(ex))
        {
            Assert.Fail($"""
                '{testCase.Name}' is deferred in _unsignalledInvalidCases but the engine now signals the error, so the entry is stale and must be removed.
                Expression: {testCase.Expression}
                Signalled: {ex.GetType().Name}: {ex.Message}
                Deferral reason on file: {deferralReason}
                """);
        }

        SkipTest($"{testCase.Name}: {deferralReason}");
    }

    /// <summary>
    /// Builds the evaluation context every case is run under. The version's schema is supplied so the type
    /// operators can tell an unresolvable type identifier from a type that simply does not match, which the
    /// suite's <c>as(string1)</c> / <c>ofType(string1)</c> cases require.
    /// </summary>
    private static FhirEvaluationContext BuildContext(IElement element, IFhirSchemaProvider schemaProvider) => new()
    {
        Resource = element,
        ElementResolver = TestElementResolver.Create(element),
        Schema = schemaProvider
    };

    /// <summary>
    /// The exception the parser raises to mean "this expression is not FHIRPath", which is the only throw
    /// out of <see cref="FhirPathParser.Parse"/> that lets a <c>syntax</c>-marked case pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An allowlist, not a denylist, and that is the whole point. Named exception types are the engine's
    /// error signal; everything else reaching one of these catches is the engine falling over, and a
    /// harness that reads "it threw" as "it correctly refused" cannot fail. This one names
    /// <see cref="FormatException"/> because <see cref="FhirPathParser.ParseToTree"/> documents and throws
    /// exactly that for a failed tokenize and a failed parse.
    /// </para>
    /// <para>
    /// <see cref="ArgumentException"/> from the same method is deliberately not here. It reports a null or
    /// whitespace expression - a caller-contract violation, not a verdict on FHIRPath syntax - and a
    /// harness that accepted it would record "we passed the parser nothing" as "the parser rejected the
    /// expression".
    /// </para>
    /// </remarks>
    private static bool IsParserSignalledError(Exception exception) =>
        exception is FormatException;

    /// <summary>
    /// The exception the evaluator raises to mean "the FHIRPath specification requires an error here",
    /// which is the only throw out of <c>Evaluate</c> that lets a <c>semantic</c>/<c>execution</c>-marked
    /// case pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FhirPathEvaluationException"/> is the type whose entire reason for existing is this
    /// distinction - see its own remarks: "the expression is wrong" versus "we are wrong". Its base
    /// <see cref="InvalidOperationException"/> is not accepted, and that asymmetry is load-bearing: the
    /// base type is what the engine throws for a broken internal invariant, so accepting it would let an
    /// engine defect count as conformance on any case the corpus happens to mark invalid.
    /// </para>
    /// <para>
    /// The list is deliberately narrower than the parser's. <see cref="FormatException"/> is a legitimate
    /// parse-time signal but an evaluation-time defect - <c>BoundaryFunctions</c> and
    /// <c>TypeConversionFunctions</c> both reach <c>int.Parse</c>/<c>decimal.Parse</c> on values they have
    /// already pattern-matched, so a <see cref="FormatException"/> out of <c>Evaluate</c> means one of
    /// those matches is wrong. One flat list across both phases would launder that as a pass.
    /// </para>
    /// <para>
    /// A bare <see cref="NotSupportedException"/> is not on either list, which is the rule this harness
    /// was rebuilt around: the generated dispatcher's default arm and the evaluator's binary-operator
    /// default arm both throw it for "not yet implemented", and an engine that cannot evaluate an
    /// expression has not refused it. <see cref="FhirPathFunctionNotSupportedException"/> is excluded by
    /// the same rule - it derives from <see cref="NotSupportedException"/>, not from either allowlisted
    /// type - and reaches a recorded skip through the marker catches that precede every call to this
    /// method, never through here.
    /// </para>
    /// <para>
    /// <see cref="OperationCanceledException"/> and <see cref="XunitException"/> fall out for free, which
    /// the previous denylist had to name or missed. An abandoned run now surfaces as a failure rather than
    /// as a recorded pass, consistent with <see cref="SkipUnlessTheCaseWouldNowPass"/> re-throwing it, and
    /// <c>Assert.Fail</c>'s <c>FailException</c> still cannot be mistaken for the engine reporting an
    /// invalid expression.
    /// </para>
    /// </remarks>
    private static bool IsEvaluatorSignalledError(Exception exception) =>
        exception is FhirPathEvaluationException;

    /// <summary>
    /// Records a case the suite exercises through a feature this engine deliberately does not implement.
    /// </summary>
    /// <remarks>
    /// The feature name is repeated into the reason rather than left implicit in the exception message,
    /// because the name is what the allowlist matched on and what a reader has to check the entry against.
    /// </remarks>
    private void SkipDeliberatelyUnsupportedFeature(FhirPathTestCase testCase, FhirPathFunctionNotSupportedException exception) =>
        SkipTest($"{testCase.Name}: '{exception.FeatureName}' is deliberately not implemented - {_deliberatelyUnsupportedFeatures[exception.FeatureName]} [{exception.Message}]");

    /// <summary>
    /// Records that this test case is deliberately not asserted, with the reason, and stops the test.
    /// </summary>
    /// <remarks>
    /// This reports as a real skip. xunit v2.9.3 has no working dynamic skip of its own - <c>Assert.Skip</c>
    /// does not exist and <c>xunit.execution</c> 2.9.3 does not honour its own <c>Xunit.Sdk.SkipException</c>'s
    /// <c>DynamicSkipToken</c> - so the deferrals used to report as passes, indistinguishable from real
    /// coverage. <c>Xunit.SkippableFact</c> supplies the missing mechanism: the <c>[SkippableTheory]</c>
    /// attribute on the three suite entry points installs a runner that translates a thrown
    /// <c>Xunit.SkipException</c> into a skipped result carrying the reason.
    /// The reason string remains the compensating control: every deferral is named, justified in
    /// <see cref="_unsignalledInvalidCases"/>, checked against the upstream suites by
    /// <see cref="OfficialTestSuiteSkipListTests"/>, and re-run by
    /// <see cref="AssertDeferralIsStillNeeded"/> so a gap that has since closed fails instead of skipping.
    /// </remarks>
    private void SkipTest(string reason)
    {
        _output.WriteLine($"[SKIPPED] {reason}");

        // Xunit.SkipException, not Xunit.Sdk.SkipException: only the former prefixes the message with
        // the DynamicSkipToken that SkippableFact's runner looks for. The latter is the one xunit 2.9.3
        // ships and ignores.
        throw new Xunit.SkipException(reason);
    }

    private static void ValidatePredicateResult(FhirPathTestCase testCase, IReadOnlyList<IElement> actualResults)
    {
        if (testCase.ExpectedOutputs.Count != 1 || testCase.ExpectedOutputs[0].Type != "boolean")
        {
            throw new InvalidOperationException($"Predicate test '{testCase.Name}' must declare a single boolean output.");
        }

        if (!bool.TryParse(testCase.ExpectedOutputs[0].Value, out var expectedValue))
        {
            throw new InvalidOperationException($"Predicate test '{testCase.Name}' has invalid expected boolean value '{testCase.ExpectedOutputs[0].Value}'.");
        }

        var actualValue = ConvertToPredicateBoolean(actualResults);
        if (actualValue != expectedValue)
        {
            var message = $"""
                Predicate mismatch in test '{testCase.Name}' (group: {testCase.GroupName})
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected predicate result: {expectedValue}
                Actual predicate result: {actualValue}
                Actual outputs: {FormatActualOutputs(actualResults.ToList())}
                """;
            throw new InvalidOperationException(message);
        }
    }

    private static bool ConvertToPredicateBoolean(IReadOnlyList<IElement> actualResults)
    {
        if (actualResults.Count == 0)
        {
            return false;
        }

        if (actualResults.Count == 1 && actualResults[0].Value is bool booleanValue)
        {
            return booleanValue;
        }

        return true;
    }

    private static void ValidateResults(FhirPathTestCase testCase, List<IElement> actualResults)
    {
        var expectedCount = testCase.ExpectedOutputs.Count;
        var actualCount = actualResults.Count;

        if (actualCount != expectedCount)
        {
            var message = $"""
                Result count mismatch in test '{testCase.Name}' (group: {testCase.GroupName})
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected {expectedCount} result(s), but got {actualCount}
                Expected outputs: {FormatExpectedOutputs(testCase.ExpectedOutputs)}
                Actual outputs: {FormatActualOutputs(actualResults)}
                """;
            throw new InvalidOperationException(message);
        }

        for (var i = 0; i < expectedCount; i++)
        {
            var expected = testCase.ExpectedOutputs[i];
            var actual = actualResults[i];

            ValidateResult(testCase, expected, actual, i);
        }
    }

    private static void ValidateResult(FhirPathTestCase testCase, ExpectedOutput expected, IElement actual, int index)
    {
        var expectedType = expected.Type;
        var expectedValue = expected.Value;

        var actualValue = actual.Value;
        var actualType = InferFhirPathType(actualValue);

        // If the value is a string but the element metadata says it's a temporal type, trust the metadata
        // This handles the case where the model returns raw values (no @ prefix)
        if (actualType == "string" && 
            (actual.InstanceType == "date" || actual.InstanceType == "dateTime" || actual.InstanceType == "time" || actual.InstanceType == "instant"))
        {
            actualType = actual.InstanceType;
        }

        if (!TypesMatch(expectedType, actualType, actualValue))
        {
            var message = $"""
                Type mismatch in test '{testCase.Name}' (group: {testCase.GroupName}) at output index {index}
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected type: {expectedType}
                Actual type: {actualType}
                Expected value: {expectedValue}
                Actual value: {actualValue ?? "(null)"}
                """;
            throw new InvalidOperationException(message);
        }

        if (!ValuesMatch(expectedValue, actualValue, expectedType))
        {
            var message = $"""
                Value mismatch in test '{testCase.Name}' (group: {testCase.GroupName}) at output index {index}
                Expression: {testCase.Expression}
                Input file: {testCase.InputFile}
                Expected: {expectedValue} (type: {expectedType})
                Actual: {actualValue ?? "(null)"} (type: {actualType})
                """;
            throw new InvalidOperationException(message);
        }
    }

    private static string InferFhirPathType(object? value)
    {
        return value switch
        {
            null => "null",
            bool => "boolean",
            int => "integer",
            long => "integer",
            decimal => "decimal",
            double => "decimal",
            string str when str.StartsWith('@') => ParseFhirPathTypePrefix(str),
            string => "string",
            FhirTemporal temporal => temporal.Kind switch
            {
                FhirPrimitive.Date => "date",
                FhirPrimitive.DateTime => "dateTime",
                FhirPrimitive.Instant => "instant",
                FhirPrimitive.Time => "time",
                _ => "dateTime"
            },
            // Named explicitly rather than left to the CLR-name fallback below: the suite asserts the
            // FHIRPath system type, which is Quantity regardless of what the carrier class is called.
            FhirQuantity => "Quantity",
            _ => value.GetType().Name
        };
    }

    private static string ParseFhirPathTypePrefix(string value)
    {
        if (value.StartsWith("@T", StringComparison.Ordinal))
        {
            return "time";
        }
        if (value.StartsWith('@') && value.Length > 1)
        {
            if (value.Contains('T', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
            {
                return "dateTime";
            }
            return "date";
        }
        return "string";
    }

    private static bool TypesMatch(string expectedType, string actualType, object? actualValue)
    {
        if (expectedType == actualType)
        {
            return true;
        }

        // If the test suite doesn't specify an expected type, accept any actual type
        if (expectedType == "unknown" || string.IsNullOrEmpty(expectedType))
        {
            return true;
        }

        if (expectedType == "code" && actualType == "string")
        {
            return true;
        }

        if (expectedType == "string" && actualType == "code")
        {
            return true;
        }

        // 'id' is a restricted string type in FHIR
        if (expectedType == "id" && actualType == "string")
        {
            return true;
        }

        if (expectedType == "string" && actualType == "id")
        {
            return true;
        }

        if (expectedType == "integer" && actualType == "decimal")
        {
            if (actualValue is decimal decValue && decValue == Math.Floor(decValue))
            {
                return true;
            }
        }

        if (expectedType == "decimal" && actualType == "integer")
        {
            return true;
        }

        if ((expectedType == "date" || expectedType == "dateTime") && actualType == "string" && actualValue is string str && str.StartsWith('@'))
        {
            return true;
        }

        // Boundary functions on dates may return 'date' type but test expects 'dateTime' type
        // This is acceptable when the value is a partial date like @2014-12 (year-month)
        if (expectedType == "dateTime" && actualType == "date")
        {
            return true;
        }

        return false;
    }

    private static bool ValuesMatch(string expectedValue, object? actualValue, string expectedType)
    {
        if (actualValue is null)
        {
            return string.IsNullOrEmpty(expectedValue);
        }

        var actualStr = actualValue.ToString();
        if (actualStr is null)
        {
            return string.IsNullOrEmpty(expectedValue);
        }

        if (expectedType is "date" or "dateTime" or "time")
        {
            return string.Equals(expectedValue, actualStr, StringComparison.Ordinal);
        }

        if (expectedType == "boolean")
        {
            return string.Equals(expectedValue, actualStr, StringComparison.OrdinalIgnoreCase);
        }

        if (expectedType is "integer" or "decimal")
        {
            if (decimal.TryParse(expectedValue, out var expectedDecimal) && decimal.TryParse(actualStr, out var actualDecimal))
            {
                return expectedDecimal == actualDecimal;
            }
        }

        // For unknown types, try numeric comparison if both look like numbers
        // This handles cases like "-0.0" vs "0.0" which are mathematically equal
        if (expectedType == "unknown" && 
            decimal.TryParse(expectedValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var expectedNum) && 
            decimal.TryParse(actualStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var actualNum))
        {
            return expectedNum == actualNum;
        }

        // For unknown types, try boolean comparison case-insensitively
        // This handles cases like "true" vs "True" for comparable() tests
        if (expectedType == "unknown" && 
            (expectedValue.Equals("true", StringComparison.OrdinalIgnoreCase) || expectedValue.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Equals(expectedValue, actualStr, StringComparison.OrdinalIgnoreCase);
        }

        // For unknown types that look like temporal values, normalize them
        // This handles FHIRPath boundary test cases where expected has @ prefix
        if (expectedType == "unknown" && expectedValue.StartsWith('@'))
        {
            return NormalizeTemporalValue(expectedValue) == NormalizeTemporalValue(actualStr);
        }

        return string.Equals(expectedValue, actualStr, StringComparison.Ordinal);
    }

    private static string NormalizeTemporalValue(string value)
    {
        // Strip @ prefix (FHIRPath literal syntax)
        value = value.TrimStart('@');
        // Strip T prefix from time values (FHIRPath syntax, not part of value)
        if (value.StartsWith('T'))
            value = value.Substring(1);
        return value;
    }

    private static string FormatExpectedOutputs(IReadOnlyList<ExpectedOutput> outputs)
    {
        if (outputs.Count == 0)
        {
            return "(empty collection)";
        }

        return string.Join(", ", outputs.Select(o => $"{o.Value} ({o.Type})"));
    }

    private static string FormatActualOutputs(List<IElement> results)
    {
        if (results.Count == 0)
        {
            return "(empty collection)";
        }

        return string.Join(", ", results.Select(r => $"{r.Value ?? "(null)"} ({InferFhirPathType(r.Value)})"));
    }
}
