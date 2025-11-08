/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath type conversion, string manipulation, and utility functions (Phase 3).
 */

using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class ConversionAndStringFunctionTests
{
    private readonly FhirPathCompiler _compiler = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Type Conversion Function Tests

    [Fact]
    public void GivenStringNumber_WhenToInteger_ThenConvertsToInteger()
    {
        // Arrange
        var expr = _compiler.Parse("'42'.toInteger()");
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
        var expr = _compiler.Parse("'abc'.toInteger()");
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
        var expr = _compiler.Parse("'3.14'.toDecimal()");
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
        var expr = _compiler.Parse("42.toString()");
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
        var expr = _compiler.Parse("'true'.toBoolean()");
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
        var expr = _compiler.Parse("'2025-01-15'.toDate()");
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
        var expr = _compiler.Parse("'2025-01-15T10:30:00Z'.toDateTime()");
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
        var expr = _compiler.Parse("'10:30:00'.toTime()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("10:30:00", result.Value);
        Assert.Equal("time", result.InstanceType);
    }

    #endregion

    #region Type Checking Function Tests

    [Fact]
    public void GivenValidIntegerString_WhenConvertsToInteger_ThenReturnsTrue()
    {
        // Arrange
        var expr = _compiler.Parse("'42'.convertsToInteger()");
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
        var expr = _compiler.Parse("'abc'.convertsToInteger()");
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
        var expr = _compiler.Parse("'3.14'.convertsToDecimal()");
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
        var expr = _compiler.Parse("'true'.convertsToBoolean()");
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
        var expr = _compiler.Parse("'2025-01-15'.convertsToDate()");
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
        var expr = _compiler.Parse("iif(true, 'yes', 'no')");
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
        var expr = _compiler.Parse("iif(false, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("no", result.Value);
    }

    [Fact]
    public void GivenEmptyCondition_WhenIif_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _compiler.Parse("iif({}, 'yes', 'no')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region String Manipulation Function Tests

    [Fact]
    public void GivenString_WhenIndexOf_ThenReturnsIndex()
    {
        // Arrange
        // Note: Using delimited identifier due to 'indexOf' containing 'in' keyword
        var expr = _compiler.Parse("'Hello World'.`indexOf`('World')");
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
        var expr = _compiler.Parse("'Hello World'.`indexOf`('xyz')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal(-1, result.Value);
    }

    [Fact]
    public void GivenString_WhenSubstring_ThenReturnsSubstring()
    {
        // Arrange
        var expr = _compiler.Parse("'Hello World'.substring(6, 5)");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("World", result.Value);
    }

    [Fact]
    public void GivenString_WhenStartsWith_ThenReturnsTrue()
    {
        // Arrange
        var expr = _compiler.Parse("'Hello World'.startsWith('Hello')");
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
        var expr = _compiler.Parse("'Hello World'.endsWith('World')");
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
        var expr = _compiler.Parse("'hello'.upper()");
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
        var expr = _compiler.Parse("'HELLO'.lower()");
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
        var expr = _compiler.Parse("'Hello World'.length()");
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
        var expr = _compiler.Parse("'Hello World'.replace('World', 'FhirPath')");
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
        var expr = _compiler.Parse("'Hello123'.matches('[A-Za-z]+[0-9]+')");
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
        var expr = _compiler.Parse("'Hello'.matches('[0-9]+')");
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
        var expr = _compiler.Parse("'Hello123World456'.replaceMatches('[0-9]+', 'NUM')");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.Equal("HelloNUMWorldNUM", result.Value);
    }

    [Fact]
    public void GivenString_WhenToChars_ThenReturnsSingleCharacters()
    {
        // Arrange
        var expr = _compiler.Parse("'ABC'.toChars()");
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
        var expr = _compiler.Parse("children()");
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
        var expr = _compiler.Parse("descendants()");
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
        var expr = _compiler.Parse("42.trace('test')");
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
        var expr = _compiler.Parse("now()");
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
        var expr = _compiler.Parse("today()");
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
        var expr = _compiler.Parse("timeOfDay()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).Single();

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal("time", result.InstanceType);
    }

    #endregion

    #region Helper Methods

    private ITypedElement CreateIntegerElement(int value)
    {
        return new PrimitiveTypedElement(value, "integer");
    }

    private ITypedElement CreateStringElement(string value)
    {
        return new PrimitiveTypedElement(value, "string");
    }

    private ITypedElement CreatePatientWithName()
    {
        var children = new List<ITypedElement>
        {
            new ComplexTypedElement("name", "HumanName", new List<ITypedElement>
            {
                new PrimitiveTypedElement("John", "string", "given"),
                new PrimitiveTypedElement("Doe", "string", "family")
            }),
            new PrimitiveTypedElement("male", "code", "gender")
        };

        return new ComplexTypedElement("Patient", "Patient", children);
    }

    /// <summary>
    /// Simple test implementation of ITypedElement for primitive values.
    /// </summary>
    private class PrimitiveTypedElement : ITypedElement
    {
        public PrimitiveTypedElement(object value, string type, string? name = null)
        {
            Value = value;
            InstanceType = type;
            Name = name ?? string.Empty;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null) => Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// Test implementation of ITypedElement for complex types with children.
    /// </summary>
    private class ComplexTypedElement : ITypedElement
    {
        private readonly List<ITypedElement> _children;

        public ComplexTypedElement(string name, string type, List<ITypedElement> children)
        {
            Name = name;
            InstanceType = type;
            _children = children;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value => null;
        public string Location => string.Empty;
        public IElementDefinitionSummary? Definition => null;

        public IEnumerable<ITypedElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }

            return _children.Where(c => c.Name == name);
        }
    }

    #endregion
}
