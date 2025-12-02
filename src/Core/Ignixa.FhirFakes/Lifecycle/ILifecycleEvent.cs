// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Represents an age-based lifecycle event that can occur during a patient's life simulation.
/// Lifecycle events are triggered at specific ages and may generate FHIR resources based on
/// clinical schedules, probabilistic disease onset, or other age-dependent health events.
/// </summary>
/// <remarks>
/// <para>
/// Implementations include:
/// <list type="bullet">
///   <item><description><c>PediatricWellnessSchedule</c> - Wellness visits at ages 1, 2, 4, 6, 8, 10, 12, 14, 16, 18</description></item>
///   <item><description><c>AdultWellnessSchedule</c> - Annual wellness visits starting at age 18</description></item>
///   <item><description><c>ImmunizationScheduleEvent</c> - Vaccinations per CDC schedule</description></item>
///   <item><description><c>ProbabilisticConditionOnset</c> - Disease onset with epidemiological probabilities</description></item>
/// </list>
/// </para>
/// <para>
/// Example usage:
/// <code>
/// var event = new PediatricWellnessSchedule();
/// if (event.IsApplicable(patientAge: 6))
/// {
///     event.Execute(context, schemaProvider);
/// }
/// </code>
/// </para>
/// </remarks>
public interface ILifecycleEvent
{
    /// <summary>
    /// Gets the name of this lifecycle event for logging and debugging purposes.
    /// </summary>
    /// <example>"PediatricWellnessSchedule", "AdultWellnessSchedule", "ImmunizationSchedule"</example>
    string Name { get; }

    /// <summary>
    /// Determines whether this lifecycle event should be triggered at the specified patient age.
    /// </summary>
    /// <param name="patientAge">The patient's age in years (0 = birth year).</param>
    /// <returns>
    /// <c>true</c> if the event should be executed at this age; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// Events may return <c>true</c> for:
    /// <list type="bullet">
    ///   <item><description>Specific ages (e.g., pediatric wellness at ages 1, 2, 4, etc.)</description></item>
    ///   <item><description>Age ranges (e.g., adult wellness for ages >= 18)</description></item>
    ///   <item><description>One-time conditions that haven't yet occurred</description></item>
    /// </list>
    /// </remarks>
    bool IsApplicable(int patientAge);

    /// <summary>
    /// Executes this lifecycle event, potentially generating FHIR resources and modifying scenario state.
    /// </summary>
    /// <param name="context">
    /// The scenario context containing patient demographics, current simulation time,
    /// and the collection of generated resources. The event may add encounters, observations,
    /// conditions, immunizations, or other clinical resources to this context.
    /// </param>
    /// <param name="schemaProvider">
    /// The FHIR schema provider for creating version-appropriate resources.
    /// </param>
    /// <remarks>
    /// <para>
    /// Implementations should:
    /// <list type="number">
    ///   <item><description>Use <paramref name="context"/>.CurrentTime for resource timestamps</description></item>
    ///   <item><description>Reference <paramref name="context"/>.Patient for patient links</description></item>
    ///   <item><description>Add generated resources via context.Add* methods</description></item>
    ///   <item><description>Optionally track state using context.SetAttribute for one-time events</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider);
}
