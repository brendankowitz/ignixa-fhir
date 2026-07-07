// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Enforces that no <c>extension.url</c> / <c>modifierExtension.url</c> carries a version suffix
/// (<c>|version</c>). An extension URL is a bare canonical identity; a version pipe belongs on a
/// reference to the extension definition, not on the instance's url. The reference validator
/// rejects it: "The extension URL must not contain a version." Closed-world — a single raw-JSON
/// walk of the resource root that descends through every element, since extensions can appear at
/// any depth.
/// </summary>
public sealed class ExtensionUrlVersionCheck : IValidationCheck, ISingletonCheck
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
            // Contained resources are validated independently against their own schema, which applies
            // this same rule; walking into them here would duplicate the diagnostic.
            if (value is null || string.Equals(key, "contained", StringComparison.Ordinal))
            {
                continue;
            }

            var childPath = $"{path}.{key}";
            var isExtensionArray = key is "extension" or "modifierExtension";

            switch (value)
            {
                case JsonArray array:
                    WalkArray(array, childPath, isExtensionArray, issues);
                    break;
                case JsonObject nested:
                    WalkObject(nested, childPath, issues);
                    break;
            }
        }
    }

    private static void WalkArray(JsonArray array, string path, bool isExtensionArray, List<ValidationIssue> issues)
    {
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                continue;
            }

            if (isExtensionArray
                && item["url"] is JsonValue urlValue
                && urlValue.ToString() is { Length: > 0 } url
                && url.Contains('|', StringComparison.Ordinal))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "ext-url-version",
                    "The extension URL must not contain a version",
                    $"{path}[{i}].url"));
            }

            WalkObject(item, $"{path}[{i}]", issues);
        }
    }
}
