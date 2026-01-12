// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Lifecycle;

/// <summary>
/// Defines an age-based event that executes during patient lifecycle simulation.
/// Lifecycle events represent recurring or one-time clinical activities that occur
/// at specific patient ages (e.g., wellness visits, immunizations, condition onset).
/// </summary>
/// <remarks>
/// <para>
/// This interface is the foundation of Layer 3 (Patient Lifecycles) in the Faker architecture.
/// Lifecycle events are orchestrated over a patient's entire lifetime from birth to death,
/// enabling simulation of realistic patient journeys with age-appropriate clinical activities.
/// </para>
/// <para>
/// Common lifecycle event patterns:
/// - Scheduled wellness visits (pediatric, adult, geriatric)
/// - Immunization schedules (CDC childhood schedule, adult boosters)
/// - Age-stratified disease onset (probabilistic conditions)
/// - Routine screenings (mammography, colonoscopy, etc.)
/// </para>
/// </remarks>
public interface ILifecycleEvent
{
    /// <summary>
    /// Determines whether this event should execute at the specified patient age.
    /// </summary>
    /// <param name="patientAge">The patient's current age in years.</param>
    /// <returns>
    /// <c>true</c> if the event should execute at this age; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is called during lifecycle simulation for each year of the patient's life.
    /// Implementations should return <c>true</c> when the event is scheduled for the given age.
    /// </para>
    /// <para>
    /// Examples:
    /// - Pediatric wellness visit: return true for ages [1, 2, 4, 6, 8, 10, 12, 14, 16, 18]
    /// - Annual adult wellness: return true for age >= 18
    /// - Probabilistic condition: return true if age is in onset range and condition hasn't occurred yet
    /// </para>
    /// </remarks>
    bool IsApplicable(int patientAge);

    /// <summary>
    /// Executes the lifecycle event, adding appropriate resources to the scenario context.
    /// </summary>
    /// <param name="context">
    /// The scenario context containing the patient and accumulated resources.
    /// The context provides access to the current simulation time, patient demographics,
    /// and previously generated resources.
    /// </param>
    /// <param name="schemaProvider">
    /// The FHIR schema provider for resource generation.
    /// Used to create FHIR-compliant resources for the configured FHIR version.
    /// </param>
    /// <remarks>
    /// <para>
    /// This method is invoked when <see cref="IsApplicable"/> returns <c>true</c>.
    /// Implementations should use the provided context to generate clinically appropriate resources
    /// such as encounters, observations, immunizations, or conditions.
    /// </para>
    /// <para>
    /// The context's <c>CurrentTime</c> should be used for resource timestamps to maintain
    /// temporal consistency across the patient timeline.
    /// </para>
    /// </remarks>
    void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider);
}
