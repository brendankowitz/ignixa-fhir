// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates an Encounter resource.
/// Encounters represent clinical visits and provide context for other resources.
/// </summary>
public sealed class EncounterState : ScenarioState
{
    /// <summary>
    /// Gets or sets the encounter type code.
    /// </summary>
    public FhirCode EncounterType { get; init; } = FhirCode.EncounterTypes.Ambulatory;

    /// <summary>
    /// Gets or sets the encounter status.
    /// Cross-version compatible values: "planned", "in-progress", "cancelled", "entered-in-error", "unknown".
    /// Note: "finished" (STU3-R4B) was renamed to "completed" in R5. Use "in-progress" for cross-version compatibility.
    /// </summary>
    public string Status { get; init; } = "in-progress";

    /// <summary>
    /// Gets or sets the encounter class ("AMB", "EMER", "IMP", etc.).
    /// </summary>
    public string EncounterClass { get; init; } = "AMB";

    /// <summary>
    /// Gets or sets the reason for the encounter (human-readable description).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets or sets the encounter duration in minutes.
    /// </summary>
    public int DurationMinutes { get; init; } = 30;

    /// <summary>
    /// Creates an Encounter resource linked to the patient.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Encounter without a Patient. Ensure InitialState runs first.");
        }

        var encounter = faker.Generate("Encounter");
        var node = encounter.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;

        // Set class
        node["class"] = new JsonObject
        {
            ["system"] = FhirCode.Systems.EncounterType,
            ["code"] = EncounterClass,
            ["display"] = EncounterType.Display
        };

        // Set type
        node["type"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = EncounterType.System,
                        ["code"] = EncounterType.Code,
                        ["display"] = EncounterType.Display
                    }
                },
                ["text"] = EncounterType.Display
            }
        };

        // Set patient reference
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set period (R5 uses "actualPeriod" instead of "period")
        var startTime = context.CurrentTime;
        var endTime = startTime.AddMinutes(DurationMinutes);
        var periodField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Encounter",
            "period");
        node[periodField] = new JsonObject
        {
            ["start"] = startTime.ToString("o"),
            ["end"] = endTime.ToString("o")
        };

        // Set reason if provided (version-aware field name: R4/R4B uses "reasonCode", STU3 uses "reason", R5 uses backbone element)
        if (!string.IsNullOrEmpty(Reason))
        {
            var reasonField = VersionFieldOverrides.GetFieldName(
                faker.SchemaProvider.Version,
                "Encounter",
                "reasonCode");

            // Skip R5 as it requires a different structure (Encounter.Reason backbone element)
            if (!string.IsNullOrEmpty(reasonField))
            {
                node[reasonField] = new JsonArray
                {
                    new JsonObject
                    {
                        ["text"] = Reason
                    }
                };
            }
        }

        // Set participant if practitioner is available
        if (context.CurrentPractitioner is not null)
        {
            node["participant"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["coding"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["system"] = "http://terminology.hl7.org/CodeSystem/v3-ParticipationType",
                                    ["code"] = "PPRF",
                                    ["display"] = "Primary Performer"
                                }
                            }
                        }
                    },
                    ["individual"] = new JsonObject
                    {
                        ["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}"
                    }
                }
            };
        }

        // Set serviceProvider if organization is available
        if (context.CurrentOrganization is not null)
        {
            node["serviceProvider"] = new JsonObject
            {
                ["reference"] = $"Organization/{context.CurrentOrganization.Id}"
            };
        }

        // Add to context
        var description = Reason ?? EncounterType.Display;
        context.AddEncounter(encounter, description);

        // NEW: Register with StateId for cross-references
        context.RegisterStateResource(StateId, encounter);
    }

    /// <summary>
    /// Creates an ambulatory encounter state.
    /// </summary>
    public static EncounterState Ambulatory(string? reason = null) => new()
    {
        EncounterType = FhirCode.EncounterTypes.Ambulatory,
        EncounterClass = "AMB",
        Reason = reason
    };

    /// <summary>
    /// Creates an emergency encounter state.
    /// </summary>
    public static EncounterState Emergency(string? reason = null) => new()
    {
        EncounterType = FhirCode.EncounterTypes.Emergency,
        EncounterClass = "EMER",
        Reason = reason
    };

    /// <summary>
    /// Creates an inpatient encounter state.
    /// </summary>
    public static EncounterState Inpatient(string? reason = null, int durationMinutes = 1440) => new()
    {
        EncounterType = FhirCode.EncounterTypes.Inpatient,
        EncounterClass = "IMP",
        Reason = reason,
        DurationMinutes = durationMinutes
    };

    /// <summary>
    /// Creates a wellness visit encounter state.
    /// </summary>
    public static EncounterState Wellness(string? reason = null) => new()
    {
        EncounterType = FhirCode.EncounterTypes.Wellness,
        EncounterClass = "AMB",
        Reason = reason ?? "Routine wellness visit"
    };
}
