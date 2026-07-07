// <copyright file="EmptyArrayCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Rejects a JSON array that is present but empty anywhere in the resource's raw JSON tree
/// ("Array cannot be empty - the property should not be present if it has no values"). Tier 2
/// (Spec) validator, mirroring <see cref="StructuralShapeCheck"/>'s ele-1 empty-array rule.
/// </summary>
/// <remarks>
/// <see cref="StructuralShapeCheck"/> already enforces this for every declared element that gets
/// its own nested schema, but complex datatypes such as CodeableConcept and Coding are not expanded
/// into a nested schema (see <c>StructureDefinitionSchemaBuilder.ResolveNestedType</c>), so an empty
/// array nested inside one of them (e.g. <c>category[0].coding: []</c>) is never inspected by that
/// per-element check. This check closes that gap with a single schema-independent walk of the raw
/// JSON tree from the resource root, so the rule applies uniformly regardless of how deep the
/// StructureDefinition-driven schema expansion goes.
/// <para>
/// "contained" is excluded: contained resources are validated independently via
/// <see cref="ContainedResourceCheck"/>, which applies this same structural rule (and every other
/// resource-level check) to each contained resource's own JSON subtree. Walking into "contained"
/// here would either duplicate that validation or require re-deriving its resource-root path
/// conventions; skipping it keeps this check strictly about the current resource's own shape.
/// </para>
/// </remarks>
public sealed class EmptyArrayCheck : IValidationCheck, ISingletonCheck
{
    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.Meta<JsonNode>() is not JsonObject root)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        WalkObject(root, element.InstanceType, issues);

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }

    private static void WalkObject(JsonObject obj, string path, List<ValidationIssue> issues)
    {
        foreach (var (key, value) in obj)
        {
            if (value is null || string.Equals(key, "resourceType", StringComparison.Ordinal)
                || string.Equals(key, "contained", StringComparison.Ordinal))
            {
                continue;
            }

            var childPath = $"{path}.{key}";
            switch (value)
            {
                case JsonArray array:
                    WalkArray(array, childPath, issues);
                    break;
                case JsonObject nested:
                    WalkObject(nested, childPath, issues);
                    break;
            }
        }
    }

    private static void WalkArray(JsonArray array, string path, List<ValidationIssue> issues)
    {
        if (array.Count == 0)
        {
            issues.Add(ValidationIssue.InvariantFailure(
                "invalid",
                "Array cannot be empty - the property should not be present if it has no values",
                path));
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonObject item)
            {
                WalkObject(item, $"{path}[{i}]", issues);
            }
        }
    }
}
