// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// The output of invoking a workflow scenario pack: the assembled resource graph and its manifest.
/// Bundle composition (transaction/batch) is a separate step via <see cref="ResourceBundleComposer"/>,
/// applied to <see cref="Graph"/>'s <see cref="ResourceGraph.AllResources"/> — packs are responsible for
/// graph assembly only.
/// </summary>
public sealed class WorkflowScenarioResult
{
    /// <summary>The assembled, cross-patient resource graph.</summary>
    public required ResourceGraph Graph { get; init; }

    /// <summary>Manifest metadata describing this generation run.</summary>
    public required WorkflowManifest Manifest { get; init; }
}
