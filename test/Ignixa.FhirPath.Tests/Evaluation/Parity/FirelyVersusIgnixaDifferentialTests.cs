/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Differential harness holding Ignixa's engine against Firely 5.11.4 - the engine ADR 2608
 * (microsoft/fhir-server) commits to replacing behind a seam.
 *
 * The two existing differential harnesses compare Ignixa against itself. This one crosses engines,
 * which changes what a failure means. Ignixa is not required to match Firely: where Ignixa is more
 * spec-compliant it keeps its behaviour and the seam adapts. So the output is an inventory, not a
 * verdict - and the thing that decides whether an entry costs anything is whether a shipped
 * SearchParameter expression can reach it, because those run on every write.
 */

using System.IO;

namespace Ignixa.FhirPath.Tests.Evaluation.Parity;

/// <summary>
/// Pins every known disagreement between Firely 5.11.4 and Ignixa on the corpora that matter to the
/// seam, so a new one fails the build instead of shipping unnoticed.
/// </summary>
/// <remarks>
/// A differential harness detects <em>divergence</em>, never <em>shared wrongness</em>: two engines
/// that are wrong in the same way agree with each other and produce no signal here. A green run over
/// this suite means Firely and Ignixa answer identically on the corpus exercised - it is a
/// migration-risk signal for ADR 2608's seam, telling you where behaviour would change if the seam
/// swapped providers, not a correctness proof for either engine. The FHIRPath conformance suites
/// elsewhere in this project are what establish correctness against the spec; this one only compares
/// the two engines to each other.
/// </remarks>
public class FirelyVersusIgnixaDifferentialTests
{
    /// <summary>
    /// Sweeps the expressions that actually run in production and pins what they disagree on.
    /// </summary>
    [Fact]
    public void GivenTheShippedSearchParameterCorpus_WhenEvaluatedByBothEngines_ThenOnlyPinnedDivergencesAppear()
    {
        // Arrange & Act
        var corpus = FirelyParityFixture.SearchParameterExpressions;
        var divergences = ParitySweep.Run(corpus, "searchparam");

        // Assert
        AssertPinned(divergences, KnownDivergences.SearchParameterSignatures, "searchparam", corpus.Count);
    }

    /// <summary>
    /// Sweeps the language constructs this branch changed.
    /// </summary>
    [Fact]
    public void GivenTheChangedConstructs_WhenEvaluatedByBothEngines_ThenOnlyPinnedDivergencesAppear()
    {
        // Arrange & Act
        var corpus = FirelyParityFixture.ConstructCorpus;
        var divergences = ParitySweep.Run(corpus, "construct");

        // Assert
        AssertPinned(divergences, KnownDivergences.ConstructSignatures, "construct", corpus.Count);
    }

    /// <summary>
    /// Pins raw primitive-name divergences <see cref="ParityTypeName"/> normalises away, so
    /// normalising them does not amount to hiding them.
    /// </summary>
    [Theory]
    [InlineData("active and true", "System.Boolean", "boolean")]
    [InlineData("'a' & 'b'", "System.String", "string")]
    [InlineData("1 + 1", "System.Integer", "integer")]
    [InlineData("birthDate + 1 year", "System.Date", "date")]
    public void GivenAnOperatorResult_WhenTypedByBothEngines_ThenFirelyNamesTheSystemTypeAndIgnixaTheFhirType(
        string expression,
        string firelyType,
        string ignixaType)
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;

        // Act
        var firely = FirelyEngine.RawInstanceTypes(FirelyEngine.Parse(patient), expression);
        var ignixa = IgnixaEngine.RawInstanceTypes(IgnixaEngine.Parse(patient), expression);

