/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath defineVariable() function (FHIRPath 2.0).
 * Tests variable definition, scoping, and usage in subsequent expressions.
 */

using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class DefineVariableFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenDefineVariable_WhenReferencingVariable_ThenReturnsDefinedValue()
    {
        var expr = _parser.Parse("defineVariable('x', 5) | %x");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Value);
        Assert.Equal(5, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsingInWhereClause_ThenFiltersCorrectly()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).where($this > %threshold)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Value);
        Assert.Equal(4, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariableWithExpression_WhenReferencingVariable_ThenEvaluatesExpression()
    {
        var expr = _parser.Parse("defineVariable('doubled', 5 * 2) | %doubled");
        var root = CreateIntegerElement(10);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(10, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenVariableIsString_ThenStoresStringValue()
    {
        var expr = _parser.Parse("defineVariable('name', 'test') | %name");
        var root = CreateStringElement("root");

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("root", result[0].Value);
        Assert.Equal("test", result[1].Value);
    }

    [Fact]
    public void GivenDefineVariableChained_WhenMultipleVariables_ThenBothAvailable()
    {
        var expr = _parser.Parse("defineVariable('a', 1).defineVariable('b', 2) | %a + %b");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Value);
        Assert.Equal(3, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariableWithCollection_WhenReferencingVariable_ThenReturnsCollection()
    {
        var expr = _parser.Parse("defineVariable('items', 1 | 2 | 3) | %items.count()");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Value);
        Assert.Equal(3, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariableInvalidArgumentCount_WhenEvaluating_ThenThrowsException()
    {
        // Test with 0 arguments - should throw
        var expr = _parser.Parse("defineVariable()");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList()
        );

        Assert.Contains("1 or 2 arguments", exception.Message, StringComparison.Ordinal);

        // Test with 3 arguments - should also throw
        var expr3 = _parser.Parse("defineVariable('x', 1, 2)");
        var exception3 = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr3).ToList()
        );

        Assert.Contains("1 or 2 arguments", exception3.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariableNonStringName_WhenEvaluating_ThenThrowsException()
    {
        var expr = _parser.Parse("defineVariable(5, 10)");
        var root = CreateIntegerElement(0);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _evaluator.Evaluate(root, expr).ToList()
        );

        Assert.Contains("defineVariable requires a string as the first argument", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInSelect_ThenVariableAvailable()
    {
        var expr = _parser.Parse("(1 | 2 | 3).defineVariable('multiplier', 2).select($this * %multiplier)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result[0].Value);
        Assert.Equal(4, result[1].Value);
        Assert.Equal(6, result[2].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenReturnsOriginalFocus_ThenFocusUnchanged()
    {
        var expr = _parser.Parse("(5 | 10).defineVariable('x', 100)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal(5, result[0].Value);
        Assert.Equal(10, result[1].Value);
    }

    private static IElement CreateIntegerElement(int value)
    {
        return new TestElement(value, "integer");
    }

    private static IElement CreateStringElement(string value)
    {
        return new TestElement(value, "string");
    }

    private class TestElement(object value, string type) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => type;
        public object Value => value;
        public string Location => string.Empty;
        public IType? Type => null;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
