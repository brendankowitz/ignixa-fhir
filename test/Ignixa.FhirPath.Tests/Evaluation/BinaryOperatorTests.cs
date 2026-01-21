/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath binary operators.
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class BinaryOperatorTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Union Operator (|) Tests

    [Fact]
    public void GivenTwoCollections_WhenUsingUnionOperator_ThenEliminatesDuplicates()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3) | (2 | 3 | 4)");
        var root = CreateIntegerElement(0); // Dummy root

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(4, result.Count); // Should have 1, 2, 3, 4 (no duplicates)
    }

    [Fact]
    public void GivenEmptyCollections_WhenUsingUnionOperator_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{} | {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Math Operator Tests

    [Fact]
    public void GivenTwoIntegers_WhenAddition_ThenReturnsInteger()
    {
        // Arrange
        var expr = _parser.Parse("5 + 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(8, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenIntegerAndDecimal_WhenAddition_ThenReturnsDecimal()
    {
        // Arrange
        var expr = _parser.Parse("5 + 3.5");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(8.5m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenTwoIntegers_WhenSubtraction_ThenReturnsCorrectValue()
    {
        // Arrange
        var expr = _parser.Parse("10 - 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(7, result.Value);
    }

    [Fact]
    public void GivenTwoIntegers_WhenMultiplication_ThenReturnsCorrectValue()
    {
        // Arrange
        var expr = _parser.Parse("4 * 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(12, result.Value);
    }

    [Fact]
    public void GivenTwoIntegers_WhenDivision_ThenReturnsDecimal()
    {
        // Arrange
        var expr = _parser.Parse("10 / 4");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(2.5m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenDivisionByZero_WhenDivision_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("10 / 0");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenTwoIntegers_WhenIntegerDivision_ThenReturnsTruncatedInteger()
    {
        // Arrange
        var expr = _parser.Parse("10 div 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(3, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenTwoIntegers_WhenModulo_ThenReturnsRemainder()
    {
        // Arrange
        var expr = _parser.Parse("10 mod 3");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1m, result.Value);
    }

    #endregion

    #region String Concatenation Tests

    [Fact]
    public void GivenTwoStrings_WhenStringConcatenation_ThenConcatenates()
    {
        // Arrange
        var expr = _parser.Parse("'Hello' & ' ' & 'World'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Hello World", result.Value);
        Assert.Equal("string", result.InstanceType);
    }

    #endregion

    #region Equivalence Operator Tests

    [Fact]
    public void GivenEmptyCollections_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("{} ~ {}");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).SingleOrDefault();

        // Assert
        Assert.NotNull(result);
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenCaseInsensitiveStrings_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'HELLO' ~ 'hello'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenWhitespaceNormalizedStrings_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'Hello  World' ~ 'Hello World'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenDifferentStrings_WhenNotEquivalent_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'HELLO' !~ 'goodbye'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Quantity Equivalence Operator Tests

    [Fact]
    public void GivenSameQuantityDifferentPrecision_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange - 5.0 'mg' ~ 5.00 'mg' (same value, different precision notation)
        var expr = _parser.Parse("5.0 'mg' ~ 5.00 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenDifferentQuantitiesAtOnePrecision_WhenEquivalent_ThenReturnsFalse()
    {
        // Arrange - 5.0 'mg' ~ 5.1 'mg' (different values at 1 decimal precision)
        var expr = _parser.Parse("5.0 'mg' ~ 5.1 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenDifferentQuantitiesAtTwoPrecision_WhenEquivalent_ThenReturnsFalse()
    {
        // Arrange - 5.00 'mg' ~ 5.01 'mg' (different at 2 decimal precision)
        var expr = _parser.Parse("5.00 'mg' ~ 5.01 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenQuantitiesRoundToSame_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange - 5 'mg' ~ 5.4 'mg' (5.4 rounds to 5 at 0 precision)
        var expr = _parser.Parse("5 'mg' ~ 5.4 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenQuantitiesRoundToDifferent_WhenEquivalent_ThenReturnsFalse()
    {
        // Arrange - 5 'mg' ~ 5.5 'mg' (5.5 rounds to 6 at 0 precision with AwayFromZero)
        var expr = _parser.Parse("5 'mg' ~ 5.5 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenQuantitiesExactMatch_WhenEquivalent_ThenReturnsTrue()
    {
        // Arrange - 4 'g' ~ 4000 'mg' (exact match after conversion)
        var expr = _parser.Parse("4 'g' ~ 4000 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenQuantitiesNotEquivalent_WhenNotEquivalentOperator_ThenReturnsTrue()
    {
        // Arrange - 5.0 'mg' !~ 5.1 'mg' (different values)
        var expr = _parser.Parse("5.0 'mg' !~ 5.1 'mg'");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Type Operator Tests

    [Fact]
    public void GivenIntegerValue_WhenIsInteger_ThenReturnsTrue()
    {
        // Arrange
        // Note: Using delimited identifiers due to 'integer' containing 'in' keyword
        var expr = _parser.Parse("42 is `integer`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenIntegerValue_WhenIsString_ThenReturnsFalse()
    {
        // Arrange
        // Note: Using delimited identifiers due to 'string' containing 'in' keyword
        var expr = _parser.Parse("42 is `string`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenIntegerValue_WhenAsInteger_ThenReturnsValue()
    {
        // Arrange
        // Note: Using delimited identifiers due to 'integer' containing 'in' keyword
        var expr = _parser.Parse("42 as `integer`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GivenIntegerValue_WhenAsString_ThenReturnsEmpty()
    {
        // Arrange
        // Note: Using delimited identifiers due to 'string' containing 'in' keyword
        var expr = _parser.Parse("42 as `string`");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Membership Operator Tests

    [Fact]
    public void GivenValueInCollection_WhenInOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("2 in (1 | 2 | 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenValueNotInCollection_WhenInOperator_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("5 in (1 | 2 | 3)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenValueInCollection_WhenContainsOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3) contains 2");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Logical Operator Tests

    [Fact]
    public void GivenTrueAndTrue_WhenAndOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("true and true");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTrueAndFalse_WhenAndOperator_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("true and false");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenTrueOrFalse_WhenOrOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("true or false");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTrueXorFalse_WhenXorOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("true xor false");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTrueXorTrue_WhenXorOperator_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("true xor true");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenFalseImpliesTrue_WhenImpliesOperator_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("false implies true");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenTrueImpliesFalse_WhenImpliesOperator_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("true implies false");
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

    /// <summary>
    /// Simple test implementation of IElement for primitive values.
    /// </summary>
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
