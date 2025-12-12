// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios.Codes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that creates a Condition resource representing disease onset.
/// Conditions are linked to the patient and optionally to an encounter.
/// </summary>
public sealed class ConditionOnsetState : ScenarioState
{
    /// <summary>
    /// Gets or sets the condition code.
    /// </summary>
    public required FhirCode Code { get; init; }

    /// <summary>
    /// Gets or sets the clinical status ("active", "recurrence", "relapse", "inactive", "remission", "resolved").
    /// </summary>
    public string ClinicalStatus { get; init; } = "active";

    /// <summary>
    /// Gets or sets the verification status ("unconfirmed", "provisional", "differential", "confirmed", "refuted", "entered-in-error").
    /// </summary>
    public string VerificationStatus { get; init; } = "confirmed";

    /// <summary>
    /// Gets or sets the category ("problem-list-item", "encounter-diagnosis").
    /// </summary>
    public string Category { get; init; } = "encounter-diagnosis";

    /// <summary>
    /// Gets or sets the severity level (1-5). Used to set initial disease severity attribute.
    /// </summary>
    public int Severity { get; init; } = 1;

    /// <summary>
    /// Gets or sets the attribute name to store this condition for later reference.
    /// </summary>
    public string? AssignToAttribute { get; init; }

    /// <summary>
    /// Creates a Condition resource linked to the patient and current encounter.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        if (context.Patient is null)
        {
            throw new InvalidOperationException("Cannot create Condition without a Patient. Ensure InitialState runs first.");
        }

        var condition = faker.Generate("Condition");
        var node = condition.MutableNode;

        // Set required fields
        node["id"] = Guid.NewGuid().ToString();

        // Set clinical status
        node["clinicalStatus"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/condition-clinical",
                    ["code"] = ClinicalStatus
                }
            }
        };

        // Set verification status
        node["verificationStatus"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/condition-ver-status",
                    ["code"] = VerificationStatus
                }
            }
        };

        // Set category
        node["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = "http://terminology.hl7.org/CodeSystem/condition-category",
                        ["code"] = Category
                    }
                }
            }
        };

        // Set condition code
        node["code"] = new JsonObject
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

        // Set encounter reference if available (STU3 uses "context" instead of "encounter")
        var encounterField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Condition",
            "encounter");

        if (context.CurrentEncounter is not null)
        {
            node[encounterField] = new JsonObject
            {
                ["reference"] = $"Encounter/{context.CurrentEncounter.Id}"
            };
        }
        else
        {
            // Clear any faker-generated encounter reference
            node.Remove(encounterField);
            // Also clear STU3 "context" field if present
            node.Remove("context");
        }

        // Remove any existing choice element variants to avoid "Choice element 'onset[x]' can only have one type variant" error
        RemoveChoiceConflicts(node, "onset");

        // Set onset date using version-appropriate field name (R4+ normative is "onsetDateTime")
        var onsetField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Condition",
            "onsetDateTime");
        node[onsetField] = context.CurrentTime.ToString("o");

        // Set recorded date using version-appropriate field name (STU3 uses "assertedDate" instead of "recordedDate")
        var recordedDateField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Condition",
            "recordedDate");
        node[recordedDateField] = context.CurrentTime.ToString("o");

        // Add to context
        context.AddCondition(condition, Code.Display);

        // NEW: Register with StateId for cross-references
        context.RegisterStateResource(StateId, condition);

        // Set severity attribute if provided
        if (!string.IsNullOrEmpty(AssignToAttribute))
        {
            context.SetAttribute(AssignToAttribute, condition.Id);
            context.SetAttribute($"{AssignToAttribute}_severity", Severity);
        }
    }

    /// <summary>
    /// Creates a condition onset state for Type 2 Diabetes.
    /// </summary>
    public static ConditionOnsetState DiabetesType2(int severity = 1) => new()
    {
        Code = FhirCode.Conditions.DiabetesType2,
        Severity = severity,
        AssignToAttribute = "diabetes_condition"
    };

    /// <summary>
    /// Creates a condition onset state for Hypertension.
    /// </summary>
    public static ConditionOnsetState Hypertension(int severity = 1) => new()
    {
        Code = FhirCode.Conditions.Hypertension,
        Severity = severity,
        AssignToAttribute = "hypertension_condition"
    };

    /// <summary>
    /// Creates a condition onset state for Pregnancy.
    /// </summary>
    public static ConditionOnsetState Pregnancy() => new()
    {
        Code = FhirCode.Conditions.PregnancyNormal,
        Category = "problem-list-item",
        AssignToAttribute = "pregnancy_condition"
    };

    /// <summary>
    /// Creates a condition onset state for Asthma.
    /// </summary>
    public static ConditionOnsetState Asthma(int severity = 1) => new()
    {
        Code = FhirCode.Conditions.Asthma,
        Severity = severity,
        AssignToAttribute = "asthma_condition"
    };
}
