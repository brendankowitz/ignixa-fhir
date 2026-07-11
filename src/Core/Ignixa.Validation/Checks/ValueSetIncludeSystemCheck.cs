// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Enforces that every <c>ValueSet.compose.include.system</c> / <c>exclude.system</c> URI is
/// absolute. A fragment (<c>#localCodeSystem</c>) or relative reference cannot identify a code
/// system, so the reference validator rejects it: "URI values in ValueSet.compose.include.system
/// must be absolute." Closed-world and terminology-independent — a single raw-JSON walk of the
/// ValueSet root, so it is registered only for the ValueSet resource type.
/// </summary>
public sealed class ValueSetIncludeSystemCheck : IValidationCheck, ISingletonCheck, ICompatibilityConformanceCheck
{
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
            if (group[i] is not JsonObject entry || entry["system"] is not JsonValue systemValue)
            {
                continue;
            }

            var system = systemValue.ToString();
            if (!string.IsNullOrEmpty(system) && !IsAbsoluteUri(system))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "vs-1",
                    "URI values in ValueSet.compose.include.system must be absolute. To reference a "
                        + "contained code system, use the full CodeSystem URL and reference it using "
                        + "the http://hl7.org/fhir/StructureDefinition/valueset-system extension",
                    $"ValueSet.compose.{groupName}[{i}].system"));
            }
        }
    }

    private static bool IsAbsoluteUri(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out _);
}
