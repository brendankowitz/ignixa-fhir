// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that conditionally adds a medication based on disease severity.
/// Used for modeling medication escalation in disease management.
/// </summary>
internal sealed class ConditionalMedicationEscalationState : ScenarioState
{
    /// <summary>
    /// Gets or sets the severity attribute to check.
    /// </summary>
    public required string SeverityAttribute { get; init; }

    /// <summary>
    /// Gets or sets the threshold severity for escalation.
    /// </summary>
    public required int ThresholdSeverity { get; init; }

    /// <summary>
    /// Gets or sets the medication to add if threshold is met.
    /// </summary>
    public required MedicationOrderState EscalateMedication { get; init; }

    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        var currentSeverity = context.GetAttribute<int>(SeverityAttribute, 1);
        if (currentSeverity >= ThresholdSeverity)
        {
            EscalateMedication.Execute(context, faker);
        }
    }
}
