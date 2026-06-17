// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
          "name": [{ "family": "Smith", "given": ["John"], "text": "2021" }]
        }
        """;

    [Fact]
    public void GivenDateTypedElement_WhenCheckingCanApply_ThenEligible()
    {
        var target = EdgeCaseTargetFactory.AtPath(SampleJson, "Patient.birthDate");
        var strategy = new LeapYearTemporalStrategy();

        var result = strategy.CanApply(target);

        target.InstanceType.ShouldBe("date");
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenStringElementHoldingDateShapedValue_WhenCheckingCanApply_ThenNotEligible()
    {
        var target = EdgeCaseTargetFactory.AtPath(SampleJson, "Patient.name[0].text");
        var strategy = new LeapYearTemporalStrategy();

        var result = strategy.CanApply(target);

        target.InstanceType.ShouldBe("string");
        target.Value.ShouldBe("2021");
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenTemporalStrategyViaFullPipeline_WhenApplied_ThenOnlyDateTypedLeavesMutated()
    {
        var strategies = EdgeCaseCatalog.CreateDefault().Resolve(["temporal"]);
        var resource = ResourceJsonNode.Parse(SampleJson);

        var manifest = new EdgeCasePipeline(42, EdgeCaseTargetFactory.Schema).Apply(resource, strategies);

        manifest.Mutations.ShouldAllBe(m => m.Path == "Patient.birthDate");
        resource.MutableNode["gender"]?.GetValue<string>().ShouldBe("male");
        resource.MutableNode["name"]?.AsArray()?[0]?.AsObject()?["family"]?.GetValue<string>().ShouldBe("Smith");
    }
}
