// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Fluent wrapper over <see cref="ResourceGraph"/> that lets a workflow scenario register
/// <see cref="IResourceGraphEnricher"/> factories while the graph is still being assembled, then
/// applies them all, in registration order, at <see cref="Build"/> time. Deferring construction this
/// way lets each factory read graph state (practitioners, patients, encounters) that may not exist
/// yet at the point the enricher is registered, but is guaranteed to exist by the time it runs.
/// </summary>
public sealed class WorkflowGraphBuilder
{
    private readonly ResourceGraph _graph = new();
    private readonly List<Func<ResourceGraph, IResourceGraphEnricher>> _enricherFactories = [];

    /// <summary>Adds every resource from a patient-centric scenario to the graph.</summary>
    public WorkflowGraphBuilder AddScenario(ScenarioContext context)
    {
        _graph.AddScenario(context);
        return this;
    }

    /// <summary>Adds a single non-patient workflow resource (e.g. an Appointment) to the graph.</summary>
    public WorkflowGraphBuilder AddResource(ResourceJsonNode resource)
    {
        _graph.AddResource(resource);
        return this;
    }

    /// <summary>
    /// Registers one or more enricher factories to run at <see cref="Build"/> time, in registration
    /// order. Each factory is invoked with the graph as assembled at <see cref="Build"/> time, so it
    /// can construct its enricher from state that only exists once every scenario has been added.
    /// </summary>
    public WorkflowGraphBuilder WithEnrichers(params Func<ResourceGraph, IResourceGraphEnricher>[] enricherFactories)
    {
        ArgumentNullException.ThrowIfNull(enricherFactories);
        _enricherFactories.AddRange(enricherFactories);
        return this;
    }

    /// <summary>Applies every registered enricher factory, in registration order, and returns the finished graph.</summary>
    public ResourceGraph Build(ResourceGraphEnrichmentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var factory in _enricherFactories)
        {
            factory(_graph).Enrich(_graph, context);
        }

        return _graph;
    }
}
