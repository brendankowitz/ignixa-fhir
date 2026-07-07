// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Enforces the closed-world, terminology-independent shape rules for
/// <c>ValueSet.compose.include.filter</c>. A filter that tests a standard boolean concept-property
/// (<c>notSelectable</c>, <c>inactive</c>) with the equality operator must supply a boolean value —
/// the reference validator rejects any other: "The value for a filter based on property
/// 'notSelectable' must be either 'true' or 'false', not '1'." Only the FHIR-defined boolean
/// concept-properties are consulted, so no code-system knowledge is required.
/// </summary>
public sealed class ValueSetFilterCheck : IValidationCheck, ISingletonCheck
{
    // FHIR-standard concept-properties (http://hl7.org/fhir/concept-properties) whose type is
    // boolean. Their filter values are constrained regardless of the target code system.
    private static readonly FrozenSet<string> BooleanProperties =
        new[] { "notSelectable", "inactive" }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.Meta<JsonNode>() is not JsonObject root
            || root["compose"] is not JsonObject compose)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        CheckGroup(compose["include"] as JsonArray, "include", issues);
        CheckGroup(compose["exclude"] as JsonArray, "exclude", issues);

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }

    private static void CheckGroup(JsonArray? group, string groupName, List<ValidationIssue> issues)
    {
        if (group is null)
        {
            return;
        }

        for (var i = 0; i < group.Count; i++)
        {
            if (group[i] is JsonObject entry && entry["filter"] is JsonArray filters)
            {
                CheckFilters(filters, $"ValueSet.compose.{groupName}[{i}].filter", issues);
            }
        }
    }

    private static void CheckFilters(JsonArray filters, string path, List<ValidationIssue> issues)
    {
        for (var i = 0; i < filters.Count; i++)
        {
            if (filters[i] is not JsonObject filter
                || (filter["property"] as JsonValue)?.ToString() is not { } property
                || !BooleanProperties.Contains(property)
                || (filter["op"] as JsonValue)?.ToString() != "=")
            {
                continue;
            }

            // Only a literal value is constrained. A value supplied via the "_value" primitive
            // extension (e.g. a cqf-expression template) has no literal to check, so leave it alone.
            if ((filter["value"] as JsonValue)?.ToString() is not { } value)
            {
                continue;
            }

            if (value is not ("true" or "false"))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "vsf-1",
                    $"The value for a filter based on property '{property}' must be either 'true' or 'false', not '{value}'",
                    $"{path}[{i}].value"));
            }
        }
    }
}
