/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Guards the two skip lists in OfficialTestSuiteRunner.
 *
 * Skipping an official invalid-expression case reports as a pass in xunit v2, so the lists themselves
 * need teeth: an entry naming a test that no longer exists upstream would silently defer nothing while
 * still reading as a documented gap, and an entry naming a feature that has since been implemented would
 * catch nothing while still reading as a deliberate exclusion.
 */

using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Tests.TestHelpers;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests;

public class OfficialTestSuiteSkipListTests
{
    /// <summary>
    /// Checks every deferred entry still names a real, still-invalid official case.
    /// </summary>
    /// <remarks>
    /// A <see cref="TheoryAttribute"/> over the deferral list would give one case per entry and nicer
    /// failure isolation, but xunit fails a theory whose data source is empty ("No data found"). The list
    /// is currently empty because every deferral has been closed, so the guard has to survive its own
    /// success - a guard that breaks when there is nothing left to guard would push the next person to
    /// delete it. Failures are therefore accumulated and reported together, which keeps the per-entry
    /// diagnostic that the theory form gave.
    /// </remarks>
    [Fact]
    public void GivenTheDeferredInvalidCases_WhenLookedUpInTheOfficialSuites_ThenEachExistsAndIsMarkedInvalid()
    {
        // Arrange
        var allCases = AllTestCases().ToList();

        // Act
        var stale = new List<string>();
        foreach (var testName in OfficialTestSuiteRunner.DeferredInvalidCaseNames)
        {
            var matches = allCases.Where(tc => tc.Name == testName).ToList();

            if (matches.Count == 0)
            {
                stale.Add($"'{testName}' is deferred but no official R4/R4B/R5 test has that name - the entry is stale and should be removed.");
            }
            else if (!matches.Any(tc => tc.IsInvalidTest))
            {
                stale.Add($"'{testName}' is deferred as an unsignalled invalid case but no version marks it invalid - the entry no longer describes a real gap.");
            }
        }

        // Assert
        stale.ShouldBeEmpty(string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void GivenTheDeferralList_WhenInspected_ThenEveryEntryCarriesAReason()
    {
        // Arrange & Act
        var reasons = OfficialTestSuiteRunner.DeferredInvalidCaseReasons;

        // Assert
        reasons.ShouldAllBe(entry => !string.IsNullOrWhiteSpace(entry.Value));
        reasons.ShouldAllBe(entry => entry.Value.Length > 40);
    }

    /// <summary>
    /// The expression that reaches each deliberately unsupported feature, so the guard below can invoke
    /// every one of them.
    /// </summary>
    /// <remarks>
    /// Kept separate from the allowlist itself and reconciled against it by
    /// <see cref="GivenTheDeliberatelyUnsupportedFeatures_WhenInspected_ThenEachHasAProbeAndAReason"/>.
    /// An allowlist entry with no probe would be an entry nothing ever exercises, which is the same
    /// unfalsifiable shape the allowlist exists to remove.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> _featureProbes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["conformsTo"] = "conformsTo('http://hl7.org/fhir/StructureDefinition/Patient')",
        ["memberOf"] = "Patient.gender.memberOf('http://hl7.org/fhir/ValueSet/administrative-gender')",
        ["validateVS"] = "Patient.gender.validateVS('http://hl7.org/fhir/ValueSet/administrative-gender')",
        ["translate"] = "Patient.gender.translate('http://hl7.org/fhir/ConceptMap/cm-administrative-gender-v2', true)",
        ["hasTemplateIdOf"] = "hasTemplateIdOf('http://hl7.org/cda/us/ccda/StructureDefinition/ContinuityofCareDocumentCCD')",
        ["%terminologies"] = "%terminologies.expand('http://hl7.org/fhir/ValueSet/administrative-gender')",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Invokes every deliberately unsupported feature and fails if the engine no longer refuses it by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the allowlist self-retiring. The runner skips a case when the engine throws
    /// <see cref="FhirPathFunctionNotSupportedException"/> naming a listed feature; implementing that
    /// feature removes the throw, so the runner's catch stops firing and the entry becomes an allowlist
    /// line that guards nothing while still reading as a recorded exclusion. Nothing in the runner can
    /// notice that, for the same reason <see cref="OfficialTestSuiteRunner"/>'s deferral list needed
    /// <c>AssertDeferralIsStillNeeded</c>: an absent skip looks exactly like a skip that was never needed.
    /// </para>
    /// <para>
    /// So the guard asserts the throw directly rather than through a suite case. It fails the moment a
    /// listed feature starts returning a value, which forces the entry out of the allowlist and the
    /// feature's official cases back into the asserted population.
    /// </para>
    /// <para>
    /// The exception type is asserted as exactly <see cref="FhirPathFunctionNotSupportedException"/>, not
    /// as an assignable <see cref="NotSupportedException"/>. A bare <see cref="NotSupportedException"/>
    /// from one of these sites is the original defect returning: it is what the runner is now required to
    /// fail on, so a guard that accepted it would re-open the hole it was written to close.
    /// </para>
    /// </remarks>
    [Fact]
    public void GivenTheDeliberatelyUnsupportedFeatures_WhenEachIsInvoked_ThenTheEngineStillRefusesByName()
    {
        // Arrange
        var parser = new FhirPathParser();
        var evaluator = new FhirPathEvaluator();
        var schemaProvider = FhirVersion.R4.GetSchemaProvider();
        var patient = ResourceJsonNode
            .Parse("""{"resourceType":"Patient","id":"pat1","gender":"male"}""")
            .ToElement(schemaProvider);

        // Act
        var stale = new List<string>();
        foreach (var (featureName, probe) in _featureProbes)
        {
            var refusal = CaptureRefusal(parser, evaluator, schemaProvider, patient, probe);

            if (refusal is null)
            {
                stale.Add($"'{featureName}' no longer throws for `{probe}` - it is implemented or otherwise reachable, so its allowlist entry in OfficialTestSuiteRunner must be removed and its official cases asserted.");
            }
            else if (refusal is not FhirPathFunctionNotSupportedException marker)
            {
                stale.Add($"'{featureName}' threw {refusal.GetType().Name} for `{probe}` rather than FhirPathFunctionNotSupportedException, so the runner cannot tell it from an engine gap: {refusal.Message}");
            }
            else if (!string.Equals(marker.FeatureName, featureName, StringComparison.Ordinal))
            {
                stale.Add($"'{featureName}' threw a marker naming '{marker.FeatureName}' for `{probe}`, so the allowlist key and the engine disagree and the skip will not match.");
            }
        }

        // Assert
        stale.ShouldBeEmpty(string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void GivenTheDeliberatelyUnsupportedFeatures_WhenInspected_ThenEachHasAProbeAndAReason()
    {
        // Arrange & Act
        var features = OfficialTestSuiteRunner.DeliberatelyUnsupportedFeatures;

        // Assert
        features.Keys.OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(_featureProbes.Keys.OrderBy(name => name, StringComparer.Ordinal));
        features.ShouldAllBe(entry => !string.IsNullOrWhiteSpace(entry.Value));
        features.ShouldAllBe(entry => entry.Value.Length > 40);
    }

    /// <summary>
    /// Drives the runner with an expression whose function does not exist, and fails unless the case is
    /// reported as a failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the guard for the defect itself, and it pins the edge the allowlist guard above does not:
    /// that a <see cref="NotSupportedException"/> carrying no marker still fails. Re-broadening the catch
    /// in <see cref="OfficialTestSuiteRunner"/> back to <c>catch (NotSupportedException)</c> turns this
    /// red, which is what the hand-run "delete a binary-operator switch arm" mutation demonstrated once
    /// and could not keep demonstrating.
    /// </para>
    /// <para>
    /// The probe reaches a real production throw site with no engine mutation and no test seam.
    /// <c>FhirPathFunctionGenerator</c> emits the generated dispatcher's default arm as
    /// <c>_ =&gt; throw new NotSupportedException($"Function '{functionName}' is not yet implemented")</c>,
    /// so any unregistered function name lands there. It is not a hypothetical path: <c>htmlChecks()</c>
    /// reaches it on every supported version through <c>txt-1</c>/<c>txt-2</c>, which is why
    /// <c>FhirPathInvariantCheck</c> names that function first in its own catch.
    /// </para>
    /// <para>
    /// Both assertions matter and they fail for different regressions. A <see langword="null"/> exception
    /// means the runner swallowed the throw and recorded a pass - the original defect. A
    /// <c>SkipException</c> means it recorded a skip, which would be the same laundering wearing an
    /// honest label: an engine gap accounted for as a deliberate exclusion.
    /// </para>
    /// </remarks>
    [Fact]
    public void GivenAnUnregisteredFunction_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseFails()
    {
        // Arrange
        var runner = new OfficialTestSuiteRunner(new NullTestOutputHelper());
        var testCase = ProbeCase("probeUnregisteredFunction", "Patient.notARealFunctionAtAll()");

        // Act
        var thrown = Record.Exception(() => runner.OfficialTestSuite_R4(testCase));

        // Assert
        thrown.ShouldNotBeNull("An unregistered function threw NotSupportedException and the runner recorded a pass. That is the defect this runner was fixed for: the catch is scoped by exception type again.");
        thrown.ShouldNotBeOfType<Xunit.SkipException>();
        thrown.InnerException.ShouldNotBeNull().GetType().ShouldBe(typeof(NotSupportedException));
    }

    /// <summary>
    /// Drives the runner with an allowlisted feature and fails unless the case is reported as a skip.
    /// </summary>
    /// <remarks>
    /// The mirror of the guard above, and the reason it cannot be satisfied by simply failing everything.
    /// Together the two pin both edges of the discriminator: a marker naming a listed feature skips, and
    /// anything else fails. Either one alone is satisfiable by a runner that has stopped discriminating.
    /// </remarks>
    [Fact]
    public void GivenAnAllowlistedFeature_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseSkips()
    {
        // Arrange
        var runner = new OfficialTestSuiteRunner(new NullTestOutputHelper());
        var testCase = ProbeCase("probeAllowlistedFeature", "Patient.gender.memberOf('http://hl7.org/fhir/ValueSet/administrative-gender')");

        // Act
        var thrown = Record.Exception(() => runner.OfficialTestSuite_R4(testCase));

        // Assert
        var skip = thrown.ShouldBeOfType<Xunit.SkipException>();
        skip.Message.ShouldContain("memberOf");
    }

    /// <summary>
    /// The mirror of <see cref="GivenAnUnregisteredFunction_WhenRunThroughTheOfficialSuiteRunner_ThenTheCaseFails"/>
    /// on the <c>invalid</c>-marked path, and the reason that guard is not the whole story.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runner's discriminator has three edges, not two. Both guards above hand it a case with
    /// <c>IsInvalidTest: false</c>, so both enter <c>ExecuteTestCase</c> and only ever exercise the marker
    /// catch on that path. An <c>invalid</c>-marked case takes a different arm entirely -
    /// <c>RunInvalidExpressionTest</c> - which has its own catches and had its own copy of the defect: a
    /// bare <see cref="NotSupportedException"/> satisfied the old two-type denylist, produced
    /// <c>[INVALID-OK]</c>, and returned, which xunit records as a pass. Deleting the <c>"&amp;"</c> arm
    /// from the evaluator's binary-operator switch left <c>testConcatenate4</c> green on all three
    /// versions for exactly that reason.
    /// </para>
    /// <para>
    /// 114 of the corpus's cases are <c>invalid</c>-marked, so this is the arm where a pass-on-any-throw
    /// costs the most: every one of them would accept an engine that cannot evaluate the expression at all
    /// as evidence that it correctly refused it.
    /// </para>
    /// </remarks>
    [Fact]
    public void GivenAnUnregisteredFunction_WhenRunAsAnInvalidMarkedCase_ThenTheCaseFails()
    {
        // Arrange
        var runner = new OfficialTestSuiteRunner(new NullTestOutputHelper());
        var testCase = ProbeCase("probeUnregisteredFunctionInvalid", "Patient.notARealFunctionAtAll()", invalidType: "execution");

        // Act
        var thrown = Record.Exception(() => runner.OfficialTestSuite_R4(testCase));

        // Assert
        thrown.ShouldNotBeNull("An unregistered function threw NotSupportedException on the invalid-marked path and the runner recorded a pass. 'The engine threw' is not 'the engine refused the expression': the catches on that path are scoped by exception type again.");
        thrown.ShouldNotBeOfType<Xunit.SkipException>();
        thrown.GetType().ShouldBe(typeof(NotSupportedException));
    }

    /// <summary>
    /// Drives the <c>invalid</c>-marked path with an allowlisted feature and fails unless the case is
    /// reported as a skip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fourth edge, and the one that was held by a corpus coincidence rather than by a guard. The skip
    /// that <c>testConformsTo3</c> now reports comes from the marker catch inside
    /// <c>RunInvalidExpressionTest</c>, not from the allowlists - the allowlists would reject the marker,
    /// but rejecting it produces a failure. Two mechanisms therefore have to hold for that case to skip,
    /// and until this guard existed, removing both together turned the suite fully green again while
    /// restoring the founding defect: the only visible trace was the skip count dropping from 16 to 13,
    /// and the 16 lives in a documentation table, which is prose, not a guard.
    /// </para>
    /// <para>
    /// <c>ShouldBeOfType</c> rather than <c>ShouldNotBeNull</c> is the point: the failure mode being
    /// pinned is the marker escaping into the engine-error allowlists and being recorded as
    /// <c>[INVALID-OK]</c>, which throws nothing at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void GivenAnAllowlistedFeature_WhenRunAsAnInvalidMarkedCase_ThenTheCaseSkips()
    {
        // Arrange
        var runner = new OfficialTestSuiteRunner(new NullTestOutputHelper());
        var testCase = ProbeCase(
            "probeAllowlistedFeatureInvalid",
            "Patient.gender.memberOf('http://hl7.org/fhir/ValueSet/administrative-gender')",
            invalidType: "execution");

        // Act
        var thrown = Record.Exception(() => runner.OfficialTestSuite_R4(testCase));

        // Assert
        var skip = thrown.ShouldBeOfType<Xunit.SkipException>();
        skip.Message.ShouldContain("memberOf");
    }

    /// <summary>
    /// Every case the suite is expected to skip, keyed by <c>version|case name</c>, with a fragment of the
    /// reason that identifies which mechanism produced the skip.
    /// </summary>
    /// <remarks>
    /// The fragments are the point, not the count. A skip count alone rots the first time a legitimate
    /// skip is added and removed in the same change, and it cannot tell a feature skip from a
    /// version-policy skip - which is exactly the confusion a laundered pass hides inside.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> _expectedSkips = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["R4|testPlusDate19"] = "R4/R4B expect truncation of fractional seconds",
        ["R4|testFHIRPathAsFunction21"] = "the 'as' singleton rule is enforced from R5 onwards",
        ["R4|testConformsTo1"] = "'conformsTo' is deliberately not implemented",
        ["R4|testConformsTo2"] = "'conformsTo' is deliberately not implemented",
        ["R4|testConformsTo3"] = "'conformsTo' is deliberately not implemented",
        ["R4B|testPlusDate19"] = "R4/R4B expect truncation of fractional seconds",
        ["R4B|testFHIRPathAsFunction21"] = "the 'as' singleton rule is enforced from R5 onwards",
        ["R4B|testConformsTo1"] = "'conformsTo' is deliberately not implemented",
        ["R4B|testConformsTo2"] = "'conformsTo' is deliberately not implemented",
        ["R4B|testConformsTo3"] = "'conformsTo' is deliberately not implemented",
        ["R5|testConformsTo1"] = "'conformsTo' is deliberately not implemented",
        ["R5|testConformsTo2"] = "'conformsTo' is deliberately not implemented",
        ["R5|testConformsTo3"] = "'conformsTo' is deliberately not implemented",
        ["R5|txTest01"] = "'%terminologies' is deliberately not implemented",
        ["R5|txTest02"] = "'%terminologies' is deliberately not implemented",
        ["R5|txTest03"] = "'%terminologies' is deliberately not implemented",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Drives the whole official corpus through the runner and fails unless exactly the recorded cases
    /// skip, each for the recorded reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The skip population is the one number in this suite that can move without anything failing. A case
    /// converted from skip to pass is invisible - the pass count rises, the skip count falls, and both are
    /// only written down in a documentation table. That is not hypothetical: reverting the two mechanisms
    /// that make <c>testConformsTo3</c> skip produced a fully green canonical suite,
    /// <c>Failed: 0, Passed: 2887, Skipped: 13</c>, with the founding defect restored and nothing red.
    /// </para>
    /// <para>
    /// The mirror probes pin that specific case by construction. This pins the population, which is the
    /// part that generalises: a skip appearing where none was recorded means the harness stopped asserting
    /// a case and nobody said so, and one disappearing means either a gap closed - in which case its entry
    /// belongs in this table's history, not silently absent - or an assertion started passing for a reason
    /// nobody chose.
    /// </para>
    /// <para>
    /// Non-skip exceptions are deliberately swallowed. Whether a case passes or fails is the suite's job
    /// and it reports that per case; duplicating it here would turn one red suite into two, with this one
    /// naming the wrong cause. The cost is a second full corpus evaluation, a few seconds per framework.
    /// </para>
    /// </remarks>
    [Fact]
    public void GivenTheWholeOfficialCorpus_WhenRunThroughTheRunner_ThenExactlyTheRecordedCasesSkipForTheRecordedReasons()
    {
        // Arrange
        var runner = new OfficialTestSuiteRunner(new NullTestOutputHelper());

        // Act
        var observed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (version, testCase) in AllRunnableCases())
        {
            var thrown = Record.Exception(() => Run(runner, version, testCase));
            if (thrown is Xunit.SkipException skip)
            {
                observed[$"{version}|{testCase.Name}"] = skip.Message;
            }
        }

        // Assert
        var drift = new List<string>();
        foreach (var key in observed.Keys.Except(_expectedSkips.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            drift.Add($"'{key}' now skips and is not recorded: {observed[key]}");
        }

        foreach (var key in _expectedSkips.Keys.Except(observed.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            drift.Add($"'{key}' is recorded as a skip but no longer skips. If its gap closed, remove the entry; if it started passing for another reason, find out which.");
        }

        foreach (var (key, fragment) in _expectedSkips)
        {
            if (observed.TryGetValue(key, out var reason) && !reason.Contains(fragment, StringComparison.Ordinal))
            {
                drift.Add($"'{key}' skips for a different reason than recorded. Expected to contain '{fragment}', got: {reason}");
            }
        }

        drift.ShouldBeEmpty(string.Join(Environment.NewLine, drift));
    }

    private static void Run(OfficialTestSuiteRunner runner, string version, FhirPathTestCase testCase)
    {
        switch (version)
        {
            case "R4":
                runner.OfficialTestSuite_R4(testCase);
                break;
            case "R4B":
                runner.OfficialTestSuite_R4B(testCase);
                break;
            default:
                runner.OfficialTestSuite_R5(testCase);
                break;
        }
    }

    /// <summary>
    /// The runner's own discovery output, so this guard sees exactly the population the suite runs -
    /// including anything the discovery filter drops.
    /// </summary>
    private static IEnumerable<(string Version, FhirPathTestCase TestCase)> AllRunnableCases()
    {
        foreach (var row in OfficialTestSuiteRunner.GetR4TestCases())
        {
            yield return ("R4", (FhirPathTestCase)row[0]);
        }

        foreach (var row in OfficialTestSuiteRunner.GetR4BTestCases())
        {
            yield return ("R4B", (FhirPathTestCase)row[0]);
        }

        foreach (var row in OfficialTestSuiteRunner.GetR5TestCases())
        {
            yield return ("R5", (FhirPathTestCase)row[0]);
        }
    }

    /// <summary>
    /// A minimal non-predicate case with no input file, so the runner uses its default patient and reaches
    /// evaluation without any corpus dependency. <paramref name="invalidType"/> selects which of the
    /// runner's two arms the probe drives: <see langword="null"/> for the normal path through
    /// <c>ExecuteTestCase</c>, a value for the <c>invalid</c>-marked path through
    /// <c>RunInvalidExpressionTest</c>.
    /// </summary>
    private static FhirPathTestCase ProbeCase(string name, string expression, string? invalidType = null) => new(
        Name: name,
        GroupName: "runner-discrimination-probes",
        Expression: expression,
        InputFile: null,
        ExpectedOutputs: [new ExpectedOutput("boolean", "true")],
        IsInvalidTest: invalidType is not null,
        InvalidType: invalidType,
        Ordered: true,
        Predicate: false,
        Description: null,
        Mode: null);

    /// <summary>
    /// Evaluates a probe and returns whatever it threw, or <see langword="null"/> when it produced a
    /// result. Enumerating is required: the evaluator is lazy, so a throw from the function body only
    /// surfaces on iteration.
    /// </summary>
    private static Exception? CaptureRefusal(
        FhirPathParser parser,
        FhirPathEvaluator evaluator,
        IFhirSchemaProvider schemaProvider,
        IElement patient,
        string probe)
    {
        try
        {
            var expression = parser.Parse(probe);
            var context = new FhirEvaluationContext { Resource = patient, Schema = schemaProvider };
            _ = evaluator.Evaluate(patient, expression, context).ToList();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>
    /// Every official case across the three versions, read through the same provenance and per-file hash
    /// pin the suite entry points use.
    /// </summary>
    /// <remarks>
    /// The pin is not incidental here. These guards claim each deferred entry still names a real,
    /// still-invalid official case, and that claim is only worth what the corpus behind it is worth -
    /// measured, an edited expected output in <c>tests-fhir-r4.xml</c> failed all three suite entry points
    /// on the hash while this class reported all green, because it reached the XML by its own path.
    /// A missing file throws rather than shrinking the population silently, for the same reason: an
    /// absent version would otherwise leave every entry from it unchecked and nothing would say so.
    /// </remarks>
    private static IEnumerable<FhirPathTestCase> AllTestCases()
    {
        OfficialTestSuiteRunner.EnsureCorpusProvenance();

        foreach (var version in new[] { "r4", "r4b", "r5" })
        {
            var path = Path.Combine(
                OfficialTestSuiteRunner.ProjectRoot,
                "TestData",
                "fhir-test-cases",
                version,
                "fhirpath",
                $"tests-fhir-{version}.xml");

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Official {version} suite file is missing: {path}. These guards check the deferral list against the official corpus, so a missing version has to fail rather than reduce what is checked.",
                    path);
            }

            foreach (var testCase in FhirPathTestSuiteParser.ParseTestSuite(path))
            {
                yield return testCase;
            }
        }
    }
}
