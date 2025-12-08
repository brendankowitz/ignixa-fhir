// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.Codes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// State that ends/resolves a Condition resource.
/// Updates the condition's clinical status to "resolved" or "inactive" and sets abatement date.
/// </summary>
public sealed class ConditionEndState : ScenarioState
{
    /// <summary>
    /// Gets or sets the attribute name where the condition ID is stored.
    /// </summary>
    public string? AttributeName { get; init; }

    /// <summary>
    /// Gets or sets the condition code to search for when ending.
    /// </summary>
    public FhirCode? ConditionCode { get; init; }

    /// <summary>
    /// Gets or sets the clinical status to set.
    /// Defaults to "resolved".
    /// </summary>
    public string ClinicalStatus { get; init; } = ConditionClinicalStatus.Resolved;

    /// <summary>
    /// Gets or sets whether to end the most recent condition matching the criteria.
    /// Defaults to true.
    /// </summary>
    public bool EndMostRecent { get; init; } = true;

    /// <summary>
    /// Ends the condition by updating its clinical status and setting abatement date.
    /// </summary>
    public override void Execute(ScenarioContext context, SchemaBasedFhirResourceFaker faker)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(faker);

        ResourceJsonNode? condition = null;

        // Find condition by attribute reference
        if (!string.IsNullOrEmpty(AttributeName))
        {
            var conditionId = context.GetAttribute<string>(AttributeName);
            if (!string.IsNullOrEmpty(conditionId))
            {
                condition = context.Conditions.FirstOrDefault(c => c.Id == conditionId);
            }
        }
        // Find condition by code
        else if (ConditionCode is not null)
        {
            var matchingConditions = context.Conditions.Where(c =>
            {
                var codeNode = c.MutableNode["code"]?["coding"];
                if (codeNode is JsonArray codingArray)
                {
                    return codingArray.Any(coding =>
                    {
                        var codeValue = coding?["code"]?.GetValue<string>();
                        return codeValue == ConditionCode.Code;
                    });
                }
                return false;
            });

            condition = EndMostRecent
                ? matchingConditions.LastOrDefault()
                : matchingConditions.FirstOrDefault();
        }

        if (condition is null)
        {
            throw new InvalidOperationException(
                $"Cannot end condition. No condition found with attribute '{AttributeName}' or code '{ConditionCode?.Code}'.");
        }

        // Update clinical status
        condition.MutableNode["clinicalStatus"] = new JsonObject
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

        // Set abatement date using version-appropriate field name (R4+ normative is "abatementDateTime")
        var abatementField = VersionFieldOverrides.GetFieldName(
            faker.SchemaProvider.Version,
            "Condition",
            "abatementDateTime");
        condition.MutableNode[abatementField] = context.CurrentTime.ToString("o");

        // Get condition display name for logging
        var displayName = AttributeName ?? ConditionCode?.Display ?? "Unknown";

        // Record condition end event in timeline
        context.RecordConditionEnd(condition.Id, $"End: {displayName}");
    }

    /// <summary>
    /// Creates a condition end state for ending a condition by attribute reference.
    /// </summary>
    /// <param name="attributeName">The attribute name where the condition ID is stored.</param>
    /// <param name="clinicalStatus">The clinical status to set (default: "resolved").</param>
    public static ConditionEndState ByAttribute(string attributeName, string? clinicalStatus = null) => new()
    {
        Name = $"EndCondition_{attributeName}",
        AttributeName = attributeName,
        ClinicalStatus = clinicalStatus ?? ConditionClinicalStatus.Resolved
    };

    /// <summary>
    /// Creates a condition end state for ending a condition by code.
    /// </summary>
    /// <param name="code">The condition code to search for.</param>
    /// <param name="clinicalStatus">The clinical status to set (default: "resolved").</param>
    public static ConditionEndState ByCode(FhirCode code, string? clinicalStatus = null) => new()
    {
        Name = $"EndCondition_{code.Display}",
        ConditionCode = code,
        ClinicalStatus = clinicalStatus ?? ConditionClinicalStatus.Resolved
    };
}
