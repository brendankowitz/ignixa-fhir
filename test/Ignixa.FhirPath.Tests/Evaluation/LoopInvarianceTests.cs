// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// Subexpressions rooted at <c>%resource</c> are computed once per evaluation; these pin that a computed value is
/// never reused where it could differ.
/// </summary>
public class LoopInvarianceTests
{
    private static readonly IFhirSchemaProvider Schema = FhirVersion.R4.GetSchemaProvider();
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();

    [Fact]
    public void GivenAResourceRootedPathWhoseArgumentReadsTheIteration_WhenSelected_ThenEachIterationGetsItsOwnValue()
    {
        // Arrange - skip($this) depends on the enclosing select's item, so the path must be evaluated per item.
        var patient = Patient("""[{"given":["a","b","c"]}]""");
        var expression = _parser.Parse("(0 | 1 | 2).select(%resource.name.given.skip($this).count())");

        // Act
        var result = _evaluator.Evaluate(patient, expression, Bound(patient)).Select(e => e.Value).ToList();

        // Assert
        result.ShouldBe(new object?[] { 3, 2, 1 });
    }

    [Fact]
    public void GivenOneParsedExpression_WhenEvaluatedAgainstTwoResources_ThenNeitherSeesTheOthersValue()
    {
        // Arrange - the computed value lives for one evaluation, not for the parsed expression.
        var expression = _parser.Parse("(1 | 2).select(%resource.name.given.count())");
        var three = Patient("""[{"given":["a","b","c"]}]""");
        var one = Patient("""[{"given":["a"]}]""");

        // Act
        var first = _evaluator.Evaluate(three, expression, Bound(three)).Select(e => e.Value).ToList();
        var second = _evaluator.Evaluate(one, expression, Bound(one)).Select(e => e.Value).ToList();

        // Assert
        first.ShouldBe(new object?[] { 3, 3 });
        second.ShouldBe(new object?[] { 1, 1 });
    }

    private static EvaluationContext Bound(IElement resource) => new() { Resource = resource, RootResource = resource };

    private static IElement Patient(string names) =>
        ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p","name":{{names}}}""").ToElement(Schema);
}
