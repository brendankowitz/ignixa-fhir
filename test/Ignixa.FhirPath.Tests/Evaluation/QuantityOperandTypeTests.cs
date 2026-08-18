/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The Quantity branch's operand-type rule, and the unit rule it must not swallow.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the distinction between "defined but with no answer" and "not defined at all" once one operand
/// is a Quantity.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator diverts the whole expression to <c>QuantityEvaluator</c> as soon as either operand is a
/// Quantity, and that evaluator used to <c>return []</c> for every shape it did not implement. So
/// <c>1 'mg' + 5</c> answered empty while the structurally identical <c>1 + 'mg'</c> four lines below it
/// threw, and <c>1 'mg' &gt; 'x'</c> answered empty while <c>1.5 &lt; 'x'</c> threw the error this engine
/// deliberately added for official <c>testLiteralDecimalLessThanInvalid</c>.
/// </para>
/// <para>
/// Empty is not benign in that position: <c>FhirPathInvariantCheck.IsResultTrue</c> maps empty to
/// <see langword="false"/>, so a quantity-valued invariant compared against a mistyped operand rejected
/// the resource instead of reporting that the constraint could not be evaluated - the exact defect class
/// <c>FhirPathEvaluationException</c> exists to end.
/// </para>
/// <para>
/// The unit rule is the other half and must survive: incompatible <i>units</i> are spec-correct empty,
/// because the conversion simply has no answer. Every error case below is paired with the unit case it
/// would be easiest to break while fixing it.
/// </para>
/// </remarks>
public class QuantityOperandTypeTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    /// <summary>
    /// <c>+</c> and <c>-</c> on a Quantity are defined only for a Quantity operand, so anything else is
    /// one of §Math's "incompatible items".
    /// </summary>
    [Theory]
    [InlineData("1 'mg' + 5")]
    [InlineData("5 + 1 'mg'")]
    [InlineData("1 'mg' - 5")]
    [InlineData("5 - 1 'mg'")]
    [InlineData("1 'mg' + 'x'")]
    [InlineData("1 'mg' - 'x'")]
    [InlineData("1 'mg' + true")]
    [InlineData("1 'mg' * 'x'")]
    [InlineData("1 'mg' / 'x'")]
    [InlineData("2 / 1 'mg'")]
    public void GivenAQuantityAndAnOperandTheOperatorIsNotDefinedFor_WhenApplyingArithmetic_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// Ordering has no implicit conversion to fall back on for a String, Boolean or temporal operand, so
    /// it is the same type error the non-Quantity path already signals.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' > 'x'")]
    [InlineData("1 'mg' < 'x'")]
    [InlineData("1 'mg' >= true")]
    [InlineData("1 'mg' <= true")]
    [InlineData("1 'mg' > @2012-01-01")]
    [InlineData("@2012-01-01 < 1 'mg'")]
    public void GivenAQuantityAndAnUnconvertibleOperand_WhenOrdering_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// Equality between values of different types is decidably false, not undecidable, so it must not be
    /// turned into an error alongside ordering.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' = 'x'", false)]
    [InlineData("1 'mg' != 'x'", true)]
    [InlineData("1 'mg' = true", false)]
    [InlineData("1 'mg' != true", true)]
    public void GivenAQuantityAndAnUnconvertibleOperand_WhenComparedForEquality_ThenTheyAreNotEqual(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// Incompatible units are the spec's own empty case and must not become an error.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' + 1 'm'")]
    [InlineData("1 'mg' - 1 'm'")]
    [InlineData("1 'mg' < 1 'm'")]
    [InlineData("1 'mg' > 1 'm'")]
    [InlineData("1 'mg' = 1 'm'")]
    public void GivenQuantitiesWithIncompatibleUnits_WhenCompared_ThenItYieldsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Integer and Decimal are <i>implicitly</i> convertible to Quantity, in the unity unit. That is what
    /// keeps <c>1 'mg' &gt; 5</c> empty - it is <c>1 'mg'</c> against <c>5 '1'</c>, an incompatible-units
    /// case - rather than the error the unconvertible operands above get. Firely agrees on both rows.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' > 5")]
    [InlineData("5 < 1 'mg'")]
    [InlineData("1 'mg' = 5")]
    [InlineData("1 'mg' >= 5")]
    public void GivenAQuantityComparedToABareNumber_WhenTheUnitsDiffer_ThenItYieldsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    /// <summary>
    /// The other side of the same implicit conversion: a dimensionless Quantity does compare with a bare
    /// number, which is the observable proof that the empty above comes from the unit rule and not from
    /// "the operand is not a Quantity".
    /// </summary>
    [Theory]
    [InlineData("5 '1' = 5", true)]
    [InlineData("5 '1' > 1", true)]
    [InlineData("5 '1' < 1", false)]
    [InlineData("1 = 1 '1'", true)]
    public void GivenADimensionlessQuantityAndABareNumber_WhenCompared_ThenTheyCompareAsNumbers(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// The shapes the Quantity evaluator does implement must keep working; the error path above is a
    /// rule about undefined operands, not a narrowing of Quantity arithmetic.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' + 1 'g'", "1001 'mg'")]
    [InlineData("1 'mg' - 1 'g'", "-999 'mg'")]
    [InlineData("1 'mg' * 2", "2 'mg'")]
    [InlineData("2 * 1 'mg'", "2 'mg'")]
    [InlineData("1 'mg' / 2", "0.5 'mg'")]
    public void GivenQuantityArithmeticTheEngineImplements_WhenEvaluated_ThenItProducesAQuantity(string expression, string expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        DifferentialFixture.Render(result[0].Value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("1 'mg' < 1 'g'", true)]
    [InlineData("1 'mg' = 1000 'ug'", true)]
    [InlineData("1 'mg' > 1 'g'", false)]
    public void GivenQuantitiesWithCompatibleUnits_WhenCompared_ThenTheyAreConverted(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// Empty propagation still wins over the type rule, on both operators.
    /// </summary>
    [Theory]
    [InlineData("1 'mg' + {}")]
    [InlineData("{} + 1 'mg'")]
    [InlineData("1 'mg' > {}")]
    [InlineData("{} > 1 'mg'")]
    public void GivenAnEmptyOperand_WhenCombinedWithAQuantity_ThenItYieldsEmpty(string expression)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.ShouldBeEmpty();
    }

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new ScalarRoot(), parsed).ToList();
    }

    private sealed class ScalarRoot : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value => 0;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
