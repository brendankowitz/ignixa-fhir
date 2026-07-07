// <copyright file="BundleFullUrlCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that, when present, Bundle.entry.fullUrl is an absolute URI rather than a relative
/// reference (e.g. "Patient/1"). Tier 1 (Fast) validator, scoped to the Bundle resource type.
/// </summary>
/// <remarks>
/// Only checks fullUrl values that are actually present: whether a fullUrl is required at all is a
/// separate, context-dependent FHIR rule (it varies by Bundle.type and whether entries need to
/// resolve local references) that this check does not attempt. Skipped in Compatibility mode,
/// matching the same absolute-URI leniency <see cref="CodingStructureCheck"/> already applies to
/// Coding.system for Microsoft FHIR Server alignment.
/// </remarks>
public sealed class BundleFullUrlCheck : IValidationCheck, ISingletonCheck
{
    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth == ValidationDepth.Compatibility)
        {
            return ValidationResult.Success();
        }

        var entries = element.Children("entry");
        if (entries.Count == 0)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        for (var i = 0; i < entries.Count; i++)
        {
            var fullUrlChildren = entries[i].Children("fullUrl");
            if (fullUrlChildren.Count == 0)
            {
                continue;
            }

            var fullUrlNode = fullUrlChildren[0];
            var fullUrl = fullUrlNode.Value?.ToString();
            if (string.IsNullOrEmpty(fullUrl) || Uri.TryCreate(fullUrl, UriKind.Absolute, out _))
            {
                continue;
            }

            issues.Add(ValidationIssue.InvariantFailure(
                "invalid",
                $"The fullUrl must be an absolute URL (not '{fullUrl}')",
                fullUrlNode.Location ?? $"{element.Location}.entry[{i}].fullUrl"));
        }

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }
}
