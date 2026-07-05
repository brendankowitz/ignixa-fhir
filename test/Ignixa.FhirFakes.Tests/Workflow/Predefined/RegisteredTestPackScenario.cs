// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Workflow;

namespace Ignixa.FhirFakes.Tests.Workflow.Predefined;

/// <summary>
/// Test-only workflow scenario pack. Lives outside <c>Ignixa.FhirFakes</c>'s own assembly to prove
/// <see cref="WorkflowScenarioCatalog.RegisterAssembly"/> discovers packs from a registered external
/// assembly, matched by the <c>.Workflow.Predefined</c> namespace suffix rather than assembly identity.
/// </summary>
public static class RegisteredTestPackScenario
{
    public static WorkflowScenarioResult GetRegisteredTestPack(IFhirSchemaProvider schemaProvider, WorkflowScenarioOptions options) =>
        new()
        {
            Graph = new ResourceGraph(),
            Manifest = new WorkflowManifest
            {
                ScenarioId = "RegisteredTestPack",
                Seed = options.Seed,
                PrimaryResourceType = "Basic",
                ResourceCountsByType = new Dictionary<string, int>(),
            },
        };
}
