// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class ResourceGraphTests
{
    [Fact]
    public void GivenNewGraph_WhenCreated_ThenAllResourcesIsEmpty()
    {
        var graph = new ResourceGraph();

        graph.AllResources.ShouldBeEmpty();
    }

    [Fact]
    public void GivenScenarioContext_WhenAddingScenario_ThenAllOfItsResourcesAppear()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var context = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var graph = new ResourceGraph();

        graph.AddScenario(context);

        graph.AllResources.Count.ShouldBe(context.AllResources.Count);
        graph.AllResources.ShouldContain(context.Patient);
    }

    [Fact]
    public void GivenTwoScenarios_WhenBothAdded_ThenResourcesFromBothAppear()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var first = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var second = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var graph = new ResourceGraph();

        graph.AddScenario(first);
        graph.AddScenario(second);

        graph.AllResources.Count.ShouldBe(first.AllResources.Count + second.AllResources.Count);
    }

    [Fact]
    public void GivenNullScenario_WhenAdding_ThenThrowsArgumentNullException()
    {
        var graph = new ResourceGraph();

        Should.Throw<ArgumentNullException>(() => graph.AddScenario(null!));
    }
}
