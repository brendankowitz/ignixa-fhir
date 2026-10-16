/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for host functions bound through EvaluationContext.HostFunctions.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// A host binds functions by name, and the engine calls them with the focus and the evaluated arguments, as it hands
/// <c>resolve()</c> to <see cref="FhirEvaluationContext.ElementResolver"/>. New capability: before it, an unknown function name
/// always failed as not implemented.
/// </summary>
public class HostFunctionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IElement _patient = ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1","name":[{"family":"Doe"},{"family":"Roe"}]}""").ToElement(FhirVersion.R4.GetSchemaProvider());

    private List<IElement> Evaluate(string expression, IReadOnlyDictionary<string, HostFunction>? functions) =>
        [.. _evaluator.Evaluate(_patient, _parser.Parse(expression), new FhirEvaluationContext { HostFunctions = functions })];

    [Fact]
    public void GivenABoundFunction_WhenCalled_ThenItReceivesTheFocusAndEachArgumentEvaluatedInTheCallersContext()
    {
        IReadOnlyList<IElement>? seenFocus = null;
        IReadOnlyList<IReadOnlyList<IElement>>? seenArguments = null;
        var functions = new Dictionary<string, HostFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["echo"] = (focus, arguments) =>
            {
                (seenFocus, seenArguments) = (focus, arguments);
                return focus;
            },
        };

        List<IElement> result = Evaluate("name.echo('x', id, name.family)", functions);

        result.Count.ShouldBe(2);
        seenFocus.ShouldNotBeNull().Select(name => name.Children("family").Single().Value).ShouldBe(["Doe", "Roe"]);
        seenArguments.ShouldNotBeNull().Count.ShouldBe(3);
        seenArguments[0].Select(item => item.Value).ShouldBe(["x"]);
        seenArguments[1].Select(item => item.Value).ShouldBe(["p1"]);
        seenArguments[2].Select(item => item.Value).ShouldBe(["Doe", "Roe"]);
    }

    [Fact]
    public void GivenABoundFunctionInsideWhere_WhenEvaluated_ThenItsFocusIsEachItemInTurn()
    {
        var functions = new Dictionary<string, HostFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["isDoe"] = (focus, _) => focus.ShouldHaveSingleItem().Children("family").Single().Value as string == "Doe" ? focus : [],
        };

        List<IElement> result = Evaluate("name.where(isDoe().exists()).family", functions);

        result.Select(item => item.Value).ShouldBe(["Doe"]);
    }

    [Fact]
    public void GivenANameBoundByTheHostAndBuiltIn_WhenCalled_ThenTheHostsFunctionAnswers()
    {
        var functions = new Dictionary<string, HostFunction>(StringComparer.OrdinalIgnoreCase) { ["count"] = (_, _) => [] };

        Evaluate("name.count()", functions).ShouldBeEmpty();
        Evaluate("name.count()", functions: null).Single().Value.ShouldBe(2);
    }

    [Fact]
    public void GivenANameBoundNowhere_WhenCalled_ThenItFailsAsAnUnknownFunction()
    {
        var functions = new Dictionary<string, HostFunction>(StringComparer.OrdinalIgnoreCase) { ["echo"] = (focus, _) => focus };

        Should.Throw<NotSupportedException>(() => Evaluate("name.unknownFunction()", functions)).Message.ShouldContain("unknownFunction");
    }
}
