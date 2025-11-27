/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Edge case and error handling tests for FhirPath evaluator.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

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
        var context = new EvaluationContext();
        var element = CreateIntegerElement(42);

        // Act
        context.SetEnvironmentVariable("myVar", element);
        var result = context.GetEnvironmentVariable("myVar");

        // Assert
        Assert.Equal(element, result);
    }

    [Fact]
    public void GivenContext_WhenRemoveVariable_ThenVariableNoLongerExists()
    {
        // Arrange
        var context = new EvaluationContext();
        var element = CreateIntegerElement(42);
        context.SetEnvironmentVariable("myVar", element);

        // Act
        context.RemoveEnvironmentVariable("myVar");
        var result = context.GetEnvironmentVariable("myVar");

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
        // Arrange
        var context = new EvaluationContext();
        context.SetEnvironmentVariable("myValue", CreateIntegerElement(99));
        var expr = _parser.Parse("%myValue");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).SingleOrDefault();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void GivenNonExistentVariable_WhenReferenced_ThenReturnsEmpty()
    {
        // Arrange
        var context = new EvaluationContext();
        var expr = _parser.Parse("%nonExistent");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.Empty(result);
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
        // Arrange
        var expr = _parser.Parse("{} = {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenEmptyAndNonEmpty_WhenEquality_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} = 5");
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
    public void GivenNonString_WhenStringFunction_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("42.upper()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result); // String functions on non-strings return empty
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

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
