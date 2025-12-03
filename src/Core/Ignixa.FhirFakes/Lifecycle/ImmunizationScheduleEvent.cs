// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Lifecycle event that generates immunizations according to CDC vaccination schedules.
/// Supports both childhood and adult immunization recommendations.
/// </summary>
/// <remarks>
/// <para>
/// The immunization schedule is based on CDC Advisory Committee on Immunization Practices (ACIP)
/// recommendations and includes vaccines for:
/// </para>
/// <para>
/// Childhood vaccinations (birth to 18 years):
/// <list type="bullet">
///   <item><description>Hepatitis B: Birth, 1-2 months, 6-18 months</description></item>
///   <item><description>DTaP: 2, 4, 6, 15-18 months, 4-6 years</description></item>
///   <item><description>Polio (IPV): 2, 4, 6-18 months, 4-6 years</description></item>
///   <item><description>MMR: 12-15 months, 4-6 years</description></item>
///   <item><description>Varicella: 12-15 months, 4-6 years</description></item>
///   <item><description>Influenza: Annual starting at 6 months</description></item>
/// </list>
/// </para>
/// <para>
/// Adult vaccinations (19+ years):
/// <list type="bullet">
///   <item><description>Influenza: Annual</description></item>
///   <item><description>Tdap/Td: Every 10 years</description></item>
///   <item><description>Shingles (Zoster): Age 50+</description></item>
///   <item><description>Pneumococcal: Age 65+</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class ImmunizationScheduleEvent : ILifecycleEvent
{
    /// <summary>
    /// Simplified immunization schedule mapping age (in years) to vaccines.
    /// Note: Ages 0 represents birth year, vaccines scheduled for months are consolidated to year 0 or 1.
    /// </summary>
    private static readonly Dictionary<int, ImmunizationInfo[]> ImmunizationsByAge = new()
    {
        // Birth year - HepB birth dose
        [0] = [new("Hepatitis B Vaccine", "HepB", 1, "Hepatitis B series")],

        // Age 1 - Multiple childhood vaccines (consolidating 2-18 month doses)
        [1] = [
            new("Hepatitis B Vaccine", "HepB", 2, "Hepatitis B series"),
            new("DTaP Vaccine", "DTaP", 1, "DTaP series"),
            new("Polio Vaccine (IPV)", "IPV", 1, "Polio series"),
            new("Pneumococcal Conjugate Vaccine", "PCV13", 1, "Pneumococcal series"),
            new("Rotavirus Vaccine", "RV", 1, "Rotavirus series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Age 2 - Second doses
        [2] = [
            new("Hepatitis B Vaccine", "HepB", 3, "Hepatitis B series"),
            new("DTaP Vaccine", "DTaP", 2, "DTaP series"),
            new("Polio Vaccine (IPV)", "IPV", 2, "Polio series"),
            new("MMR Vaccine", "MMR", 1, "MMR series"),
            new("Varicella Vaccine", "VAR", 1, "Varicella series"),
            new("Hepatitis A Vaccine", "HepA", 1, "Hepatitis A series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Ages 3-4 - Annual flu, some catch-ups
        [3] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],

        // Age 4-6 - Kindergarten booster doses
        [4] = [
            new("DTaP Vaccine", "DTaP", 5, "DTaP series"),
            new("Polio Vaccine (IPV)", "IPV", 4, "Polio series"),
            new("MMR Vaccine", "MMR", 2, "MMR series"),
            new("Varicella Vaccine", "VAR", 2, "Varicella series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Ages 5-10 - Annual flu
        [5] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [6] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [7] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [8] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [9] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [10] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],

        // Age 11-12 - Adolescent vaccines
        [11] = [
            new("Tdap Vaccine", "Tdap", 1, "Tetanus/Diphtheria/Pertussis booster"),
            new("HPV Vaccine", "HPV", 1, "HPV series"),
            new("Meningococcal Conjugate Vaccine", "MenACWY", 1, "Meningococcal series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        [12] = [
            new("HPV Vaccine", "HPV", 2, "HPV series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Ages 13-15 - Continued flu, HPV completion
        [13] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [14] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [15] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],

        // Age 16 - MenACWY booster
        [16] = [
            new("Meningococcal Conjugate Vaccine", "MenACWY", 2, "Meningococcal series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        [17] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [18] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],

        // Adult schedule - Td booster every 10 years (19, 29, 39, 49, 59, 69, 79...)
        [19] = [new("Influenza Vaccine", "Flu", 1, "Annual influenza")],
        [29] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],
        [39] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],
        [49] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Age 50+ - Shingles vaccine
        [50] = [
            new("Zoster Vaccine (Shingrix)", "Zoster", 1, "Shingles prevention series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],
        [51] = [
            new("Zoster Vaccine (Shingrix)", "Zoster", 2, "Shingles prevention series"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        [59] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        // Age 65+ - Pneumococcal vaccines
        [65] = [
            new("Pneumococcal Conjugate Vaccine", "PCV20", 1, "Pneumococcal adult dose"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        [69] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ],

        [79] = [
            new("Td Vaccine", "Td", 1, "Tetanus/Diphtheria booster"),
            new("Influenza Vaccine", "Flu", 1, "Annual influenza")
        ]
    };

    /// <inheritdoc />
    public string Name => "ImmunizationSchedule";

    /// <inheritdoc />
    public bool IsApplicable(int patientAge)
    {
        // Check explicit schedule first
        if (ImmunizationsByAge.ContainsKey(patientAge))
        {
            return true;
        }

        // Adults 19+ get annual flu even if not explicitly listed
        return patientAge >= 19;
    }

    /// <inheritdoc />
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var builder = new ScenarioBuilder(schemaProvider);
        var currentAge = context.CurrentAge;

        // Get scheduled immunizations for this age
        if (ImmunizationsByAge.TryGetValue(currentAge, out var immunizations))
        {
            foreach (var imm in immunizations)
            {
                // Use the predefined Immunization codes where available, or create generic ones
                var vaccineCode = GetVaccineCode(imm.Code);
                builder.AddImmunization(vaccineCode, imm.DoseNumber, imm.Series);
            }
        }
        else if (currentAge >= 19)
        {
            // Annual flu for adults not explicitly scheduled
            builder.AddInfluenzaVaccine();
        }

        // Execute the scenario states against our context
        var states = builder.GetStates();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        foreach (var state in states)
        {
            state.Execute(context, faker);
        }
    }

    /// <summary>
    /// Maps vaccine abbreviations to FhirCode instances.
    /// </summary>
    private static FhirCode GetVaccineCode(string vaccineCode)
    {
        // Map to predefined codes where available
        return vaccineCode switch
        {
            "Flu" => Immunizations.Influenza,
            "DTaP" => Immunizations.DTaP,
            "Tdap" => Immunizations.Tdap,
            "Td" => Immunizations.TdAdult,
            "MMR" => Immunizations.MMR,
            "IPV" => Immunizations.IPV,
            "VAR" => Immunizations.Varicella,
            "HepB" => Immunizations.HepB,
            "HepA" => Immunizations.HepA,
            "HPV" => Immunizations.HPV,
            "Zoster" => Immunizations.Zoster,
            "PCV13" => Immunizations.PCV13,
            "PCV20" => Immunizations.PCV20,
            "MenACWY" => Immunizations.MeningococcalMCV4P,
            "RV" => Immunizations.RotavirusMonovalent,
            _ => CreateVaccineCode(vaccineCode, vaccineCode, FhirCode.Systems.Cvx)
        };
    }

    /// <summary>
    /// Creates a vaccine code for vaccines not predefined in the Immunizations class.
    /// </summary>
    /// <param name="code">The CVX vaccine code.</param>
    /// <param name="display">The display name for the vaccine.</param>
    /// <param name="system">The code system URI (typically CVX).</param>
    /// <returns>A new FhirCode representing the vaccine.</returns>
    private static FhirCode CreateVaccineCode(string code, string display, string system)
    {
        return new FhirCode(system, code, display);
    }

    /// <summary>
    /// Information about an immunization in the schedule.
    /// </summary>
    private sealed record ImmunizationInfo(string Display, string Code, int DoseNumber, string Series);
}
