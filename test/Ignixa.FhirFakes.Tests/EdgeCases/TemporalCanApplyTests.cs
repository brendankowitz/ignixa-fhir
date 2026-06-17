// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.EdgeCases.Strategies;
using Ignixa.Serialization.SourceNodes;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.EdgeCases;

public class TemporalCanApplyTests
{
    private const string SampleJson = """
        {
          "resourceType": "Patient",
          "id": "t-test",
          "gender": "male",
          "birthDate": "1990-03-15",
          "name": [{ "family": "Smith", "given": ["John"] }]
        }
        """;

    [Theory]
    [InlineData("1990-03-15")]
    [InlineData("2000")]
    [InlineData("2021-06")]
    [InlineData("2021-06-15T10:30:00Z")]
    [InlineData("2024-02-29")]
    public void GivenFhirDateShapedValue_WhenCheckingCanApply_ThenEligible(string dateValue)
    {
        var parent = new JsonObject { ["birthDate"] = dateValue };
        var target = new PropertyTarget(parent, "birthDate", "birthDate", dateValue);
        var strategy = new LeapYearTemporalStrategy();

        var result = strategy.CanApply(target);

        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Smith")]
    [InlineData("2021abc")]
    [InlineData("MRN-001")]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("http://example.org")]
    public void GivenNonDateValue_WhenCheckingCanApply_ThenNotEligible(string value)
    {
        var parent = new JsonObject { ["text"] = value };
        var target = new PropertyTarget(parent, "text", "text", value);
        var strategy = new LeapYearTemporalStrategy();

        var result = strategy.CanApply(target);

        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenTemporalStrategyViaFullPipeline_WhenApplied_ThenOnlyDateShapedLeavesMutated()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["temporal"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42).Apply(resource, strategies);

        manifest.Mutations.ShouldAllBe(m => m.Path == "birthDate");
        resource.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>().ShouldBe("Smith");
    }
}
