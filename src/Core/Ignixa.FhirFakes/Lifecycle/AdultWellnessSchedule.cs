// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Abstractions;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Lifecycle;

/// <summary>
/// Lifecycle event that generates annual adult wellness visits starting at age 18.
/// Follows USPSTF (U.S. Preventive Services Task Force) recommendations for adult health maintenance.
/// </summary>
/// <remarks>
/// <para>
/// Adult wellness visits are recommended annually starting at age 18.
/// Each visit generates an Encounter resource and may include age-appropriate screenings
/// and observations such as blood pressure, cholesterol, and BMI.
/// </para>
/// <para>
/// The visit may also trigger age-specific preventive care recommendations:
/// <list type="bullet">
///   <item><description>Ages 18-39: Annual wellness, immunization updates</description></item>
///   <item><description>Ages 40-49: Diabetes screening, cardiovascular risk assessment</description></item>
///   <item><description>Ages 50-64: Colorectal cancer screening, mammography (as applicable)</description></item>
///   <item><description>Ages 65+: Medicare Annual Wellness Visit, bone density screening</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class AdultWellnessSchedule : ILifecycleEvent
{
    /// <summary>
    /// The minimum age for adult wellness visits.
    /// </summary>
    private const int MinimumAdultAge = 18;

    /// <inheritdoc />
    public string Name => "AdultWellnessSchedule";

    /// <inheritdoc />
    public bool IsApplicable(int patientAge) => patientAge >= MinimumAdultAge;

    /// <inheritdoc />
    public void Execute(ScenarioContext context, IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        // Create a wellness visit using the existing scenario infrastructure
        var builder = new ScenarioBuilder(schemaProvider);

        // Determine visit description based on age
        var visitDescription = context.CurrentAge switch
        {
            >= 65 => $"Medicare Annual Wellness Visit - age {context.CurrentAge}",
            >= 50 => $"Adult wellness visit with preventive screenings - age {context.CurrentAge}",
            >= 40 => $"Adult wellness visit - age {context.CurrentAge}",
            _ => $"Young adult wellness visit - age {context.CurrentAge}"
        };

        // Add the wellness visit encounter and age-appropriate vital signs (height, weight, BMI, BP)
        builder
            .AddWellnessVisit(visitDescription)
            .AddSubScenario(
                CommonScenarios.RecordAgeAppropriateVitalSigns(context.CurrentAge, context.GetAttribute<string>("gender")),
                "Record Vitals");

        // Age-appropriate lab panels (USPSTF):
        //  - 40+: comprehensive metabolic panel (diabetes / kidney function screening)
        //  - 45+: lipid panel (cardiovascular risk)
        if (context.CurrentAge >= 40)
        {
            builder.AddSubScenario(CommonScenarios.BasicMetabolicPanel(), "Metabolic Panel");
        }

        if (context.CurrentAge >= 45)
        {
            builder.AddSubScenario(CommonScenarios.LipidPanel(), "Lipid Panel");
        }

        // Execute the scenario states against our context
        var states = builder.GetStates();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider);

        foreach (var state in states)
        {
            state.Execute(context, faker);
        }
    }
}
