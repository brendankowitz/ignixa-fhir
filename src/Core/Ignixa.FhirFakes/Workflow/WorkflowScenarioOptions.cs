// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Cross-cutting options for a workflow scenario pack: seed, clock, and tag. Pack-specific knobs
/// (e.g. appointment count) stay as factory-method parameters so they surface through
/// <see cref="Scenarios.DiscoveredScenarioParameter"/> discovery metadata instead of being buried here.
/// </summary>
public sealed record WorkflowScenarioOptions
{
    /// <summary>Seed for reproducible generation. Null means unseeded.</summary>
    public int? Seed { get; init; }

    /// <summary>The clock backing generated timestamps. Defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Tag code applied to generated resources, for test isolation via the <c>_tag</c> search parameter.</summary>
    public string? Tag { get; init; }
}
