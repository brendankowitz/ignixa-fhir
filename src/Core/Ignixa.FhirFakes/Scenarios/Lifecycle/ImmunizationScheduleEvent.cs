// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Scenarios.Lifecycle;

/// <summary>
/// Implements the CDC-recommended immunization schedule for childhood vaccines.
/// Administers vaccines at age-appropriate intervals following the ACIP (Advisory Committee on Immunization Practices) guidelines.
/// </summary>
/// <remarks>
/// <para>
/// This lifecycle event implements a subset of the CDC immunization schedule for common childhood vaccines.
/// The full CDC schedule includes many more vaccines and complex dose timing (e.g., 2 months, 4 months, 6 months).
/// This implementation uses a simplified year-based schedule for demonstration purposes.
/// </para>
/// <para>
/// Current implementation includes:
/// - Hepatitis B (HepB): Birth (age 0), 1-2 months (age 0), 6-18 months (age 1)
/// - DTaP (Diphtheria, Tetanus, Pertussis): Ages 0, 1, 1, 2, 5 (simplified from 2mo, 4mo, 6mo, 15-18mo, 4-6yr)
/// - MMR (Measles, Mumps, Rubella): Ages 1, 5 (12-15 months, 4-6 years)
/// - Varicella (Chickenpox): Ages 1, 5 (12-15 months, 4-6 years)
/// </para>
/// <para>
/// Future enhancements (mentioned in ADR but deferred):
/// - Data-driven configuration from immunization_schedule.json
/// - Month-level precision for infant vaccines (2mo, 4mo, 6mo, 12mo, 15mo, 18mo)
/// - Additional vaccines (Hib, PCV13, IPV, Rotavirus, Hepatitis A, HPV, Meningococcal)
/// - Catch-up schedules for delayed immunizations
/// - Adult booster schedules (Tdap, influenza, pneumococcal, shingles)
/// </para>
/// <para>
/// Clinical rationale:
/// - Timely immunization prevents serious childhood diseases (measles, pertussis, tetanus)
/// - Herd immunity protects vulnerable populations (infants, immunocompromised)
/// - Vaccine-preventable diseases cause significant morbidity and mortality
/// </para>
/// </remarks>
public sealed class ImmunizationScheduleEvent : ILifecycleEvent
{
    // Track which vaccines have been administered to prevent duplicates
    private readonly HashSet<string> _administeredVaccines = [];

    // Define the immunization schedule with vaccine doses at specific ages
    // Format: (Age in years, Vaccine code, Dose number, Series name)
    private readonly List<ImmunizationScheduleEntry> _schedule =
    [
        // Hepatitis B series: birth, 1-2 months, 6 months
        new(Age: 0, Immunizations.HepB, DoseNumber: 1, Series: "Hepatitis B Series"),
        new(Age: 0, Immunizations.HepB, DoseNumber: 2, Series: "Hepatitis B Series"), // 1-2 months approximated as age 0
        new(Age: 1, Immunizations.HepB, DoseNumber: 3, Series: "Hepatitis B Series"),

        // DTaP series: 2mo, 4mo, 6mo, 15-18mo, 4-6yr (simplified to ages 0, 0, 1, 2, 5)
        new(Age: 0, Immunizations.DTaP, DoseNumber: 1, Series: "Childhood Immunization Series"),
        new(Age: 0, Immunizations.DTaP, DoseNumber: 2, Series: "Childhood Immunization Series"),
        new(Age: 1, Immunizations.DTaP, DoseNumber: 3, Series: "Childhood Immunization Series"),
        new(Age: 2, Immunizations.DTaP, DoseNumber: 4, Series: "Childhood Immunization Series"),
        new(Age: 5, Immunizations.DTaP, DoseNumber: 5, Series: "Childhood Immunization Series"),

        // MMR series: 12-15 months, 4-6 years
        new(Age: 1, Immunizations.MMR, DoseNumber: 1, Series: "MMR Series"),
        new(Age: 5, Immunizations.MMR, DoseNumber: 2, Series: "MMR Series"),

        // Varicella series: 12-15 months, 4-6 years
        new(Age: 1, Immunizations.Varicella, DoseNumber: 1, Series: "Varicella Series"),
        new(Age: 5, Immunizations.Varicella, DoseNumber: 2, Series: "Varicella Series"),
    ];

    /// <summary>
    /// Determines if any immunizations are scheduled at the specified age.
    /// </summary>
    /// <param name="patientAge">The patient's current age in years.</param>
    /// <returns><c>true</c> if one or more immunizations are scheduled; otherwise, <c>false</c>.</returns>
    public bool IsApplicable(int patientAge)
    {
        return _schedule.Any(entry => entry.Age == patientAge);
    }

    /// <summary>
    /// Administers all scheduled immunizations for the patient's current age.
    /// </summary>
    /// <param name="context">The scenario context for resource generation.</param>
    /// <param name="schemaProvider">The FHIR schema provider.</param>
    /// <remarks>
    /// <para>
    /// Immunizations are administered during the current encounter context if available.
    /// If no encounter exists, the immunizations are still recorded but without an encounter reference.
    /// </para>
    /// <para>
    /// This implementation prevents duplicate vaccines by tracking administered vaccine keys
    /// (combination of vaccine code and dose number). In a production system, you might want
    /// more sophisticated deduplication logic based on time intervals.
    /// </para>
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Used for test data generation only")]
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var patientAge = context.CurrentAge;
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        // Get all scheduled vaccines for this age
        var scheduledVaccines = _schedule.Where(entry => entry.Age == patientAge);

        foreach (var scheduleEntry in scheduledVaccines)
        {
            // Create a unique key to prevent duplicate vaccines
            var vaccineKey = $"{scheduleEntry.VaccineCode.Code}-dose{scheduleEntry.DoseNumber}";

            if (_administeredVaccines.Contains(vaccineKey))
            {
                // Already administered, skip
                continue;
            }

            // Create the immunization state
            var immunizationState = new ImmunizationState
            {
                Name = $"{scheduleEntry.VaccineCode.Display} - Dose {scheduleEntry.DoseNumber}",
                Code = scheduleEntry.VaccineCode,
                DoseNumber = scheduleEntry.DoseNumber,
                Series = scheduleEntry.Series,
                SeriesDosesRecommended = GetSeriesDosesCount(scheduleEntry.VaccineCode)
            };

            // Execute the immunization state
            immunizationState.Execute(context, faker);

            // Mark as administered
            _administeredVaccines.Add(vaccineKey);
        }
    }

    /// <summary>
    /// Gets the total number of doses recommended for a vaccine series.
    /// </summary>
    private static int GetSeriesDosesCount(FhirCode vaccineCode)
    {
        // Map vaccine codes to their series dose counts
        if (vaccineCode == Immunizations.HepB) return 3;
        if (vaccineCode == Immunizations.DTaP) return 5;
        if (vaccineCode == Immunizations.MMR) return 2;
        if (vaccineCode == Immunizations.Varicella) return 2;

        // Default to 1 for unknown vaccines
        return 1;
    }

    /// <summary>
    /// Represents a single immunization schedule entry.
    /// </summary>
    /// <param name="Age">The patient age (in years) when this vaccine should be administered.</param>
    /// <param name="VaccineCode">The vaccine code (CVX code from CDC).</param>
    /// <param name="DoseNumber">The dose number in the vaccine series.</param>
    /// <param name="Series">The series name for tracking purposes.</param>
    private sealed record ImmunizationScheduleEntry(
        int Age,
        FhirCode VaccineCode,
        int DoseNumber,
        string Series);
}
