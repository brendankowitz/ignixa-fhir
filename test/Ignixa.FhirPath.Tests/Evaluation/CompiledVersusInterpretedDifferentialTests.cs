/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Differential harness holding the two FHIRPath evaluation paths to the same answer.
 *
 * TypedElementExtensions.Select() prefers a compiled delegate and only falls back to the
 * interpreter when FhirPathDelegateCompiler.TryCompile returns null, so the compiled path is the
 * one production search-parameter extraction observes. Nothing previously forced the two to agree
 * and they drifted: temporal literals kept their '@' sigil through the compiled ordinal string
 * compare, so "Patient.birthDate = @1974-12-25" answered false while the interpreter answered true.
 *
 * Every expression here is evaluated through both paths and the results must be indistinguishable.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class CompiledVersusInterpretedDifferentialTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathDelegateCompiler _compiler = new(new FhirPathEvaluator());

    public static TheoryData<string> Corpus => DifferentialFixture.Corpus;

    /// <summary>
    /// Expressions whose comparison operands are temporal and which must still take the compiled fast
    /// path. Search-parameter extraction leans on date comparisons, so a correctness fix that silently
    /// downgraded them to the interpreter would pass the differential test above while losing the
    /// reason the compiler exists. This list is the tripwire for that.
    /// </summary>
    public static TheoryData<string> MustCompile => new()
    {
        "birthDate = @1974-12-25",
        "meta.lastUpdated > @2024-01-01T00:00:00Z",
        "extension.value = @T10:30:00",
        "contact.period.start < contact.period.end",
        "telecom.where(system = 'phone')",
        "gender = 'male'",
    };

    /// <summary>
    /// Asserts <see cref="MustCompile"/> is a subset of <see cref="Corpus"/>.
    /// </summary>
    /// <remarks>
    /// The two lists are maintained by hand - <c>MustCompile</c> here, <c>Corpus</c> via
    /// <see cref="DifferentialFixture"/> - so a row could be edited out of one while surviving in the
    /// other, leaving this suite asserting compilation for an expression the differential theory above
    /// never runs. Mirrors <c>VersionedCompiledVersusInterpretedDifferentialTests</c>'s guard of the same
    /// name.
    /// </remarks>
    [Fact]
    public void GivenMustCompile_WhenComparedAgainstTheCorpus_ThenEveryRowIsAMemberOfIt()
    {
        // Arrange
        var corpus = new HashSet<string>(StringComparer.Ordinal);
        foreach (string expression in Corpus)
        {
            corpus.Add(expression);
        }

        var mustCompile = new List<string>();
        foreach (string expression in MustCompile)
        {
            mustCompile.Add(expression);
        }

        // Act & Assert
        mustCompile.ShouldAllBe(
            expression => corpus.Contains(expression),
            "every row in MustCompile has to also be a row in Corpus, or this suite is asserting "
            + "compilation for an expression the differential theory never runs.");
    }

    /// <summary>
    /// Asserts the <see cref="MustCompile"/> inventory is complete, so a row cannot be deleted from it
    /// without failing the build.
    /// </summary>
    /// <remarks>
    /// The subset check above cannot catch deletion: removing a row still leaves every remaining row a
    /// member of <c>Corpus</c>, so that check stays green with fewer rows compared. The expected list
    /// here is written independently of <see cref="MustCompile"/> for the same reason
    /// <c>FirelyVersusIgnixaDifferentialTests.NormalisedTypeNames</c>'s inventory test is: a list derived
    /// from the collection it guards agrees with any edit to that collection and asserts nothing.
    /// </remarks>
    [Fact]
    public void GivenTheMustCompileInventory_WhenEnumerated_ThenEveryPinnedExpressionIsPresent()
    {
        // Arrange
        string[] expected =
        [
            "birthDate = @1974-12-25",
            "meta.lastUpdated > @2024-01-01T00:00:00Z",
            "extension.value = @T10:30:00",
            "contact.period.start < contact.period.end",
            "telecom.where(system = 'phone')",
            "gender = 'male'",
        ];

        var actual = new List<string>();
        foreach (string expression in MustCompile)
        {
            actual.Add(expression);
        }

        // Assert
        actual.ToArray().ShouldBe(
            expected,
            "The MustCompile inventory changed. A row may only be retired after confirming it no longer "
            + "carries differential coverage; update this inventory in the same change.");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GivenAnExpression_WhenEvaluatedByBothPaths_ThenResultsAreIdentical(string expression)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();
        var ast = _parser.Parse(expression);
        var compiled = _compiler.TryCompile(ast);

        if (compiled is null)
        {
            // Declining to compile is the designed escape hatch: Select() falls back to the
            // interpreter, so the two paths agree by construction and there is nothing to compare.
            return;
        }

        // Act
        var compiledResult = DifferentialFixture.Describe(() => compiled(subject, DifferentialFixture.CreateContext(subject)));
        var interpretedResult = DifferentialFixture.Describe(() => _evaluator.Evaluate(subject, ast, DifferentialFixture.CreateContext(subject)));

        // Assert
        compiledResult.ShouldBe(
            interpretedResult,
            $"Compiled and interpreted evaluation of '{expression}' disagree.");
    }

    [Theory]
    [MemberData(nameof(MustCompile))]
    public void GivenAComparisonOnTheFastPath_WhenCompiled_ThenCompilationIsNotDeclined(string expression)
    {
        // Arrange
        var ast = _parser.Parse(expression);

        // Act
        var compiled = _compiler.TryCompile(ast);

        // Assert
        compiled.ShouldNotBeNull($"'{expression}' must keep using the compiled fast path.");
    }

    [Fact]
    public void GivenADatePathEqualToItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Regression: the compiled path compared "1974-12-25" against the unstripped "@1974-12-25"
        // ordinally and reported false while the interpreter reported true.

        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("birthDate = @1974-12-25").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenAnInstantGreaterThanItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("meta.lastUpdated > @2024-01-01T00:00:00Z").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenATimeValuedExtensionEqualToItsLiteral_WhenEvaluatedByBothPaths_ThenBothReportTrue()
    {
        // Regression: "extension.value = @T10:30:00" answered false on the compiled path. The literal
        // kept its '@' and the element's FhirTemporal was compared to it as an ordinal string.

        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("extension.value = @T10:30:00").Single();

        // Assert
        result.Value.ShouldBe(true);
    }

    [Fact]
    public void GivenOperandsOfDifferentPrecision_WhenOrderedOnTheCompiledPath_ThenResultIsEmpty()
    {
        // A year and a month overlap rather than order, so FHIRPath requires empty. The old compiled
        // comparer was typed Func<object?, object?, bool> and structurally could not express it.

        // Arrange
        var subject = DifferentialFixture.CreateSubject();

        // Act
        var result = subject.Select("@2012 > @2012-01").ToList();

        // Assert
        result.ShouldBeEmpty();
    }
}
