/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Differential harness holding the optimizing parser to the unoptimized one.
 *
 * CompilationOptions.Optimize swaps OptimizingAstBuilder in for AstBuilder, so an optimized parse and
 * an unoptimized parse of the same text produce different ASTs that must still answer identically.
 * Nothing previously forced that and they drifted: TryShortCircuit folded "X and false" to the
 * constant false by discarding X entirely, so "(1 | 2).single() and false" answered false where the
 * unoptimized parse threw FhirPathEvaluationException.
 *
 * Every expression here is parsed both ways, evaluated by the same interpreter, and the results must
 * be indistinguishable - a thrown error included.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.FhirPath.Parsing;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class OptimizedVersusUnoptimizedDifferentialTests
{
    private readonly FhirPathParser _plainParser = new(CompilationOptions.Default);
    private readonly FhirPathParser _optimizingParser = new(CompilationOptions.Optimized);
    private readonly FhirPathEvaluator _evaluator = new();

    public static TheoryData<string> Corpus => DifferentialFixture.Corpus;

    public static TheoryData<string> FoldableCorpus => DifferentialFixture.FoldableCorpus;

    [Theory]
    [MemberData(nameof(Corpus))]
    public void GivenAnExpression_WhenParsedBothWays_ThenResultsAreIdentical(string expression)
    {
        AssertBothParsesAgree(expression);
    }

    [Theory]
    [MemberData(nameof(FoldableCorpus))]
    public void GivenAFoldableExpression_WhenParsedBothWays_ThenResultsAreIdentical(string expression)
    {
        AssertBothParsesAgree(expression);
    }

    [Fact]
    public void GivenAnErrorSignallingOperandAndedWithFalse_WhenOptimized_ThenTheErrorStillSurfaces()
    {
        // Regression: TryShortCircuit rewrote "X and false" to the constant false whatever X was, so
        // the operand that should have thrown was never evaluated.

        // Arrange
        var subject = DifferentialFixture.CreateSubject();
        var ast = _optimizingParser.Parse("(1 | 2).single().exists() and false");

        // Act
        var evaluate = () => _evaluator.Evaluate(subject, ast, DifferentialFixture.CreateContext(subject)).ToList();

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    [Fact]
    public void GivenAnErrorSignallingOperandOredWithTrue_WhenOptimized_ThenTheErrorStillSurfaces()
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();
        var ast = _optimizingParser.Parse("(1 | 2).single().exists() or true");

        // Act
        var evaluate = () => _evaluator.Evaluate(subject, ast, DifferentialFixture.CreateContext(subject)).ToList();

        // Assert
        Should.Throw<FhirPathEvaluationException>(evaluate);
    }

    [Fact]
    public void GivenAConstantThatDecidesFromTheLeft_WhenOptimized_ThenTheRightOperandIsStillDiscarded()
    {
        // "false and X" is one of the three rows the interpreter itself short-circuits, so folding it
        // away is the optimizer agreeing with the interpreter rather than diverging from it. Losing
        // this fold would be a silent regression in what Optimize is for.

        // Arrange & Act
        var optimized = _optimizingParser.Parse("false and (1 | 2).single().exists()");

        // Assert
        optimized.ShouldBeOfType<Ignixa.FhirPath.Expressions.ConstantExpression>()
            .Value.ShouldBe(false);
    }

    [Fact]
    public void GivenAConstantAndAnErrorSignallingOperand_WhenOptimized_ThenTheOperandIsNotFoldedAway()
    {
        // Arrange & Act
        var optimized = _optimizingParser.Parse("(1 | 2).single().exists() and false");

        // Assert
        optimized.ShouldBeOfType<Ignixa.FhirPath.Expressions.BinaryExpression>();
    }

    /// <summary>
    /// Integer and Decimal are one number in FHIRPath, and the folder decided by CLR type instead.
    /// </summary>
    /// <remarks>
    /// <c>object.Equals</c> on the boxed literals made <c>1 = 1.0</c> fold to <see langword="false"/>
    /// while the evaluator - which widens both operands to <see cref="decimal"/> - answered
    /// <see langword="true"/>. The same expression, over the same data, gave a different answer per
    /// <see cref="CompilationOptions"/>. Asserted on the folded constant rather than only through the
    /// differential so that a regression names the folder rather than the corpus.
    /// </remarks>
    [Theory]
    [InlineData("1 = 1.0", true)]
    [InlineData("1 != 1.0", false)]
    [InlineData("2 = 2.00", true)]
    [InlineData("1L = 1", true)]
    [InlineData("1L != 1.0", false)]
    [InlineData("1 = 2", false)]
    [InlineData("'a' = 'a'", true)]
    [InlineData("true != false", true)]
    public void GivenNumericLiteralsOfDifferentTypes_WhenOptimized_ThenEqualityFoldsByValue(string expression, bool expected)
    {
        // Arrange & Act
        var optimized = _optimizingParser.Parse(expression);

        // Assert
        optimized.ShouldBeOfType<Ignixa.FhirPath.Expressions.ConstantExpression>()
            .Value.ShouldBe(expected);
    }

    /// <summary>
    /// The folder's rule is a refusal, not a second copy of the evaluator's: an operand pair it cannot
    /// answer identically is left as written and decided at runtime.
    /// </summary>
    [Theory]
    [InlineData("'a' = true")]
    [InlineData("1 = 'a'")]
    [InlineData("1 = true")]
    [InlineData("@2012 = @2012-01")]
    [InlineData("1 'm' = 100 'cm'")]
    public void GivenOperandsTheFolderCannotDecide_WhenOptimized_ThenItDeclinesToFold(string expression)
    {
        // Arrange & Act
        var optimized = _optimizingParser.Parse(expression);

        // Assert
        optimized.ShouldNotBeOfType<Ignixa.FhirPath.Expressions.ConstantExpression>();
    }

    [Fact]
    public void GivenTwoConstants_WhenOptimized_ThenTheyAreStillFolded()
    {
        // Arrange & Act
        var optimized = _optimizingParser.Parse("true and false");

        // Assert
        optimized.ShouldBeOfType<Ignixa.FhirPath.Expressions.ConstantExpression>()
            .Value.ShouldBe(false);
    }

    private void AssertBothParsesAgree(string expression)
    {
        // Arrange
        var subject = DifferentialFixture.CreateSubject();
        var plainAst = _plainParser.Parse(expression);
        var optimizedAst = _optimizingParser.Parse(expression);

        // Act
        var plainResult = DifferentialFixture.Describe(
            () => _evaluator.Evaluate(subject, plainAst, DifferentialFixture.CreateContext(subject)));
        var optimizedResult = DifferentialFixture.Describe(
            () => _evaluator.Evaluate(subject, optimizedAst, DifferentialFixture.CreateContext(subject)));

        // Assert
        optimizedResult.ShouldBe(
            plainResult,
            $"Optimized and unoptimized parses of '{expression}' disagree.");
    }
}
