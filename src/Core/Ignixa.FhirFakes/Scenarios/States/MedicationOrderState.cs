// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a MedicationRequest resource.
/// MedicationRequests represent prescriptions and medication orders.
/// </summary>
public sealed class MedicationOrderState : ScenarioState
{
    /// <summary>
    /// Gets or sets the medication code.
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the request status ("active", "on-hold", "cancelled", "completed", "entered-in-error", "stopped", "draft", "unknown").
    /// </summary>
    public string Status { get; init; } = "active";

    /// <summary>
    /// Gets or sets the intent ("proposal", "plan", "order", "original-order", "reflex-order", "filler-order", "instance-order", "option").
    /// </summary>
    public string Intent { get; init; } = "order";

    /// <summary>
    /// Gets or sets whether this is a chronic medication (affects duration).
    /// </summary>
    public bool IsChronic { get; init; }

    /// <summary>
    /// Gets or sets the dosage instruction text.
    /// </summary>
    public string? DosageInstructions { get; init; }

    /// <summary>
    /// Gets or sets the frequency code ("once", "daily", "twice-daily", "three-times-daily", "as-needed").
    /// </summary>
    public string Frequency { get; init; } = "daily";

    /// <summary>
    /// Gets or sets the dose quantity value.
    /// </summary>
    public decimal? DoseQuantity { get; init; }

    /// <summary>
    /// Gets or sets the dose unit.
    /// </summary>
    public string? DoseUnit { get; init; }

    /// <summary>
    /// Gets or sets the duration in days for non-chronic medications.
    /// </summary>
    public int DurationDays { get; init; } = 30;

    /// <summary>
    /// Gets or sets the reason code for the prescription.
    /// </summary>
    public FhirCode? ReasonCode { get; init; }

    /// <summary>
    /// Gets or sets the attribute name of the condition this medication is treating.
    /// </summary>
    public string? ReasonConditionAttribute { get; init; }

