using System.Collections.Generic;
using System.Linq;
using Xunit;
using Ignixa.FhirPath;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class IndexScopedFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    private class TestIntegerElement : IElement
    {
        public TestIntegerElement(int value)
        {
            Value = value;
        }

        public string Name => "integer";
        public string InstanceType => "integer";
        public object Value { get; }
        public string Location => "";
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }

    [Fact]
    public void GivenWhereFunction_WhenIndexAccessed_ThenFiltersCorrectly()
    {
        // Spec Failing Example: where($index = 3)
        // Test: (10 | 20 | 30 | 40 | 50).where($index = 2) -> Should be 30
        
        var expr = _parser.Parse("(10 | 20 | 30 | 40 | 50).where($index = 2)");
        var root = new TestIntegerElement(0); // Dummy root

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(30, result[0].Value);
    }

    [Fact]
    public void GivenAllFunction_WhenIndexAccessed_ThenEvaluatesCorrectly()
    {
        // Test: (0 | 1 | 2).all($this = $index) -> Should be true
        
        var expr = _parser.Parse("(0 | 1 | 2).all($this = $index)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenExistsFunction_WhenIndexAccessed_ThenEvaluatesCorrectly()
    {
        // Test: (10 | 20 | 30).exists($index = 1 and $this = 20) -> Should be true
        
        var expr = _parser.Parse("(10 | 20 | 30).exists($index = 1 and $this = 20)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenAggregateFunction_WhenIndexAccessed_ThenAggregatesCorrectly()
    {
        // Test: (1 | 2 | 3).aggregate($total + $this + $index, 0)
        // Iteration 0: total=0, this=1, index=0 -> 1
        // Iteration 1: total=1, this=2, index=1 -> 4
        // Iteration 2: total=4, this=3, index=2 -> 9
        
        var expr = _parser.Parse("(1 | 2 | 3).aggregate($total + $this + $index, 0)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.Equal(9, result.Value);
    }

    [Fact]
    public void GivenAnyFunction_WhenIndexAccessed_ThenEvaluatesCorrectly()
    {
        // Test: (10 | 20 | 30).any($index = 2 and $this = 30) -> Should be true
        
        var expr = _parser.Parse("(10 | 20 | 30).any($index = 2 and $this = 30)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).Single();

        Assert.True((bool)result.Value!);
    }

    [Fact]
    public void GivenStringToCharsSelectWhereIndex_WhenIndexAccessed_ThenFiltersCorrectly()
    {
        // Simulates the user's example: identifier.value.toChars().select(toInteger()).where($index = 3)
        // For string "8003", char at index 3 is '3' -> toInteger() = 3
        // '8' -> 8 at index 0
        // '0' -> 0 at index 1
        // '0' -> 0 at index 2
        // '3' -> 3 at index 3 <- this one should be returned
        var expr = _parser.Parse("'8003'.toChars().select(toInteger()).where($index = 3)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Single(result);
        Assert.Equal(3, result[0].Value); // '3' at index 3 converted to integer 3
    }

    [Fact]
    public void GivenNestedSelectWhere_WhenIndexAccessedInWhere_ThenUsesWhereIndex()
    {
        // select(toInteger()) sets $index per item, then where($index = 3) should filter by where's index
        var expr = _parser.Parse("('a' | 'b' | 'c' | 'd' | 'e').select($index).where($index = 2)");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // select($index) produces: 0, 1, 2, 3, 4
        // where($index = 2) filters to item at index 2, which is value 2
        Assert.Single(result);
        Assert.Equal(2, result[0].Value);
    }

    [Fact]
    public void GivenSelectWithInnerWhere_WhenBothUseIndex_ThenInnerScopeShadowsOuter()
    {
        // Test nested select containing where - inner where() shadows outer select() scope
        // In FHIRPath, inner scopes shadow outer $this and $index
        // If you need outer scope, use defineVariable()
        var expr = _parser.Parse("(1 | 2 | 3).select((10 | 20 | 30).where($index = 1).first())");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // Inner where filters (10, 20, 30) by index=1, returning 20
        // This happens for each outer iteration, so we get: 20, 20, 20
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal(20, r.Value));
    }

    [Fact]
    public void GivenDefineVariableWithNestedWhere_WhenOuterThisNeeded_ThenVariablePreservesIt()
    {
        // Use defineVariable to preserve outer $this when inner scope shadows it
        // Chain via select: defineVariable -> select to access variable in inner where
        var expr = _parser.Parse("(0 | 1 | 2).select(defineVariable('outer', $this).select((10 | 20 | 30).where($index = %outer).first()).first())");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // For outer $this=0: %outer=0, where($index=0) -> 10
        // For outer $this=1: %outer=1, where($index=1) -> 20
        // For outer $this=2: %outer=2, where($index=2) -> 30
        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].Value);
        Assert.Equal(20, result[1].Value);
        Assert.Equal(30, result[2].Value);
    }

    [Fact]
    public void GivenWhereInsideSelect_WhenIndexReferencedInBoth_ThenScopedIndependently()
    {
        // Test that inner where() gets its own $index scope
        var expr = _parser.Parse("(100 | 200).select((1 | 2 | 3 | 4).where($index > 1))");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // For each item in (100, 200), filter (1,2,3,4) where inner $index > 1
        // Inner indices: 1->0, 2->1, 3->2, 4->3
        // where($index > 1) keeps: 3 (idx 2), 4 (idx 3) - twice
        Assert.Equal(4, result.Count);
        Assert.Equal(3, result[0].Value);
        Assert.Equal(4, result[1].Value);
        Assert.Equal(3, result[2].Value);
        Assert.Equal(4, result[3].Value);
    }

    [Fact]
    public void GivenIifInsideSelect_WhenIndexUsed_ThenPreservesOuterIndex()
    {
        // iif() inside select() should preserve the select()'s $index
        var expr = _parser.Parse("('a' | 'b' | 'c' | 'd').select(iif($index < 2, 'early', 'late'))");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        Assert.Equal(4, result.Count);
        Assert.Equal("early", result[0].Value); // index 0 < 2
        Assert.Equal("early", result[1].Value); // index 1 < 2
        Assert.Equal("late", result[2].Value);  // index 2 >= 2
        Assert.Equal("late", result[3].Value);  // index 3 >= 2
    }

    [Fact]
    public void GivenTripleNestedScopes_WhenEachUsesIndex_ThenAllScopedCorrectly()
    {
        // Deep nesting: select -> where -> select
        var expr = _parser.Parse("(1 | 2).select((10 | 20 | 30).where($index != 1).select($this + $index))");
        var root = new TestIntegerElement(0);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // Outer select iterates (1, 2)
        // For each: where($index != 1) on (10,20,30) keeps 10(idx0), 30(idx2)
        // Inner select on (10, 30) adds inner $index: 10+0=10, 30+1=31
        // Result: 10, 31, 10, 31
        Assert.Equal(4, result.Count);
        Assert.Equal(10, result[0].Value);
        Assert.Equal(31, result[1].Value);
        Assert.Equal(10, result[2].Value);
        Assert.Equal(31, result[3].Value);
    }

    #region Non-Scoped Function Context Tests

    [Fact]
    public void GivenNonScopedFunction_WhenTraceInArgument_ThenUsesOuterContext()
    {
        // Per FHIRPath spec: Non-scoped functions (like contains) should NOT change $this
        // In: 'abc'.contains(trace('t').length().toString())
        // - trace('t') returns $this which should be the outer context (integer root), not 'abc'
        // - Since our root is an integer, trace returns the integer, and length() on integer throws
        
        var expr = _parser.Parse("'abc'.contains(trace('t').length().toString())");
        var root = new TestIntegerElement(42);

        // Should throw because trace() returns $this (integer 42), 
        // and length() is not valid on integers
        var ex = Assert.Throws<InvalidOperationException>(() => 
            _evaluator.Evaluate(root, expr).ToList());
        
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("integer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GivenNonScopedFunction_WhenThisInArgument_ThenUsesOuterContext()
    {
        // $this inside non-scoped function argument should be outer context
        // In: 'hello'.contains($this.toString())
        // $this should be the integer root (42), not 'hello'
        
        var expr = _parser.Parse("'hello42'.contains($this.toString())");
        var root = new TestIntegerElement(42);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // $this is 42, '42'.toString() = '42'
        // 'hello42'.contains('42') = true
        Assert.Single(result);
        Assert.Equal(true, result[0].Value);
    }

    [Fact]
    public void GivenScopedFunction_WhenThisInCriteria_ThenUsesCurrentItem()
    {
        // Contrast: scoped function SHOULD change $this
        // In: ('abc' | 'xyz').where($this = 'abc')
        // $this should be each item being tested
        
        var expr = _parser.Parse("('abc' | 'xyz').where($this = 'abc')");
        var root = new TestIntegerElement(42);

        var result = _evaluator.Evaluate(root, expr).ToList();

        // where() is scoped - $this is each item
        Assert.Single(result);
        Assert.Equal("abc", result[0].Value);
    }

    #endregion
}
