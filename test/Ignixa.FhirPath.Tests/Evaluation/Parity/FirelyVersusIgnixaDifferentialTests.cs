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
    /// Pins the one divergence <see cref="ParityTypeName"/> normalises away, so normalising it does
    /// not amount to hiding it.
    /// </summary>
    [Theory]
    [InlineData("active and true", "System.Boolean", "boolean")]
    [InlineData("'a' & 'b'", "System.String", "string")]
    [InlineData("1 + 1", "System.Integer", "integer")]
    [InlineData("birthDate + 1 year", "System.Date", "date")]
    [InlineData("1 'mg'", "System.Quantity", "Quantity")]
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
    /// Ignixa ships no <c>Predicate</c>, which is why ADR 2608 derives it in the seam rather than
    /// asking the provider for it.
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
    /// The high boundary of a year is its December, not its January. Ignixa answers 2012-01-31, which
    /// is a defect rather than a defensible difference - pinned here so the inventory entry has a
    /// reproducing test and so fixing it is noticed.
    /// </summary>
    [Fact]
    public void GivenAYearPrecisionDate_WhenTakingItsHighBoundary_ThenIgnixaReportsJanuaryWhereFirelyReportsDecember()
    {
        // Arrange
        var patient = FirelyParityFixture.Resources[0].Json;

        // Act
        var firely = FirelyEngine.RawValues(FirelyEngine.Parse(patient), "@2012.highBoundary()");
        var ignixa = IgnixaEngine.RawValues(IgnixaEngine.Parse(patient), "@2012.highBoundary()");

        // Assert
        firely.ShouldBe(["2012-12-31T23:59:59.999"]);
        ignixa.ShouldBe(["2012-01-31T23:59:59.999-12:00"]);

        // Month precision is handled correctly, which localises the defect to year precision.
        IgnixaEngine.RawValues(IgnixaEngine.Parse(patient), "@2012-06.highBoundary()")
            .ShouldBe(["2012-06-30T23:59:59.999-12:00"]);
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