    /// <summary>
    /// Creates a MedicationRequest resource linked to the patient and current encounter.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create MedicationRequest without a Patient. Ensure InitialState runs first.");
        }

        var medication = faker.Generate("MedicationRequest");
        var node = medication.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();
        node["status"] = Status;
        node["intent"] = Intent;

        // Set medication code
        node["medicationCodeableConcept"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = Code.System,
                    ["code"] = Code.Code,
                    ["display"] = Code.Display
                }
            },
            ["text"] = Code.Display
        };

        // Set patient reference
        node["subject"] = new JsonObject
        {
            ["reference"] = $"Patient/{context.Patient.Id}"
        };

        // Set encounter reference if available
        if (context.CurrentEncounter is not null)
        {
            node["encounter"] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }

        // Set authored date
        node["authoredOn"] = context.CurrentTime.ToString("o");

        // Set requester if practitioner is available
        if (context.CurrentPractitioner is not null)
        {
            node["requester"] = new JsonObject
            {
                ["reference"] = $"Practitioner/{context.CurrentPractitioner.Id}"
            };
        }

        // Set dosage instructions
        var dosageText = DosageInstructions ?? BuildDosageText();
        node["dosageInstruction"] = new JsonArray
        {
            new JsonObject
            {
                ["text"] = dosageText,
                ["timing"] = new JsonObject
                {
                    ["repeat"] = BuildTimingRepeat()
                }
            }
        };

        // Add dose quantity if specified
        if (DoseQuantity.HasValue)
        {
            var dosageNode = node["dosageInstruction"]!.AsArray()[0]!.AsObject();
            dosageNode["doseAndRate"] = new JsonArray
            {
                new JsonObject
                {
                    ["doseQuantity"] = new JsonObject
                    {
                        ["value"] = DoseQuantity.Value,
                        ["unit"] = DoseUnit ?? "tablet",
                        ["system"] = FhirCode.Systems.Ucum,
                        ["code"] = DoseUnit ?? "{tbl}"
                    }
                }
            };
        }

        // Set dispense request (for chronic meds, longer duration)
        var validityDays = IsChronic ? 365 : DurationDays;
        var dispenseQuantity = IsChronic ? 90 : DurationDays;
        node["dispenseRequest"] = new JsonObject
        {
            ["validityPeriod"] = new JsonObject
            {
                ["start"] = context.CurrentTime.ToString("yyyy-MM-dd"),
                ["end"] = context.CurrentTime.AddDays(validityDays).ToString("yyyy-MM-dd")
            },
            ["numberOfRepeatsAllowed"] = IsChronic ? 12 : 0,
            ["quantity"] = new JsonObject
            {
                ["value"] = dispenseQuantity,
                ["unit"] = "tablets"
            }
        };

        // Set reason if provided
        if (ReasonCode is not null)
        {
            node["reasonCode"] = new JsonArray
            {
                new JsonObject
                {
                    ["coding"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["system"] = ReasonCode.System,
                            ["code"] = ReasonCode.Code,
                            ["display"] = ReasonCode.Display
                        }
                    }
                }
            };
        }
        else if (!string.IsNullOrEmpty(ReasonConditionAttribute) &&
                 context.HasAttribute(ReasonConditionAttribute))
        {
            var conditionId = context.GetAttribute<string>(ReasonConditionAttribute);
            node["reasonReference"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference"] = $"Condition/{conditionId}"
                }
            };
        }

        // Add to context
        context.AddMedication(medication, Code.Display);
    }

    private string BuildDosageText()
    {
        var frequencyText = Frequency switch
        {
            "once" => "once",
            "daily" => "once daily",
            "twice-daily" => "twice daily",
            "three-times-daily" => "three times daily",
            "as-needed" => "as needed",
            _ => Frequency
        };

        return $"Take {DoseQuantity ?? 1} {DoseUnit ?? "tablet"} {frequencyText}";
    }

    private JsonObject BuildTimingRepeat()
    {
        return Frequency switch
        {
            "once" => new JsonObject { ["count"] = 1 },
            "daily" => new JsonObject { ["frequency"] = 1, ["period"] = 1, ["periodUnit"] = "d" },
            "twice-daily" => new JsonObject { ["frequency"] = 2, ["period"] = 1, ["periodUnit"] = "d" },
            "three-times-daily" => new JsonObject { ["frequency"] = 3, ["period"] = 1, ["periodUnit"] = "d" },
            "as-needed" => new JsonObject { ["asNeededBoolean"] = true },
            _ => new JsonObject { ["frequency"] = 1, ["period"] = 1, ["periodUnit"] = "d" }
        };
    }

    /// <summary>
    /// Creates a Metformin 500mg prescription for diabetes.
    /// </summary>
    public static MedicationOrderState Metformin500mg() => new()
    {
        Code = FhirCode.Medications.Metformin500mg,
        IsChronic = true,
        Frequency = "twice-daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.DiabetesType2
    };

    /// <summary>
    /// Creates a Metformin 1000mg prescription for diabetes.
    /// </summary>
    public static MedicationOrderState Metformin1000mg() => new()
    {
        Code = FhirCode.Medications.Metformin1000mg,
        IsChronic = true,
        Frequency = "twice-daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.DiabetesType2
    };

    /// <summary>
    /// Creates a Lisinopril 10mg prescription for hypertension.
    /// </summary>
    public static MedicationOrderState Lisinopril10mg() => new()
    {
        Code = FhirCode.Medications.Lisinopril10mg,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Hypertension
    };

    /// <summary>
    /// Creates a Lisinopril 20mg prescription for hypertension.
    /// </summary>
    public static MedicationOrderState Lisinopril20mg() => new()
    {
        Code = FhirCode.Medications.Lisinopril20mg,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Hypertension
    };

    /// <summary>
    /// Creates an Amlodipine 5mg prescription for hypertension.
    /// </summary>
    public static MedicationOrderState Amlodipine5mg() => new()
    {
        Code = FhirCode.Medications.Amlodipine5mg,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Hypertension
    };

    /// <summary>
    /// Creates an Albuterol prescription for asthma.
    /// </summary>
    public static MedicationOrderState Albuterol() => new()
    {
        Code = FhirCode.Medications.Albuterol,
        IsChronic = true,
        Frequency = "as-needed",
        DoseQuantity = 2,
        DoseUnit = "puffs",
        DosageInstructions = "Inhale 2 puffs as needed for shortness of breath",
        ReasonCode = FhirCode.Conditions.Asthma
    };

    /// <summary>
    /// Creates a prenatal vitamins prescription.
    /// </summary>
    public static MedicationOrderState PrenatalVitamins() => new()
    {
        Code = FhirCode.Medications.PrenatalVitamins,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Pregnancy
    };

    /// <summary>
    /// Creates a folic acid prescription for pregnancy.
    /// </summary>
    public static MedicationOrderState FolicAcid() => new()
    {
        Code = FhirCode.Medications.FolicAcid,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Pregnancy
    };

    /// <summary>
    /// Creates an Atorvastatin 20mg prescription for hyperlipidemia.
    /// </summary>
    public static MedicationOrderState Atorvastatin20mg() => new()
    {
        Code = FhirCode.Medications.Atorvastatin20mg,
        IsChronic = true,
        Frequency = "daily",
        DoseQuantity = 1,
        DoseUnit = "tablet",
        ReasonCode = FhirCode.Conditions.Hyperlipidemia
    };

    /// <summary>
    /// Creates a Fluticasone Propionate inhaler prescription for asthma (controller medication).
    /// </summary>
    public static MedicationOrderState FlucticasonePropionate() => new()
    {
        Code = FhirCode.Medications.FlucticasonePropionate,
        IsChronic = true,
        Frequency = "twice-daily",
        DoseQuantity = 2,
        DoseUnit = "puffs",
        DosageInstructions = "Inhale 2 puffs twice daily for asthma control",
        ReasonCode = FhirCode.Conditions.Asthma
    };

    /// <summary>
    /// Creates a Vitamin D 50,000 IU prescription for vitamin D deficiency.
    /// </summary>
    public static MedicationOrderState VitaminD50000IU() => new()
    {
        Code = FhirCode.Medications.VitaminD50000IU,
        IsChronic = false,
        Frequency = "weekly",
        DoseQuantity = 1,
        DoseUnit = "capsule",
        DurationDays = 56,  // 8 weeks
        ReasonCode = FhirCode.Conditions.VitaminDDeficiency
    };
}
