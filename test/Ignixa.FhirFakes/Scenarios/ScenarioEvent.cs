// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Represents a single event in a scenario timeline.
/// Records when FHIR resources were created and what clinical event they represent.
/// </summary>
/// <param name="Timestamp">The date/time when this event occurred.</param>
/// <param name="EventType">The type of event (e.g., "Encounter", "Observation", "MedicationOrder").</param>
/// <param name="ResourceId">The FHIR resource ID associated with this event.</param>
/// <param name="ResourceType">The FHIR resource type (e.g., "Encounter", "Observation").</param>
/// <param name="Description">Human-readable description of the event.</param>
public record ScenarioEvent(
    DateTime Timestamp,
    string EventType,
    string ResourceId,
    string ResourceType,
    string Description);
