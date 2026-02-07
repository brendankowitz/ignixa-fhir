/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath type conversion, string manipulation, and utility functions (Phase 3).
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class ConversionAndStringFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Type Conversion Function Tests

    [Fact]
    public void GivenStringNumber_WhenToInteger_ThenConvertsToInteger()
    {
        // Arrange
        var expr = _parser.Parse("'42'.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42, result.Value);
        Assert.Equal("integer", result.InstanceType);
    }

    [Fact]
    public void GivenInvalidString_WhenToInteger_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'abc'.toInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenIntegerString_WhenToDecimal_ThenConvertsToDecimal()
    {
        // Arrange
        var expr = _parser.Parse("'3.14'.toDecimal()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(3.14m, result.Value);
        Assert.Equal("decimal", result.InstanceType);
    }

    [Fact]
    public void GivenInteger_WhenToString_ThenConvertsToString()
    {
        // Arrange
        var expr = _parser.Parse("42.toString()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("42", result.Value);
        Assert.Equal("string", result.InstanceType);
    }

    [Fact]
    public void GivenTrueString_WhenToBoolean_ThenConvertsToBoolean()
    {
        // Arrange
        var expr = _parser.Parse("'true'.toBoolean()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
        Assert.Equal("boolean", result.InstanceType);
    }

    [Fact]
    public void GivenValidDateString_WhenToDate_ThenConvertsToDate()
    {
        // Arrange
        var expr = _parser.Parse("'2025-01-15'.toDate()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("2025-01-15", result.Value);
        Assert.Equal("date", result.InstanceType);
    }

    [Fact]
    public void GivenValidDateTimeString_WhenToDateTime_ThenConvertsToDateTime()
    {
        // Arrange
        var expr = _parser.Parse("'2025-01-15T10:30:00Z'.toDateTime()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("2025-01-15T10:30:00Z", result.Value);
        Assert.Equal("dateTime", result.InstanceType);
    }

    [Fact]
    public void GivenValidTimeString_WhenToTime_ThenConvertsToTime()
    {
        // Arrange
        var expr = _parser.Parse("'10:30:00'.toTime()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("10:30:00", result.Value);
        Assert.Equal("time", result.InstanceType);
    }

    [Fact]
    public void GivenStringNumber_WhenToLong_ThenConvertsToLong()
    {
        // Arrange
        var expr = _parser.Parse("'42'.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenLargeStringNumber_WhenToLong_ThenConvertsToLong()
    {
        // Arrange - Value larger than int.MaxValue
        var expr = _parser.Parse("'3000000000'.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(3000000000L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenInteger_WhenToLong_ThenConvertsToLong()
    {
        // Arrange
        var expr = _parser.Parse("42.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenLongLiteral_WhenToLong_ThenReturnsLong()
    {
        // Arrange
        var expr = _parser.Parse("42L.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenBooleanTrue_WhenToLong_ThenReturnsOneLong()
    {
        // Arrange
        var expr = _parser.Parse("true.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenBooleanFalse_WhenToLong_ThenReturnsZeroLong()
    {
        // Arrange
        var expr = _parser.Parse("false.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(0L, result.Value);
        Assert.Equal("long", result.InstanceType);
    }

    [Fact]
    public void GivenInvalidString_WhenToLong_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("'abc'.toLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Type Checking Function Tests

    [Fact]
    public void GivenValidIntegerString_WhenConvertsToInteger_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'42'.convertsToInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenInvalidString_WhenConvertsToInteger_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("'abc'.convertsToInteger()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenValidDecimalString_WhenConvertsToDecimal_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'3.14'.convertsToDecimal()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenValidBooleanString_WhenConvertsToBoolean_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'true'.convertsToBoolean()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenValidDateString_WhenConvertsToDate_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'2025-01-15'.convertsToDate()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenValidLongString_WhenConvertsToLong_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'42'.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenLargeLongString_WhenConvertsToLong_ThenReturnsTrue()
    {
        // Arrange - Value larger than int.MaxValue
        var expr = _parser.Parse("'3000000000'.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenInvalidString_WhenConvertsToLong_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("'abc'.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenInteger_WhenConvertsToLong_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("42.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenLongLiteral_WhenConvertsToLong_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("42L.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenBoolean_WhenConvertsToLong_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("true.convertsToLong()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    #endregion

    #region Conditional Function Tests

    [Fact]
    public void GivenTrueCondition_WhenIif_ThenReturnsTrueBranch()
    {
        // Arrange
        var expr = _parser.Parse("iif(true, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("yes", result.Value);
    }

    [Fact]
    public void GivenFalseCondition_WhenIif_ThenReturnsFalseBranch()
    {
        // Arrange
        var expr = _parser.Parse("iif(false, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("no", result.Value);
    }

    [Fact]
    public void GivenEmptyCondition_WhenIif_ThenReturnsOtherwiseResult()
    {
        // Arrange - Per FHIRPath spec testCollectionBoolean2: iif({}, true, false) → false
        // Empty criterion is treated as falsy, so returns otherwise-result
        var expr = _parser.Parse("iif({}, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert - Empty criterion returns the 'else' branch, not empty
        Assert.Single(result);
        Assert.Equal("no", result[0].Value);
    }

    [Fact]
    public void GivenBooleanExpression_WhenIifCondition_ThenEvaluatesCorrectly()
    {
        // Arrange - Boolean expression (1 = 1) evaluates to true boolean, should work
        var expr = _parser.Parse("iif(1 = 1, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("yes", result[0].Value);
    }

    [Fact]
    public void GivenCollectionOfIntegers_WhenIifCondition_ThenShouldRejectAsSemanticError()
    {
        // Arrange
        var expr = _parser.Parse("iif(1 | 2 | 3, true, false)");
        var root = CreateIntegerElement(0);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(root, expr).ToList());
    }

    [Fact]
    public void GivenCollectionFocus_WhenIifCalled_ThenShouldRejectAsSemanticError()
    {
        // Arrange
        var expr = _parser.Parse("('item1' | 'item2').iif(true, 'true-result', 'false-result')");
        var root = CreateIntegerElement(0);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(root, expr).ToList());
    }

    [Fact]
    public void GivenNonBooleanSingleValue_WhenIifCondition_ThenShouldRejectAsSemanticError()
    {
        // Arrange
        var expr = _parser.Parse("iif('non boolean criteria', 'true-result', 'false-result')");
        var root = CreateIntegerElement(0);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _evaluator.Evaluate(root, expr).ToList());
    }

    #endregion

    #region String Manipulation Function Tests

    [Fact]
    public void GivenString_WhenIndexOf_ThenReturnsIndex()
    {
        // Arrange
        // Note: Using delimited identifier due to 'indexOf' containing 'in' keyword
        var expr = _parser.Parse("'Hello World'.`indexOf`('World')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(6, result.Value);
    }

    [Fact]
    public void GivenString_WhenIndexOfNotFound_ThenReturnsNegativeOne()
    {
        // Arrange
        // Note: Using delimited identifier due to 'indexOf' containing 'in' keyword
        var expr = _parser.Parse("'Hello World'.`indexOf`('xyz')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-1, result.Value);
    }

    [Fact]
    public void GivenString_WhenLastIndexOf_ThenReturnsLastIndex()
    {
        // Arrange
        var expr = _parser.Parse("'abc abc'.`lastIndexOf`('a')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void GivenString_WhenLastIndexOfNotFound_ThenReturnsNegativeOne()
    {
        // Arrange
        var expr = _parser.Parse("'abcdefg'.`lastIndexOf`('x')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-1, result.Value);
    }

    [Fact]
    public void GivenString_WhenLastIndexOfEmptyString_ThenReturnsLength()
    {
        // Arrange
        var expr = _parser.Parse("'0123'.`lastIndexOf`('')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(4, result.Value);
    }

    [Fact]
    public void GivenString_WhenLastIndexOfSubstring_ThenReturnsFirstOccurrence()
    {
        // Arrange
        var expr = _parser.Parse("'abcdefg'.`lastIndexOf`('bc')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public void GivenEmptyCollection_WhenLastIndexOf_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.`lastIndexOf`('a')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenString_WhenSubstring_ThenReturnsSubstring()
    {
        // Arrange
        var expr = _parser.Parse("'Hello World'.substring(6, 5)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("World", result.Value);
    }

    [Fact]
    public void GivenString_WhenSubstringWithZeroLength_ThenReturnsEmptyString()
    {
        // Arrange - per FHIRPath spec, zero length returns empty string
        var expr = _parser.Parse("'abcdefg'.substring(3, 0)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void GivenString_WhenSubstringWithNegativeLength_ThenReturnsEmptyString()
    {
        // Arrange - per FHIRPath spec, negative length returns empty string
        var expr = _parser.Parse("'abcdefg'.substring(3, -1)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(string.Empty, result.Value);
    }

    [Fact]
    public void GivenString_WhenSubstringWithNegativeStart_ThenReturnsEmptyCollection()
    {
        // Arrange - per FHIRPath spec, start outside string returns empty collection
        var expr = _parser.Parse("'abcdefg'.substring(-1, 2)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenString_WhenStartsWith_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'Hello World'.startsWith('Hello')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenEndsWith_ThenReturnsTrue()
    {
        // Arrange
        var expr = _parser.Parse("'Hello World'.endsWith('World')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenUpper_ThenReturnsUppercase()
    {
        // Arrange
        var expr = _parser.Parse("'hello'.upper()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("HELLO", result.Value);
    }

    [Fact]
    public void GivenString_WhenLower_ThenReturnsLowercase()
    {
        // Arrange
        var expr = _parser.Parse("'HELLO'.lower()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void GivenString_WhenLength_ThenReturnsLength()
    {
        // Arrange
        var expr = _parser.Parse("'Hello World'.length()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(11, result.Value);
    }

    #endregion

    #region Regex Function Tests

    [Fact]
    public void GivenString_WhenReplace_ThenReplacesSubstring()
    {
        // Arrange
        var expr = _parser.Parse("'Hello World'.replace('World', 'FhirPath')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("Hello FhirPath", result.Value);
    }

    [Fact]
    public void GivenString_WhenMatches_ThenReturnsTrueForMatch()
    {
        // Arrange
        var expr = _parser.Parse("'Hello123'.matches('[A-Za-z]+[0-9]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenMatchesNoMatch_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("'Hello'.matches('[0-9]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenReplaceMatches_ThenReplacesPattern()
    {
        // Arrange
        var expr = _parser.Parse("'Hello123World456'.replaceMatches('[0-9]+', 'NUM')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("HelloNUMWorldNUM", result.Value);
    }

    [Fact]
    public void GivenString_WhenMatchesFull_ThenReturnsTrueForFullMatch()
    {
        // Arrange
        var expr = _parser.Parse("'Hello123'.matchesFull('[A-Za-z]+[0-9]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenMatchesFullPartialMatch_ThenReturnsFalse()
    {
        // Arrange - 'Hello123World' does not fully match pattern for letters then digits
        var expr = _parser.Parse("'Hello123World'.matchesFull('[A-Za-z]+[0-9]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenMatchesFullNoMatch_ThenReturnsFalse()
    {
        // Arrange
        var expr = _parser.Parse("'Hello'.matchesFull('[0-9]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.False((bool)result.Value!);
    }

    [Fact]
    public void GivenString_WhenMatchesFullWithFlags_ThenIgnoresCase()
    {
        // Arrange
        var expr = _parser.Parse("'HELLO'.matchesFull('hello', 'i')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenEmptyCollection_WhenMatchesFull_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.matchesFull('[a-z]+')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenString_WhenToChars_ThenReturnsSingleCharacters()
    {
        // Arrange
        var expr = _parser.Parse("'ABC'.toChars()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("A", result[0].Value);
        Assert.Equal("B", result[1].Value);
        Assert.Equal("C", result[2].Value);
    }

    #endregion

    #region Tree Navigation Function Tests

    [Fact]
    public void GivenPatientResource_WhenChildren_ThenReturnsDirectChildren()
    {
        // Arrange
        var expr = _parser.Parse("children()");
        var root = CreatePatientWithName();

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
        // Should return direct children like 'name', 'gender', etc.
    }

    [Fact]
    public void GivenPatientResource_WhenDescendants_ThenReturnsAllDescendants()
    {
        // Arrange
        var expr = _parser.Parse("descendants()");
        var root = CreatePatientWithName();

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.NotEmpty(result);
        // Should return all descendants recursively
    }

    #endregion

    #region Utility Function Tests

    [Fact]
    public void GivenValue_WhenTrace_ThenReturnsValue()
    {
        // Arrange
        var expr = _parser.Parse("42.trace('test')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GivenNoInput_WhenNow_ThenReturnsCurrentDateTime()
    {
        // Arrange
        var expr = _parser.Parse("now()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal("dateTime", result.InstanceType);
    }

    [Fact]
    public void GivenNoInput_WhenToday_ThenReturnsCurrentDate()
    {
        // Arrange
        var expr = _parser.Parse("today()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal("date", result.InstanceType);
    }

    [Fact]
    public void GivenNoInput_WhenTimeOfDay_ThenReturnsCurrentTime()
    {
        // Arrange
        var expr = _parser.Parse("timeOfDay()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal("time", result.InstanceType);
    }

    #endregion

    #region toQuantity Unit Conversion Tests

    [Fact]
    public void GivenQuantityInYears_WhenToQuantityWithMonths_ThenConvertsCorrectly()
    {
        // Arrange - 1 year = 12 months
        var expr = _parser.Parse("(1 year).toQuantity('month')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(12m, quantity.Value);
        Assert.Equal("month", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInMonths_WhenToQuantityWithDays_ThenConvertsCorrectly()
    {
        // Arrange - 1 month = 30 days (per FHIRPath spec)
        var expr = _parser.Parse("(1 month).toQuantity('day')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(30m, quantity.Value);
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInDays_WhenToQuantityWithHours_ThenConvertsCorrectly()
    {
        // Arrange - 1 day = 24 hours
        var expr = _parser.Parse("(1 day).toQuantity('hour')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(24m, quantity.Value);
        Assert.Equal("hour", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInHours_WhenToQuantityWithMinutes_ThenConvertsCorrectly()
    {
        // Arrange - 1 hour = 60 minutes
        var expr = _parser.Parse("(1 hour).toQuantity('minute')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(60m, quantity.Value);
        Assert.Equal("minute", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInMinutes_WhenToQuantityWithSeconds_ThenConvertsCorrectly()
    {
        // Arrange - 1 minute = 60 seconds
        var expr = _parser.Parse("(1 minute).toQuantity('second')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(60m, quantity.Value);
        Assert.Equal("second", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInYears_WhenToQuantityWithDays_ThenConvertsCorrectly()
    {
        // Arrange - 1 year = 365 days (per FHIRPath spec)
        var expr = _parser.Parse("(1 year).toQuantity('day')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(365m, quantity.Value);
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenQuantityInDays_WhenToQuantityWithWeek_ThenConvertsCorrectly()
    {
        // Arrange - 7 days = 1 week
        var expr = _parser.Parse("(7 days).toQuantity('week')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(1m, quantity.Value);
        Assert.Equal("week", quantity.Unit);
    }

    [Fact]
    public void GivenQuantity_WhenToQuantityWithSameUnit_ThenReturnsUnchanged()
    {
        // Arrange - same unit conversion should return same value
        var expr = _parser.Parse("(5 day).toQuantity('day')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(5m, quantity.Value);
        Assert.Equal("day", quantity.Unit);
    }

    [Fact]
    public void GivenQuantity_WhenToQuantityWithNoUnit_ThenReturnsOriginal()
    {
        // Arrange - no unit argument should return original quantity
        var expr = _parser.Parse("(5 day).toQuantity()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Single(result);
        var quantity = result[0].Value as Ignixa.FhirPath.Types.Quantity;
        Assert.NotNull(quantity);
        Assert.Equal(5m, quantity.Value);
        // Calendar keyword "day" is preserved as-is when no unit conversion requested
        Assert.Equal("day", quantity.Unit);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private IElement CreateStringElement(string value)
    {
        return new PrimitiveElement(value, "string");
    }

    private IElement CreatePatientWithName()
    {
        var children = new List<IElement>
        {
            new ComplexElement("name", "HumanName", new List<IElement>
            {
                new PrimitiveElement("John", "string", "given"),
                new PrimitiveElement("Doe", "string", "family")
            }),
            new PrimitiveElement("male", "code", "gender")
        };

        return new ComplexElement("Patient", "Patient", children);
    }

    /// <summary>
    /// Simple test implementation of IElement for primitive values.
    /// </summary>
    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string type, string? name = null)
        {
            Value = value;
            InstanceType = type;
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>
    /// Test implementation of IElement for complex types with children.
    /// </summary>
    private class ComplexElement : IElement
    {
        private readonly List<IElement> _children;

        public ComplexElement(string name, string type, List<IElement> children)
        {
            Name = name;
            InstanceType = type;
            _children = children;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }

            return _children.Where(c => c.Name == name).ToList();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
