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

    #region Complex Scoping Tests

    [Fact]
    public void GivenDefineVariable_WhenUsedInNestedSelect_ThenVariableAccessible()
    {
        // defineVariable value is accessible in nested select
        var expr = _parser.Parse("defineVariable('x', 100) | %x + 1");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // defineVariable returns focus (0), then union with %x + 1 = 101
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].Value);
        Assert.Equal(101, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedAcrossChainedOperations_ThenVariableAccessible()
    {
        // Variable defined early is accessible in later operations
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).where($this > %threshold).select($this * 10)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // where filters to 3, 4 -> select multiplies to 30, 40
        Assert.Equal(2, result.Count);
        Assert.Equal(30, result[0].Value);
        Assert.Equal(40, result[1].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenShadowingExistingVariable_ThenInnerScopeSeesNewValue()
    {
        // Inner defineVariable can shadow outer variable
        var expr = _parser.Parse("defineVariable('x', 10).where(false) | defineVariable('x', %x + 5).where(false) | %x");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // First defineVariable sets x=10
        // Second defineVariable sets x=%x+5 = 10+5 = 15
        // %x returns 15
        Assert.Single(result);
        Assert.Equal(15, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInAggregate_ThenVariableAccessibleInAccumulator()
    {
        // defineVariable should be accessible within aggregate expression
        var expr = _parser.Parse("(1 | 2 | 3).defineVariable('base', 100).aggregate($total + $this + %base, 0)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        // Iteration 0: 0 + 1 + 100 = 101
        // Iteration 1: 101 + 2 + 100 = 203
        // Iteration 2: 203 + 3 + 100 = 306
        Assert.Equal(306, result.Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenMultipleVariablesChained_ThenAllAccessible()
    {
        // Multiple chained defineVariable calls, all accessible
        var expr = _parser.Parse("defineVariable('a', 10).defineVariable('b', 20).defineVariable('c', %a + %b).where(false) | %c");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(30, result[0].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedInIif_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4).defineVariable('threshold', 2).select(iif($this > %threshold, 'high', 'low'))");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(4, result.Count);
        Assert.Equal("low", result[0].Value);  // 1 <= 2
        Assert.Equal("low", result[1].Value);  // 2 <= 2
        Assert.Equal("high", result[2].Value); // 3 > 2
        Assert.Equal("high", result[3].Value); // 4 > 2
    }

    [Fact]
    public void GivenDefineVariable_WhenOneArgument_ThenUsesFocusAsValue()
    {
        // Single argument form: defineVariable('name') uses current focus as value
        var expr = _parser.Parse("(5 | 10 | 15).defineVariable('items') | %items.sum()");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // defineVariable returns focus (5, 10, 15), then union with %items.sum() = 30
        Assert.Equal(4, result.Count);
        Assert.Equal(5, result[0].Value);
        Assert.Equal(10, result[1].Value);
        Assert.Equal(15, result[2].Value);
        Assert.Equal(30, result[3].Value);
    }

    [Fact]
    public void GivenDefineVariable_WhenReferencingUndefinedVariable_ThenReturnsEmpty()
    {
        var expr = _parser.Parse("%undefined");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedWithExists_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).defineVariable('target', 3).exists($this = %target)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenDefineVariable_WhenUsedWithAll_ThenVariableAccessible()
    {
        var expr = _parser.Parse("(5 | 10 | 15).defineVariable('min', 4).all($this > %min)");
        var root = CreateIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    #endregion

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
