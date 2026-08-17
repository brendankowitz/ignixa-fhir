/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Edge case and error handling tests for FhirPath evaluator.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class EdgeCaseAndErrorTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region EvaluationContext Tests

    [Fact]
    public void GivenContext_WhenSetAndGetVariable_ThenReturnsVariable()
    {
        // Arrange
        var element = CreateIntegerElement(42);

        // Act - use immutable pattern
        var context = new EvaluationContext()
            .WithEnvironmentVariable("myVar", element);
        var result = context.GetEnvironmentVariable("myVar");

        // Assert
        Assert.Equal(element, result);
    }

    [Fact]
    public void GivenContext_WhenRemoveVariable_ThenVariableNoLongerExists()
    {
        // Arrange
        var element = CreateIntegerElement(42);
        var contextWithVar = new EvaluationContext()
            .WithEnvironmentVariable("myVar", element);

        // Act - use immutable pattern
        var contextWithoutVar = contextWithVar.WithoutEnvironmentVariable("myVar");
        var result = contextWithoutVar.GetEnvironmentVariable("myVar");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenContext_WhenGetNonExistentVariable_ThenReturnsNull()
    {
        // Arrange
        var context = new EvaluationContext();

        // Act
        var result = context.GetEnvironmentVariable("nonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GivenExternalVariable_WhenReferenced_ThenReturnsValue()
    {
        // Arrange - use immutable pattern
        var context = new EvaluationContext()
            .WithEnvironmentVariable("myValue", CreateIntegerElement(99));
        var expr = _parser.Parse("%myValue");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).SingleOrDefault();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void GivenNonExistentVariable_WhenReferenced_ThenSignalsError()
    {
        // Arrange - FHIRPath 1.9 makes reading an undefined environment variable an error, not an empty
        // collection; a silent empty is indistinguishable from a variable that is bound to nothing.
        var context = new EvaluationContext();
        var expr = _parser.Parse("%nonExistent");
        var root = CreateIntegerElement(0);

        // Act
        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr, context).ToList());

        // Assert
        Assert.Contains("nonExistent", exception.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Empty Collection Tests

    [Fact]
    public void GivenEmptyCollection_WhenFirst_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.first()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenLast_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.last()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenSingle_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.single()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenCount_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("{}.count()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    #endregion

    #region Null and Error Handling Tests

    [Fact]
    public void GivenInvalidTypeConversion_WhenToInteger_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'not-a-number'.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenInvalidTypeConversion_WhenToDecimal_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'not-a-number'.toDecimal()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenMathOperation_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} + 5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenMultipleItems_WhenMathOperation_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2) + 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenInvalidRegex_WhenMatches_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'test'.matches('[invalid(')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Invalid regex returns empty
    }

    [Fact]
    public void GivenInvalidRegex_WhenReplaceMatches_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'test'.replaceMatches('[invalid(', 'x')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Invalid regex returns empty
    }

    #endregion

    #region Comparison Edge Cases

    [Fact]
    public void GivenEmptyCollections_WhenEquality_ThenReturnsEmpty()
    {
        // Arrange - FHIRPath official tests: {} = {} returns empty (three-valued logic)
        var expr = _parser.Parse("{} = {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert - empty collection
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollections_WhenNotEquals_ThenReturnsEmpty()
    {
        // Arrange - FHIRPath spec: {} != {} returns empty (indeterminate)
        // This is different from {} = {} which returns true
        var expr = _parser.Parse("{} != {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert - empty collection
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyAndNonEmpty_WhenEquality_ThenReturnsEmpty()
    {
        // Arrange - FHIRPath spec: {} = X returns {}
        var expr = _parser.Parse("{} = 5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenNonEmptyAndEmpty_WhenEquality_ThenReturnsEmpty()
    {
        // Arrange - FHIRPath spec: X = {} returns {}
        var expr = _parser.Parse("5 = {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyAndNonEmpty_WhenNotEquals_ThenReturnsEmpty()
    {
        // Arrange - FHIRPath spec: {} != X returns {} (not true!)
        var expr = _parser.Parse("{} != 5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollections_WhenGreaterThan_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} > {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenMultipleItems_WhenComparison_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2) > 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Comparison on multiple items is undefined
    }

    #endregion

    #region String Edge Cases

    [Fact]
    public void GivenEmptyString_WhenLength_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("''.length()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void GivenEmptyString_WhenUpper_ThenReturnsEmptyString()
    {
        // Arrange
        var expr = _parser.Parse("''.upper()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void GivenOutOfBoundsSubstring_WhenSubstring_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'Hello'.substring(10)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Out of bounds returns empty
    }

    [Fact]
    public void GivenNegativeIndex_WhenSubstring_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'Hello'.substring(-1)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenNonString_WhenStringFunction_ThenThrowsError()
    {
        // Arrange
        var expr = _parser.Parse("42.upper()");
        var root = CreateIntegerElement(0);

        // Act & Assert - String functions on non-strings throw (matching Firely/fhirpath.js behavior)
        var ex = Assert.Throws<FhirPathEvaluationException>(() => 
            _evaluator.Evaluate(root, expr).ToList());
        
        Assert.Contains("upper", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Skip/Take Edge Cases

    [Fact]
    public void GivenNonIntegerSkip_WhenSkip_ThenReturnsEmpty()
    {
        // Arrange
        // Skip with empty argument returns empty
        var expr = _parser.Parse("(1 | 2 | 3).skip({})");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenZeroSkip_WhenSkip_ThenReturnsAll()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).skip(0)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GivenNegativeTake_WhenTake_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).take(-1)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenZeroTake_WhenTake_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).take(0)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Indexer Edge Cases

    [Fact]
    public void GivenNegativeIndex_WhenIndexer_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3)[-1]");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenOutOfBoundsIndex_WhenIndexer_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3)[10]");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Unary Operator Edge Cases

    [Fact]
    public void GivenPositiveInteger_WhenUnaryMinus_ThenReturnsNegative()
    {
        // Arrange
        var expr = _parser.Parse("-5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-5, result.Value); // Returns integer (preserves type)
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenLargeLong_WhenUnaryMinus_ThenReturnsNegatedDecimal()
    {
        // Arrange
        var expr = _parser.Parse("-2147483648L");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-2147483648m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenQuantity_WhenUnaryMinus_ThenQuantityHasChildren()
    {
        // Arrange
        var expr = _parser.Parse("-(5 'mg')");
        var valueExpr = _parser.Parse("(-(5 'mg')).value");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();
        var valueResult = _evaluator.Evaluate(root, valueExpr).ToList();
        var valueChildren = result.Children("value").ToList();
        var unitChildren = result.Children("unit").ToList();

        // Assert
        Assert.Equal("Quantity", result.InstanceType);
        Assert.False(result.HasPrimitiveValue);
        Assert.Single(valueChildren);
        Assert.Equal(-5m, valueChildren[0].Value);
        Assert.Single(unitChildren);
        Assert.Equal("mg", unitChildren[0].Value);
        Assert.Single(valueResult);
        Assert.Equal(-5m, valueResult[0].Value);
    }

    [Fact]
    public void GivenPositiveInteger_WhenUnaryPlus_ThenReturnsValue()
    {
        // Arrange
        var expr = _parser.Parse("+5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void GivenBooleanOperand_WhenUnaryMinus_ThenSignalsError()
    {
        // Official tests testLiteralIntegerNegative1Invalid and testPrecedence1, both '-1.convertsToInteger()':
        // unary minus binds looser than the invocation, so this parses as -(1.convertsToInteger()) -> -(true),
        // which the Unary Operators clause makes an error. The load-bearing part of #251 still holds - the
        // result must never be -1, which is what Convert.ToDecimal(true) used to produce.
        var expr = _parser.Parse("-1.convertsToInteger()");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());
        Assert.Contains("boolean", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GivenBooleanOperandFromConvertsToDecimal_WhenUnaryMinus_ThenSignalsError()
    {
        // Official test testLiteralDecimalNegative01Invalid: '-0.1.convertsToDecimal()' parses as
        // -(0.1.convertsToDecimal()) -> -(true), which must error rather than yield -0.1 or empty.
        var expr = _parser.Parse("-0.1.convertsToDecimal()");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());
        Assert.Contains("boolean", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GivenStringOperand_WhenUnaryMinus_ThenSignalsError()
    {
        // Unary minus is defined only for Integer, Decimal and Quantity; a string operand is an error.
        var expr = _parser.Parse("-'5'");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr).ToList());
        Assert.Contains("string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GivenDecimalLiteral_WhenUnaryMinus_ThenReturnsNegative()
    {
        // Sanity check: unary minus on a decimal still works.
        var expr = _parser.Parse("-5.5");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.Equal(-5.5m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenIntMinValue_WhenUnaryMinus_ThenReturnsEmpty()
    {
        // -int.MinValue = 2147483648 overflows int32; per FHIRPath spec overflow → empty.
        var root = CreateIntegerElement(int.MinValue);
        var result = _evaluator.Evaluate(root, _parser.Parse("-$this")).ToList();

        Assert.Empty(result);
    }

    #endregion

    #region Where/All/Any Edge Cases

    [Fact]
    public void GivenEmptyCollection_WhenWhere_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.where($this > 5)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenAll_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("{}.all($this > 5)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!); // Empty collection: all returns true
    }

    [Fact]
    public void GivenPredicateReturnsEmpty_WhenAll_ThenReturnsFalse()
    {
        // Per FHIRPath spec: all() returns true only if criteria evaluates to true for every element.
        // If criteria returns empty (uncertain) for any element, all() returns false (not empty).
        // This tests the Period invariant scenario where comparing dates of different precision returns empty.
        var expr = _parser.Parse("(1 | 2).all($this > {})");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenEmptyCollection_WhenAny_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("{}.any($this > 5)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!); // Empty collection: any returns {false}
    }

    #endregion

    #region Type Operator Edge Cases

    [Fact]
    public void GivenEmptyCollection_WhenTypeIs_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} is `integer`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyCollection_WhenTypeAs_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} as `integer`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenWrongType_WhenTypeAs_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'hello' as `integer`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("(1 | 2 | 3) as `integer`")]
    [InlineData("(1 | 2 | 3).as(`integer`)")]
    public void GivenMultipleItemsUnderR5_WhenTypeAs_ThenThrows(string expression)
    {
        // FHIRPath 1.6.3 for the 'as' operator: "If there is more than one item in the input
        // collection, the evaluator will throw an error". as() inherits it, being defined as
        // backwards compatibility "just as with the 'as' keyword". Asserted for both forms because
        // they are separate code paths - the operator in FhirPathEvaluator.EvaluateTypeAs, the
        // function in CollectionFunctions.As - and the rule is independently droppable from either.

        // Arrange
        var expr = _parser.Parse(expression);
        var root = CreateIntegerElement(0);
        var context = ContextFor(FhirVersion.R5);

        // Act & Assert
        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(root, expr, context).ToList());
        Assert.Contains("single item", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("(1 | 2 | 3) as `integer`")]
    [InlineData("(1 | 2 | 3).as(`integer`)")]
    public void GivenMultipleItemsUnderR4_WhenTypeAs_ThenBothFormsFilterElementWise(string expression)
    {
        // The rule is gated on the schema version because HL7's own R4/R4B SearchParameters break it.
        // See TypeMatcher.EnsureSingletonInput. Asserting the R4 side explicitly means the gate cannot
        // be dropped without a test going red - the R5 theory above passes either way.
        //
        // Both spellings are asserted against the SAME expected count on purpose: the operator used to
        // return empty for a non-singleton input while as() filtered element-wise, and that disagreement
        // was the reason Observation.component.value as Quantity indexed nothing for a blood pressure.
        // Pinning one count for both forms is what stops the two drifting apart again.

        // Arrange
        var expr = _parser.Parse(expression);
        var root = CreateIntegerElement(0);
        var context = ContextFor(FhirVersion.R4);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData("(1 | 2 | 3) as `integer`")]
    [InlineData("(1 | 2 | 3).as(`integer`)")]
    public void GivenMultipleItemsAndNoSchema_WhenTypeAs_ThenBothFormsFilterElementWise(string expression)
    {
        // No schema means no version, and the two ways to be wrong are not symmetric: enforcing when
        // we should not silently drops search index entries, while not enforcing returns a collection
        // where the spec wanted an error - which is what Firely does on every version anyway.

        // Arrange
        var expr = _parser.Parse(expression);
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    [Theory]
    [InlineData("(1 | 'two' | 3) as `integer`", 2)]
    [InlineData("(1 | 'two' | 3).as(`integer`)", 2)]
    public void GivenAMixedTypeCollectionUnderR4_WhenTypeAs_ThenOnlyMatchingItemsSurvive(string expression, int expectedCount)
    {
        // Element-wise means filtering, not passing the whole collection through. A homogeneous
        // collection cannot tell those two apart - this one can.

        // Arrange
        var expr = _parser.Parse(expression);
        var root = CreateIntegerElement(0);
        var context = ContextFor(FhirVersion.R4);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.Equal(expectedCount, result.Count);
    }

    [Theory]
    [InlineData("(1 | 2 | 3).ofType(`integer`)")]
    [InlineData("(1 | 2 | 3) is `integer`")]
    public void GivenMultipleItemsUnderR5_WhenOfType_ThenDoesNotThrow(string expression)
    {
        // The singleton rule is specific to the cast operators. ofType() is specified as a filter
        // over a collection, so a multi-item input is its normal case - the two share
        // TypeMatcher.FilterByType, which is exactly why the guard must not live inside it.
        // 'is' is included because it sits next to 'as' in the same spec section and returns empty
        // rather than throwing for a non-singleton, which is easy to conflate. Run under R5, the
        // version that does enforce the rule for 'as', so the exemption is what is being asserted.

        // Arrange
        var expr = _parser.Parse(expression);
        var root = CreateIntegerElement(0);
        var context = ContextFor(FhirVersion.R5);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(result);
    }

    private static EvaluationContext ContextFor(FhirVersion version) => new()
    {
        Schema = version.GetSchemaProvider(),
    };

    #endregion

    #region Math Function Overflow Edge Cases

    [Fact]
    public void GivenDecimalLargerThanIntMax_WhenFloor_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("2147483648.5.floor()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalSmallerThanIntMin_WhenFloor_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(-2147483649.5).floor()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalLargerThanIntMax_WhenCeiling_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("2147483647.5.ceiling()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalSmallerThanIntMin_WhenCeiling_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(-2147483649.5).ceiling()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalLargerThanIntMax_WhenTruncate_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("2147483648.9.truncate()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalSmallerThanIntMin_WhenTruncate_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("(-2147483649.9).truncate()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Overflow returns empty per FHIRPath spec
    }

    [Fact]
    public void GivenDecimalAtIntMax_WhenFloor_ThenReturnsValue()
    {
        // Arrange - int.MaxValue = 2147483647
        var expr = _parser.Parse("2147483647.9.floor()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2147483647, result.Value);
    }

    [Fact]
    public void GivenDecimalAtIntMin_WhenCeiling_ThenReturnsValue()
    {
        // Arrange - int.MinValue = -2147483648
        var expr = _parser.Parse("(-2147483648.9).ceiling()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-2147483648, result.Value);
    }

    #endregion

    #region Decimal Conversion Edge Cases

    [Fact]
    public void GivenDecimalOutOfIntRange_WhenToInteger_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("9999999999999.5.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // Out of int range returns empty
    }

    [Fact]
    public void GivenBooleanTrue_WhenToInteger_ThenReturnsOne()
    {
        // Arrange
        var expr = _parser.Parse("true.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenBooleanFalse_WhenToInteger_ThenReturnsZero()
    {
        // Arrange
        var expr = _parser.Parse("false.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0, result.Value);
    }

    [Fact]
    public void GivenIntegerOne_WhenToBoolean_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("1.toBoolean()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenIntegerZero_WhenToBoolean_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("0.toBoolean()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type)
        {
            Value = value;
            InstanceType = type;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
