// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Immutable;
using Ignixa.DeId.Models;
using Ignixa.DeId.Processors;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.DeId.Tests.Processors;

public class PerturbProcessorTests
{
    private readonly R4CoreSchemaProvider _schema = new();

    [Theory]
    [InlineData("""{"resourceType":"Observation","valueDecimal":12.345}""", "valueDecimal", 2, 12.34)]
    [InlineData("""{"resourceType":"Observation","valueDecimal":12.345}""", "valueDecimal", 0, 12)]
    [InlineData("""{"resourceType":"Observation","valueInteger":13}""", "valueInteger", 2, 13)]
    [InlineData("""{"resourceType":"Observation","valueQuantity":{"value":12.345,"unit":"mg"}}""", "valueQuantity", 2, 12.34)]
    public async Task GivenZeroSpan_WhenPerturbing_ThenValueIsRoundedAndPathsAreTracked(
        string json, string field, int roundTo, double expected)
    {
        var resource = ResourceJsonNode.Parse(json);
        var node = resource.ToElement(_schema).Children(field).First();
        var processor = new PerturbProcessor(_schema);
        var context = new ProcessorContext
        {
            ResourceId = "observation",
            VisitedNodes = [],
            Settings = new Dictionary<string, object> { ["span"] = 0, ["roundTo"] = roundTo }.ToImmutableDictionary()
        };

        var result = await processor.ProcessAsync(resource, node, context, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasModified.ShouldBeTrue();
        result.Value.OperationType.ShouldBe(DeIdOperations.Perturb);
        result.Value.ProcessedPaths.ShouldBe([node.Location]);
        resource.InvalidateCaches();
        var updated = resource.ToElement(_schema);
        var value = updated.Scalar(field == "valueQuantity" ? "valueQuantity.value" : field);
        Convert.ToDecimal(value).ShouldBe((decimal)expected);
        if (field == "valueQuantity")
        {
            context.VisitedNodes.ShouldContain(node.Children("value").First().Location);
            context.VisitedNodes.ShouldContain(node.Children("unit").First().Location);
            updated.Scalar("valueQuantity.unit").ShouldBe("mg");
        }
    }

    [Theory]
    [InlineData("""{"resourceType":"Observation","valueString":"original"}""", "valueString", false)]
    [InlineData("""{"resourceType":"Observation","valueQuantity":{"unit":"mg"}}""", "valueQuantity", false)]
    [InlineData("""{"resourceType":"Observation","valueQuantity":{"value":12.345}}""", "valueQuantity", true)]
    public async Task GivenIneligibleOrVisitedValue_WhenPerturbing_ThenResourceIsUnchanged(
        string json, string field, bool visited)
    {
        var resource = ResourceJsonNode.Parse(json);
        var node = resource.ToElement(_schema).Children(field).First();
        var context = new ProcessorContext
        {
            ResourceId = "observation",
            VisitedNodes = visited ? [node.Children("value").First().Location] : [],
            Settings = ImmutableDictionary<string, object>.Empty
        };
        var before = resource.SerializeToString();

        var result = await new PerturbProcessor(_schema).ProcessAsync(resource, node, context, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasModified.ShouldBeFalse();
        result.Value.ProcessedPaths.ShouldBeEmpty();
        resource.SerializeToString().ShouldBe(before);
    }

    [Fact]
    public async Task GivenMissingSettings_WhenPerturbing_ThenFailureIncludesNodePath()
    {
        var resource = ResourceJsonNode.Parse("""{"resourceType":"Observation","valueDecimal":12.345}""");
        var node = resource.ToElement(_schema).Children("valueDecimal").First();
        var context = new ProcessorContext { ResourceId = "observation", VisitedNodes = [] };

        var result = await new PerturbProcessor(_schema).ProcessAsync(resource, node, context, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("PROCESSOR_ERROR");
        result.Error.Path.ShouldBe(node.Location);
        result.Error.Exception.ShouldBeOfType<ArgumentNullException>();
        resource.InvalidateCaches();
        resource.ToElement(_schema).Scalar("valueDecimal").ShouldBe(12.345m);
    }
}
