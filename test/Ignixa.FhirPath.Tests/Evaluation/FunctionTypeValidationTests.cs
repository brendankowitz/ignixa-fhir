/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for verifying proper error handling when functions are called on wrong types.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class FunctionTypeValidationTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region length() Type Validation Tests

    [Fact]
    public void GivenLengthOnNonString_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("length()");
        var root = new TestElement("Patient", null);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Patient", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenLengthOnInteger_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("length()");
        var root = new TestElement("integer", 42);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenLengthOnEmptyCollection_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.length()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region abs() Type Validation Tests

    [Fact]
    public void GivenAbsOnNonNumber_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("abs()");
        var root = new TestElement("Patient", null);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("abs", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Patient", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAbsOnString_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("abs()");
        var root = new TestElement("string", "hello");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("abs", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenAbsOnEmptyCollection_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.abs()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region upper() Type Validation Tests

    [Fact]
    public void GivenUpperOnNonString_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("upper()");
        var root = new TestElement("Observation", null);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("upper", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Observation", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenUpperOnInteger_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("upper()");
        var root = new TestElement("integer", 123);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("upper", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenUpperOnEmptyCollection_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.upper()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region sqrt() Type Validation Tests

    [Fact]
    public void GivenSqrtOnNonNumber_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("sqrt()");
        var root = new TestElement("Encounter", null);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("sqrt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encounter", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenSqrtOnString_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("sqrt()");
        var root = new TestElement("string", "not-a-number");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("sqrt", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenSqrtOnEmptyCollection_WhenEvaluating_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.sqrt()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Additional Type Validation Tests

    [Fact]
    public void GivenLowerOnNonString_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("lower()");
        var root = new TestElement("boolean", true);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("lower", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("boolean", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenCeilingOnNonNumber_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("ceiling()");
        var root = new TestElement("string", "test");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("ceiling", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("string", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenFloorOnNonNumber_WhenEvaluating_ThenThrowsWithTypeName()
    {
        // Arrange
        var expr = _parser.Parse("floor()");
        var root = new TestElement("Patient", null);

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList());

        Assert.Contains("floor", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Patient", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region Helper Methods

    private IElement CreateIntegerElement(int value)
    {
        return new TestElement("integer", value);
    }

    private class TestElement : IElement
    {
        public TestElement(string instanceType, object? value)
        {
            InstanceType = instanceType;
            Value = value;
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
