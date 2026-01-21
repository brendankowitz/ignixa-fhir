/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for FhirPath trace() function.
 * Tests trace functionality including TraceHandler invocation,
 * custom names, and return value behavior.
 */

using System.Collections.Immutable;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class TraceFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    #region Basic Trace Behavior Tests

    [Fact]
    public void GivenFocus_WhenTrace_ThenReturnsFocusUnchanged()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).trace()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Value);
        Assert.Equal(2, result[1].Value);
        Assert.Equal(3, result[2].Value);
    }

    [Fact]
    public void GivenEmptyCollection_WhenTrace_ThenReturnsEmpty()
    {
        // Arrange
        var expr = _parser.Parse("{}.trace()");
        var root = CreateIntegerElement(0);

        // Act
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GivenNoTraceHandler_WhenTrace_ThenDoesNotThrow()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2).trace('test')");
        var root = CreateIntegerElement(0);

        // Act - should not throw
        var result = _evaluator.Evaluate(root, expr).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region TraceHandler Invocation Tests

    [Fact]
    public void GivenTraceHandler_WhenTrace_ThenInvokesHandler()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2 | 3).trace()");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("trace", capturedTrace.Name);
        Assert.Equal(3, capturedTrace.Focus.Count);
        Assert.Equal(1, capturedTrace.Focus[0].Value);
        Assert.Equal(2, capturedTrace.Focus[1].Value);
        Assert.Equal(3, capturedTrace.Focus[2].Value);
    }

    [Fact]
    public void GivenTraceHandlerWithName_WhenTrace_ThenUsesCustomName()
    {
        // Arrange
        var expr = _parser.Parse("(5 | 10).trace('my-checkpoint')");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("my-checkpoint", capturedTrace.Name);
        Assert.Equal(2, capturedTrace.Focus.Count);
    }

    [Fact]
    public void GivenMultipleTraces_WhenEvaluate_ThenInvokesHandlerMultipleTimes()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2).trace('first') | (3 | 4).trace('second')");
        var root = CreateIntegerElement(0);
        
        var traces = new List<TraceEntry>();
        var context = new EvaluationContext()
            .WithTraceHandler(entry => traces.Add(entry));

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.Equal(2, traces.Count);
        Assert.Equal("first", traces[0].Name);
        Assert.Equal(2, traces[0].Focus.Count);
        Assert.Equal("second", traces[1].Name);
        Assert.Equal(2, traces[1].Focus.Count);
    }

    #endregion

    #region TraceEntry Content Tests

    [Fact]
    public void GivenSingleElement_WhenTrace_ThenCapturesSingleElement()
    {
        // Arrange
        var expr = _parser.Parse("42.trace('answer')");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("answer", capturedTrace.Name);
        Assert.Single(capturedTrace.Focus);
        Assert.Equal(42, capturedTrace.Focus[0].Value);
    }

    [Fact]
    public void GivenEmptyCollection_WhenTrace_ThenCapturesEmptyCollection()
    {
        // Arrange
        var expr = _parser.Parse("{}.trace('empty')");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("empty", capturedTrace.Name);
        Assert.Empty(capturedTrace.Focus);
    }

    [Fact]
    public void GivenStringElements_WhenTrace_ThenCapturesStrings()
    {
        // Arrange
        var expr = _parser.Parse("('hello' | 'world').trace('strings')");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("strings", capturedTrace.Name);
        Assert.Equal(2, capturedTrace.Focus.Count);
        Assert.Equal("hello", capturedTrace.Focus[0].Value);
        Assert.Equal("world", capturedTrace.Focus[1].Value);
    }

    #endregion

    #region Dynamic Name Expression Tests

    [Fact]
    public void GivenDynamicNameExpression_WhenTrace_ThenEvaluatesExpression()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2).trace('prefix-' + 'suffix')");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.NotNull(capturedTrace);
        Assert.Equal("prefix-suffix", capturedTrace.Name);
    }

    #endregion

    #region TraceEntry ToString Tests

    [Fact]
    public void GivenTraceEntry_WhenToString_ThenFormatsCorrectly()
    {
        // Arrange
        var elements = ImmutableList.Create<IElement>(
            CreateIntegerElement(42),
            CreateIntegerElement(100)
        );
        var entry = new TraceEntry("test-trace", elements);

        // Act
        var str = entry.ToString();

        // Assert
        Assert.Contains("test-trace", str, StringComparison.Ordinal);
        Assert.Contains("2 elements", str, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTraceEntryWithSingleElement_WhenToString_ThenShowsElement()
    {
        // Arrange
        var elements = ImmutableList.Create<IElement>(CreateIntegerElement(42));
        var entry = new TraceEntry("single", elements);

        // Act
        var str = entry.ToString();

        // Assert
        Assert.Contains("single", str, StringComparison.Ordinal);
        Assert.Contains("1 element", str, StringComparison.Ordinal);
        Assert.Contains("42", str, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenEmptyTraceEntry_WhenToString_ThenShowsEmpty()
    {
        // Arrange
        var elements = ImmutableList<IElement>.Empty;
        var entry = new TraceEntry("empty-trace", elements);

        // Act
        var str = entry.ToString();

        // Assert
        Assert.Contains("empty-trace", str, StringComparison.Ordinal);
        Assert.Contains("empty", str, StringComparison.Ordinal);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void GivenComplexExpression_WhenTraceInMiddle_ThenTracesCurrent()
    {
        // Arrange
        // Trace after filtering but before counting
        var expr = _parser.Parse("(1 | 2 | 3 | 4 | 5).where($this > 2).trace('filtered').count()");
        var root = CreateIntegerElement(0);
        
        TraceEntry? capturedTrace = null;
        var context = new EvaluationContext()
            .WithTraceHandler(entry => capturedTrace = entry);

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert - result should be count (3)
        Assert.Single(result);
        Assert.Equal(3, result[0].Value);
        
        // Trace should have captured the 3 filtered elements
        Assert.NotNull(capturedTrace);
        Assert.Equal("filtered", capturedTrace.Name);
        Assert.Equal(3, capturedTrace.Focus.Count);
        Assert.Equal(3, capturedTrace.Focus[0].Value);
        Assert.Equal(4, capturedTrace.Focus[1].Value);
        Assert.Equal(5, capturedTrace.Focus[2].Value);
    }

    [Fact]
    public void GivenNestedTrace_WhenEvaluate_ThenCapturesAll()
    {
        // Arrange
        var expr = _parser.Parse("(1 | 2).trace('outer').where($this > 0).trace('inner')");
        var root = CreateIntegerElement(0);
        
        var traces = new List<TraceEntry>();
        var context = new EvaluationContext()
            .WithTraceHandler(entry => traces.Add(entry));

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert
        Assert.Equal(2, traces.Count);
        Assert.Equal("outer", traces[0].Name);
        Assert.Equal(2, traces[0].Focus.Count);
        Assert.Equal("inner", traces[1].Name);
        Assert.Equal(2, traces[1].Focus.Count);
    }

    [Fact]
    public void GivenTraceWithProjection_WhenEvaluate_ThenTracesProjectionResult()
    {
        // Arrange - trace with projection: trace('name', projection)
        // The projection is evaluated for each element, and that result is traced
        // But the original focus is still returned unchanged
        var expr = _parser.Parse("(1 | 2 | 3).trace('doubled', $this * 2)");
        var root = CreateIntegerElement(0);
        
        var traces = new List<TraceEntry>();
        var context = new EvaluationContext()
            .WithTraceHandler(entry => traces.Add(entry));

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert - result should be original values (1, 2, 3)
        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Value);
        Assert.Equal(2, result[1].Value);
        Assert.Equal(3, result[2].Value);
        
        // Trace should have captured projected values (2, 4, 6) - one trace per element
        Assert.Equal(3, traces.Count);
        Assert.All(traces, t => Assert.Equal("doubled", t.Name));
        Assert.Equal(2, traces[0].Focus[0].Value);  // 1 * 2 = 2
        Assert.Equal(4, traces[1].Focus[0].Value);  // 2 * 2 = 4
        Assert.Equal(6, traces[2].Focus[0].Value);  // 3 * 2 = 6
    }

    [Fact]
    public void GivenTraceWithIndexProjection_WhenEvaluate_ThenTracesIndexForEachElement()
    {
        // Arrange - trace with $index in projection: trace('name', $index)
        // Per FHIRPath 3.0, $index should be available in trace projection
        var expr = _parser.Parse("('a' | 'b' | 'c').trace('index', $index)");
        var root = CreateIntegerElement(0);
        
        var traces = new List<TraceEntry>();
        var context = new EvaluationContext()
            .WithTraceHandler(entry => traces.Add(entry));

        // Act
        var result = _evaluator.Evaluate(root, expr, context).ToList();

        // Assert - result should be original values ('a', 'b', 'c')
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].Value);
        Assert.Equal("b", result[1].Value);
        Assert.Equal("c", result[2].Value);
        
        // Trace should have captured index values (0, 1, 2) - one trace per element
        Assert.Equal(3, traces.Count);
        Assert.All(traces, t => Assert.Equal("index", t.Name));
        Assert.Equal(0, traces[0].Focus[0].Value);  // index 0
        Assert.Equal(1, traces[1].Focus[0].Value);  // index 1
        Assert.Equal(2, traces[2].Focus[0].Value);  // index 2
    }

    #endregion

    #region Helper Methods

    private static IElement CreateIntegerElement(int value)
    {
        return new SimpleElement(value, "integer");
    }

    private sealed class SimpleElement : IElement
    {
        public SimpleElement(object value, string instanceType)
        {
            Value = value;
            InstanceType = instanceType;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => Array.Empty<IElement>();
        public T? Meta<T>() where T : class => null;
    }

    #endregion
}
