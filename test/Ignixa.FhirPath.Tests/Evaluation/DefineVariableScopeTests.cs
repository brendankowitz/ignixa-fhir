/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tests for the lexical containment of defineVariable() bindings.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Covers the scope boundaries <c>defineVariable</c> must respect, which the official R5 cases
/// <c>defineVariable9</c>/<c>10</c>/<c>12</c>/<c>16</c> and <c>dvUsageOutsideScopeThrows</c> assert
/// against real Patient data. These restate them over hand-built elements so a failure points at the
/// scoping rule rather than at FHIR navigation.
/// </summary>
public class DefineVariableScopeTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenVariableDefinedInsideSelect_WhenUsedAfterThatSelect_ThenSignalsError()
    {
        // Official defineVariable16: the inner select() is a scope, so 'inner' is gone by the second select().
        var expr = _parser.Parse("1.select(defineVariable('inner', 'v').select(%inner)).select(%inner)");

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(CreateInteger(0), expr).ToList());

        Assert.Contains("inner", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenVariableDefinedInsideSelect_WhenUsedWithinThatSelect_ThenResolves()
    {
        // The other half of the same rule (official defineVariable15): containment must not break the chain
        // the variable was defined in.
        var expr = _parser.Parse("1.select(defineVariable('inner', 'v').select(%inner))");

        var result = _evaluator.Evaluate(CreateInteger(0), expr).ToList();

        Assert.Single(result);
        Assert.Equal("v", result[0].Value);
    }

    [Fact]
    public void GivenVariableDefinedBeforeANestedScope_WhenUsedInsideIt_ThenStillVisible()
    {
        // Forking a scope must let the enclosing bindings through - a child scope reads outwards.
        var expr = _parser.Parse("1.defineVariable('outer', 'o').select(select(select(%outer)))");

        var result = _evaluator.Evaluate(CreateInteger(0), expr).ToList();

        Assert.Single(result);
        Assert.Equal("o", result[0].Value);
    }

    [Fact]
    public void GivenVariableDefinedInsideWhere_WhenUsedAfterThatWhere_ThenSignalsError()
    {
        var expr = _parser.Parse("(1 | 2).where(defineVariable('limit', 1).exists()).select(%limit)");

        var exception = Assert.Throws<FhirPathEvaluationException>(() => _evaluator.Evaluate(CreateInteger(0), expr).ToList());

        Assert.Contains("limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenTheSameNameDefinedPerIteration_WhenSelectRepeats_ThenEachIterationRedefinesItFreely()
    {
        // Official dvConceptMapExample: re-executing a defineVariable once per item is not a redefinition,
        // because each iteration evaluates in a scope of its own.
        var expr = _parser.Parse("(1 | 2 | 3).select(defineVariable('each', $this * 10).select(%each))");

        var result = _evaluator.Evaluate(CreateInteger(0), expr).ToList();

        Assert.Equal(3, result.Count);
        Assert.Equal(10, result[0].Value);
        Assert.Equal(20, result[1].Value);
        Assert.Equal(30, result[2].Value);
    }

    [Fact]
    public void GivenNestedScopesDefiningTheSameName_WhenReadFromTheInnerScope_ThenTheInnerBindingShadows()
    {
        var expr = _parser.Parse("1.defineVariable('v', 'outer').select(defineVariable('v2', %v).select(defineVariable('v3', 'x').select(%v2)))");

        var result = _evaluator.Evaluate(CreateInteger(0), expr).ToList();

        Assert.Single(result);
        Assert.Equal("outer", result[0].Value);
    }

    [Fact]
    public void GivenAVariableDefinedInAScope_WhenTheScopeIsLeft_ThenTheCallersContextIsUnchanged()
    {
        // Evaluation must not write into the context object the caller handed in; FhirPathInvariantCheck
        // builds a fresh context per constraint today precisely because it used to.
        var context = new EvaluationContext();
        var expr = _parser.Parse("1.defineVariable('leaked', 'v').select(%leaked)");

        _ = _evaluator.Evaluate(CreateInteger(0), expr, context).ToList();

        Assert.False(context.Variables.TryResolve("leaked", out _));
    }

    private static IElement CreateInteger(int value) => new TestElement(value);

    private sealed class TestElement(object value) : IElement
    {
        public string Name => string.Empty;
        public string InstanceType => "integer";
        public object Value { get; } = value;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => true;

        public IReadOnlyList<IElement> Children(string? name = null) => [];

        public T? Meta<T>() where T : class => null;
    }
}
