// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Aggregates resources from one or more patient-centric <see cref="ScenarioContext"/>s, plus
/// non-patient workflow resources (appointments, lists, locations), into a single cross-patient
/// graph. Keeps <see cref="ScenarioBuilder"/>'s one-scenario-one-patient boundary intact: a
/// multi-patient workflow composes several <see cref="ScenarioContext"/>s into one graph rather than
/// growing <see cref="ScenarioContext"/> itself.
/// </summary>
public sealed class ResourceGraph
{
    private readonly List<ResourceJsonNode> _resources = [];

    /// <summary>Gets all resources currently in the graph, in the order they were added.</summary>
    public IReadOnlyList<ResourceJsonNode> AllResources => _resources;

    /// <summary>Adds every resource from a patient-centric scenario to the graph.</summary>
    public void AddScenario(ScenarioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _resources.AddRange(context.AllResources);
    }

    /// <summary>Adds a single non-patient workflow resource (e.g. an Appointment) to the graph.</summary>
    public void AddResource(ResourceJsonNode resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
    }
}
