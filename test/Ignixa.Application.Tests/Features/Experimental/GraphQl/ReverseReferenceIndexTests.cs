// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Experimental.GraphQl.Schema;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ReverseReferenceIndexTests
{
    private static readonly string[] ResourceTypes = ["CodeSystem", "Observation", "Patient", "Provenance"];

    private static SearchParameterInfo Reference(string code, params string[] targets) =>
        new(code, code, SearchParamType.Reference, targetResourceTypes: targets);

    private static SearchParameterInfo Token(string code) =>
        new(code, code, SearchParamType.Token);

    private static ReverseReferenceIndex Build(Dictionary<string, SearchParameterInfo[]> parametersByType) =>
        ReverseReferenceIndex.Build(
            ResourceTypes,
            resourceType => parametersByType.TryGetValue(resourceType, out var parameters) ? parameters : []);

    [Fact]
    public void GivenReferenceParameterTargetingType_WhenBuilding_ThenSourceIsReferencingType()
    {
        var index = Build(new() { ["Observation"] = [Reference("subject", "Patient")] });

        index.GetReferencingTypes("Patient").ShouldBe(["Observation"]);
    }

    [Fact]
    public void GivenSourceWithOnlyNonReferenceParameters_WhenBuilding_ThenSourceReferencesNothing()
    {
        var index = Build(new()
        {
            ["CodeSystem"] = [Token("code"), Token("url")],
            ["Observation"] = [Reference("subject", "Patient")],
        });

        index.GetReferencingTypes("Patient").ShouldNotContain("CodeSystem");
        foreach (var target in ResourceTypes)
            index.GetReferencingTypes(target).ShouldNotContain("CodeSystem");
    }

    [Fact]
    public void GivenReferenceParameterTargetingOtherType_WhenBuilding_ThenSourceIsNotReferencingType()
    {
        var index = Build(new() { ["Observation"] = [Reference("subject", "Patient")] });

        index.GetReferencingTypes("CodeSystem").ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferenceParameterWithoutTargets_WhenBuilding_ThenSourceReferencesEveryType()
    {
        var index = Build(new() { ["Provenance"] = [Reference("target")] });

        foreach (var target in ResourceTypes)
            index.GetReferencingTypes(target).ShouldContain("Provenance");
    }

    [Theory]
    [InlineData("Resource")]
    [InlineData("DomainResource")]
    public void GivenReferenceParameterTargetingAbstractBase_WhenBuilding_ThenSourceReferencesEveryType(string abstractTarget)
    {
        var index = Build(new() { ["Provenance"] = [Reference("target", "Patient", abstractTarget)] });

        foreach (var target in ResourceTypes)
            index.GetReferencingTypes(target).ShouldContain("Provenance");
    }

    [Fact]
    public void GivenSeveralReferencingTypes_WhenBuilding_ThenTheyAreListedInResourceTypeOrder()
    {
        var index = Build(new()
        {
            ["Provenance"] = [Reference("patient", "Patient")],
            ["Observation"] = [Reference("subject", "Patient")],
            ["Patient"] = [Reference("link", "Patient")],
        });

        index.GetReferencingTypes("Patient").ShouldBe(["Observation", "Patient", "Provenance"]);
    }

    [Fact]
    public void GivenTargetOutsideResourceTypes_WhenBuilding_ThenTargetIsIgnored()
    {
        var index = Build(new() { ["Observation"] = [Reference("subject", "Group")] });

        index.GetReferencingTypes("Group").ShouldBeEmpty();
        foreach (var target in ResourceTypes)
            index.GetReferencingTypes(target).ShouldBeEmpty();
    }
}
