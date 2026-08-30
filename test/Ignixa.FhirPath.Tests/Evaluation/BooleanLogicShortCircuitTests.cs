/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * The three-valued truth tables for and/or/xor/implies, and which cells may skip the right operand.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers FHIRPath's Boolean logic in two halves: every cell of the four truth tables still produces the
/// value the spec prints, and the three cells whose row is constant skip the right operand entirely.
/// </summary>
/// <remarks>
/// The two halves have to be tested together. Short-circuiting is only sound where the left operand
/// decides the whole row - <c>false and *</c>, <c>true or *</c>, <c>false implies *</c> - and any attempt
/// to extend it (to an empty left operand, say) shows up here as a changed table cell rather than as a
/// subtly different answer somewhere in a shipped invariant.
/// </remarks>
public class BooleanLogicShortCircuitTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Theory]
    // and
    [InlineData("true and true", true)]
    [InlineData("true and false", false)]
    [InlineData("true and {}", null)]
    [InlineData("false and true", false)]
    [InlineData("false and false", false)]
    [InlineData("false and {}", false)]
    [InlineData("{} and true", null)]
    [InlineData("{} and false", false)]
    [InlineData("{} and {}", null)]
    // or
    [InlineData("true or true", true)]
    [InlineData("true or false", true)]
    [InlineData("true or {}", true)]
    [InlineData("false or true", true)]
    [InlineData("false or false", false)]
    [InlineData("false or {}", null)]
    [InlineData("{} or true", true)]
    [InlineData("{} or false", null)]
    [InlineData("{} or {}", null)]
    // xor
    [InlineData("true xor true", false)]
    [InlineData("true xor false", true)]
    [InlineData("true xor {}", null)]
    [InlineData("false xor true", true)]
    [InlineData("false xor false", false)]
    [InlineData("false xor {}", null)]
    [InlineData("{} xor true", null)]
    [InlineData("{} xor false", null)]
    [InlineData("{} xor {}", null)]
    // implies
    [InlineData("true implies true", true)]
    [InlineData("true implies false", false)]
    [InlineData("true implies {}", null)]
    [InlineData("false implies true", true)]
    [InlineData("false implies false", true)]
    [InlineData("false implies {}", true)]
    [InlineData("{} implies true", true)]
    [InlineData("{} implies false", null)]
    [InlineData("{} implies {}", null)]
    public void GivenABooleanLogicExpression_WhenEvaluating_ThenItMatchesTheSpecTruthTable(string expression, bool? expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        if (expected is null)
        {
            result.ShouldBeEmpty();
            return;
        }

        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// The right operand is an expression the engine refuses to evaluate, so reaching it is observable:
    /// a value means it was skipped, a throw means it was not.
    /// </summary>
    [Theory]
    [InlineData("false and (1 | 2).single()", false)]
    [InlineData("true or (1 | 2).single()", true)]
    [InlineData("false implies (1 | 2).single()", true)]
    public void GivenTheLeftOperandDecidesTheResult_WhenEvaluating_ThenTheRightOperandIsNotEvaluated(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// The complement: every case where the left operand leaves the answer open - including an empty
    /// left operand, which decides nothing in three-valued logic - must still evaluate the right one.
    /// </summary>
    [Theory]
    [InlineData("true and (1 | 2).single()")]
    [InlineData("false or (1 | 2).single()")]
    [InlineData("true implies (1 | 2).single()")]
    [InlineData("{} and (1 | 2).single()")]
    [InlineData("{} or (1 | 2).single()")]
    [InlineData("{} implies (1 | 2).single()")]
    [InlineData("true xor (1 | 2).single()")]
    public void GivenTheLeftOperandLeavesTheResultOpen_WhenEvaluating_ThenTheRightOperandIsStillEvaluated(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// The two halves meeting: an operand that is actually consumed must be a singleton, but a
    /// short-circuited right operand is never consumed and so is never checked.
    /// </summary>
    /// <remarks>
    /// This is the pair of tests that pins the placement of the singleton rule. Hoisting the check to the
    /// top of the operator - the obvious way to implement "operands are first evaluated as Booleans" -
    /// would keep the second theory passing and silently break the first, turning R4's <c>tim-9</c> guard
    /// pattern into an error. Neither theory alone would catch that.
    /// </remarks>
    [Theory]
    [InlineData("false and (1 | 2)", false)]
    [InlineData("true or (1 | 2)", true)]
    [InlineData("false implies (1 | 2)", true)]
    public void GivenTheLeftOperandDecidesTheResult_WhenTheRightOperandIsMultiItem_ThenNoErrorIsSignalled(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    /// <summary>
    /// Every position where a multi-item operand is actually consumed, on both sides of all four
    /// operators. A left operand is always consumed, so it errors even in a row that would short-circuit.
    /// </summary>
    [Theory]
    [InlineData("true and (1 | 2)")]
    [InlineData("(1 | 2) and true")]
    [InlineData("(1 | 2) and false")]
    [InlineData("false or (1 | 2)")]
    [InlineData("(1 | 2) or false")]
    [InlineData("(1 | 2) or true")]
    [InlineData("true xor (1 | 2)")]
    [InlineData("(1 | 2) xor true")]
    [InlineData("true implies (1 | 2)")]
    [InlineData("(1 | 2) implies true")]
    [InlineData("(1 | 2) implies false")]
    [InlineData("{} and (1 | 2)")]
    [InlineData("{} or (1 | 2)")]
    [InlineData("{} implies (1 | 2)")]
    [InlineData("{} xor (1 | 2)")]
    public void GivenAMultiItemOperandIsConsumed_WhenEvaluating_ThenAnErrorIsSignalled(string expression)
    {
        // Act & Assert
        Should.Throw<FhirPathEvaluationException>(() => Evaluate(expression));
    }

    /// <summary>
    /// A single non-boolean item still evaluates to true - that is the "expected input type is Boolean"
    /// branch of Singleton Evaluation, not a case the multi-item rule swallows.
    /// </summary>
    [Theory]
    [InlineData("(1).toInteger() and true", true)]
    [InlineData("'x' and true", true)]
    [InlineData("'x' or false", true)]
    public void GivenASingleNonBooleanOperand_WhenEvaluating_ThenItIsTruthy(string expression, bool expected)
    {
        // Act
        var result = Evaluate(expression);

        // Assert
        result.Count.ShouldBe(1);
        result[0].Value.ShouldBe(expected);
    }

    private List<IElement> Evaluate(string expression)
    {
        var parsed = _parser.Parse(expression);
        return _evaluator.Evaluate(new BooleanLogicRoot(), parsed).ToList();
    }

    private sealed class BooleanLogicRoot : IElement
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
