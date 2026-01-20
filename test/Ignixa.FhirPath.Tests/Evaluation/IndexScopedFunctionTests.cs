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
}