        // Assert
        firely.ShouldBe([firelyType]);
        ignixa.ShouldBe([ignixaType]);
    }

    /// <summary>
    /// ADR 2608 pins this one by name: 5.11.4's <c>Scalar</c> calls <c>Single()</c>, so two results
    /// throw, where SDK 6 returns null. Ignixa matches SDK 6, so a seam that derived
    /// <c>Scalar</c> from Ignixa rather than reimplementing 5.11.4's contract would silently stop
    /// throwing on ambiguous search parameter definitions.
    /// </summary>
    [Fact]
    public void GivenAnExpressionWithTwoResults_WhenTakenAsAScalar_ThenFirelyThrowsAndIgnixaReturnsNull()
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;

        // Act
        var firely = FirelyEngine.ScalarOutcome(FirelyEngine.Parse(patient), "name.family");
        var ignixa = IgnixaEngine.ScalarOutcome(IgnixaEngine.Parse(patient), "name.family");

        // Assert
        firely.ShouldBe("threw InvalidOperationException");
        ignixa.ShouldBe("<null>");
    }

    /// <summary>
    /// <c>Predicate</c> and <c>IsTrue</c> disagree with each other on empty within Firely itself.
    /// This asserts against <see cref="IgnixaEngine.IsTrue"/>, not an Ignixa <c>Predicate</c> -
    /// <see cref="IgnixaEngine"/> exposes no <c>Predicate</c> wrapper. Ignixa's own <c>Predicate</c>
    /// methods (two of them, disagreeing with each other) are why ADR 2608 derives <c>Predicate</c>
    /// in the seam rather than asking the provider for it; see docs/features/fhirpath/firely-parity.md,
    /// entry 7.
    /// </summary>
    [Fact]
    public void GivenAnEmptyResult_WhenAskedAsPredicateAndAsIsTrue_ThenFirelyDisagreesWithItselfAndIgnixaMatchesIsTrue()
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;
        var firelySubject = FirelyEngine.Parse(patient);

        // Act
        var predicate = FirelyEngine.Predicate(firelySubject, "missingElement");
        var firelyIsTrue = FirelyEngine.IsTrue(firelySubject, "missingElement");
        var ignixaIsTrue = IgnixaEngine.IsTrue(IgnixaEngine.Parse(patient), "missingElement");

        // Assert
        predicate.ShouldBeTrue();
        firelyIsTrue.ShouldBeFalse();
        ignixaIsTrue.ShouldBe(firelyIsTrue);
    }

    /// <summary>
    /// Refutes the expectation that Ignixa cannot resolve <c>%resource</c> because
    /// <c>IElement</c> has no parent link. It resolves, including from inside a Bundle entry - but
    /// only because the bridge binds it explicitly. This test is what stops that binding being
    /// dropped as redundant.
    /// </summary>
    [Theory]
    [InlineData("%resource.id")]
    [InlineData("%rootResource.id")]
    [InlineData("%context.id")]
    [InlineData("Bundle.entry.resource.select(%resource.id)")]
    [InlineData("Bundle.entry.resource.ofType(Patient).name.select(%rootResource.id)")]
    public void GivenAResourceVariable_WhenResolvedByBothEngines_ThenTheyAgree(string expression)
    {
        // Arrange
        var bundle = FirelyParityFixture.Resources.First(resource => resource.Name == "Bundle").Json;

        // Act
        var firely = FirelyEngine.Evaluate(FirelyEngine.Parse(bundle), expression);
        var ignixa = IgnixaEngine.Evaluate(IgnixaEngine.Parse(bundle), expression);

        // Assert
        ignixa.Describe().ShouldBe(firely.Describe());
    }

    /// <summary>
    /// The high boundary of a year is its December, and both engines now agree on the date.
    /// </summary>
    /// <remarks>
    /// This was a pinned Ignixa defect: FormatDateTimeHighBoundary maximised the month only when the
    /// requested output precision was exactly month level, so the default full-precision call left an
    /// unspecified month at its parsed default of January and answered 2012-01-31. Every other
    /// component in that method already used a "this precision or finer" test; month was the only
    /// equality check. Nothing but the timezone offset now separates the two engines here, which is
    /// the same benign difference the other boundary entries carry.
    /// </remarks>
    [Fact]
    public void GivenAYearPrecisionDate_WhenTakingItsHighBoundary_ThenBothEnginesReportDecember()
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;

        // Act
        var firely = FirelyEngine.RawValues(FirelyEngine.Parse(patient), "@2012.highBoundary()");
        var ignixa = IgnixaEngine.RawValues(IgnixaEngine.Parse(patient), "@2012.highBoundary()");

        // Assert
        firely.ShouldBe(["2012-12-31T23:59:59.999"]);
        ignixa.ShouldBe(["2012-12-31T23:59:59.999-12:00"]);
    }

    /// <summary>
    /// Guards the neighbours of the year-precision fix: coarser input must not start borrowing the
    /// month, and finer input must keep the month it was given.
    /// </summary>
    [Theory]
    [InlineData("@2012.highBoundary()", "2012-12-31T23:59:59.999-12:00")]
    [InlineData("@2012.highBoundary(6)", "2012-12")]
    [InlineData("@2012.highBoundary(8)", "2012-12-31")]
    [InlineData("@2012-06.highBoundary()", "2012-06-30T23:59:59.999-12:00")]
    [InlineData("@2012-02.highBoundary(8)", "2012-02-29")]
    [InlineData("@2011-02.highBoundary(8)", "2011-02-28")]
    [InlineData("@2012-06-15.highBoundary()", "2012-06-15T23:59:59.999-12:00")]
    public void GivenADateOfSomePrecision_WhenTakingItsHighBoundary_ThenMaximisesOnlyUnspecifiedComponents(
        string expression,
        string expected)
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;

        // Act
        var ignixa = IgnixaEngine.RawValues(IgnixaEngine.Parse(patient), expression);

        // Assert
        ignixa.ShouldBe([expected]);
    }

    private static void AssertPinned(
        IReadOnlyList<ParityDivergence> divergences,
        IReadOnlyDictionary<string, int> pinned,
        string source,
        int expressions)
    {
        var report = ParityReport.Render(divergences, expressions, FirelyParityFixture.Resources.Count);
        var path = Path.Combine(AppContext.BaseDirectory, $"firely-parity-{source}.md");
        File.WriteAllText(path, report);

        var observed = divergences
            .GroupBy(divergence => divergence.Signature)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var pinBlock = RenderPinBlock(observed);

        var unpinned = observed.Keys.Where(signature => !pinned.ContainsKey(signature)).ToList();
        unpinned.ShouldBeEmpty(
            $"New Firely/Ignixa divergence(s) appeared. Add an inventory entry in "
            + $"docs/features/fhirpath/firely-parity.md, then paste this into KnownDivergences:\n{pinBlock}\n\nReport at {path}");

        var moved = observed
            .Where(entry => pinned[entry.Key] != entry.Value)
            .Select(entry => $"{entry.Key}: pinned {pinned[entry.Key]}, observed {entry.Value}")
            .ToList();
        moved.ShouldBeEmpty(
            $"Divergence reach changed - a known behaviour now affects a different number of subject "
            + $"resources. Paste into KnownDivergences:\n{pinBlock}\n\nReport at {path}");

        var vanished = pinned.Keys.Where(signature => !observed.ContainsKey(signature)).ToList();
        vanished.ShouldBeEmpty(
            $"Pinned divergence(s) no longer occur - remove them from KnownDivergences and from the "
            + $"inventory. Report at {path}");
    }

    /// <summary>
    /// Emits the observed divergences as the C# literal that pins them, so keeping the expectations
    /// current is a copy-paste rather than a transcription exercise.
    /// </summary>
    private static string RenderPinBlock(IReadOnlyDictionary<string, int> observed)
    {
        var lines = observed
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"            [\"{entry.Key.Replace("\"", "\\\"", StringComparison.Ordinal)}\"] = {entry.Value},");

        return string.Join("\n", lines);
    }
}
