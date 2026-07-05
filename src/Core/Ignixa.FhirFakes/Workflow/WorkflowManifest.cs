// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Metadata describing one workflow scenario generation run. Lets a caller (CLI, test) confirm what
/// was generated, and tells a generic composer what the pack's primary matched resource type is,
/// without the composer needing scenario-specific knowledge.
/// </summary>
public sealed class WorkflowManifest
{
    /// <summary>The invoked scenario id (e.g. "DailyAppointmentSchedule").</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The seed used for this run, or null if unseeded.</summary>
    public int? Seed { get; init; }

    /// <summary>The FHIR resource type this pack's search response should treat as the primary match (e.g. "Appointment").</summary>
    public required string PrimaryResourceType { get; init; }

    /// <summary>Resource counts by FHIR resource type (e.g. "Patient" -> 12).</summary>
    public required IReadOnlyDictionary<string, int> ResourceCountsByType { get; init; }
}
