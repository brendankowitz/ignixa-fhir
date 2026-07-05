// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class WorkflowGraphBuilderTests
{
    [Fact]
    public void GivenNoScenariosAdded_WhenBuilt_ThenGraphIsEmpty()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var builder = new WorkflowGraphBuilder();

        var graph = builder.Build(EnrichmentContext(schemaProvider));

        graph.AllResources.ShouldBeEmpty();
    }

    [Fact]
    public void GivenScenarioAdded_WhenBuilt_ThenItsResourcesAppearInGraph()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var context = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var builder = new WorkflowGraphBuilder();

        var graph = builder.AddScenario(context).Build(EnrichmentContext(schemaProvider));

        graph.AllResources.ShouldContain(context.Patient);
    }

    [Fact]
    public void GivenEnricherFactoryRegistered_WhenBuilt_ThenItRunsAgainstFinalGraphState()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var first = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var second = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var builder = new WorkflowGraphBuilder();
        int? observedResourceCountAtFactoryInvocation = null;

        builder.AddScenario(first);
        builder.WithEnrichers(graph =>
        {
            observedResourceCountAtFactoryInvocation = graph.AllResources.Count;
            return new RecordingEnricher();
        });
        builder.AddScenario(second);

        var finalGraph = builder.Build(EnrichmentContext(schemaProvider));

        observedResourceCountAtFactoryInvocation.ShouldBe(finalGraph.AllResources.Count);
    }

    [Fact]
    public void GivenMultipleEnrichers_WhenBuilt_ThenTheyRunInRegistrationOrder()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var builder = new WorkflowGraphBuilder();
        var executionOrder = new List<int>();

        builder.WithEnrichers(
            _ => new RecordingEnricher(() => executionOrder.Add(1)),
            _ => new RecordingEnricher(() => executionOrder.Add(2)));

        builder.Build(EnrichmentContext(schemaProvider));

        executionOrder.ShouldBe([1, 2]);
    }

    [Fact]
    public void GivenNullEnricherFactories_WhenRegistering_ThenThrowsArgumentNullException()
    {
        var builder = new WorkflowGraphBuilder();

        Should.Throw<ArgumentNullException>(() => builder.WithEnrichers(null!));
    }

    [Fact]
    public void GivenNullContext_WhenBuilding_ThenThrowsArgumentNullException()
    {
        var builder = new WorkflowGraphBuilder();

        Should.Throw<ArgumentNullException>(() => builder.Build(null!));
    }

    private static ResourceGraphEnrichmentContext EnrichmentContext(Ignixa.Abstractions.IFhirSchemaProvider schemaProvider) => new()
    {
        SchemaProvider = schemaProvider,
        Faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1),
        Clock = TimeProvider.System,
    };

    private sealed class RecordingEnricher(Action? onEnrich = null) : IResourceGraphEnricher
    {
        public void Enrich(ResourceGraph graph, ResourceGraphEnrichmentContext context) => onEnrich?.Invoke();
    }
}
