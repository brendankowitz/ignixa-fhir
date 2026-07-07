// <copyright file="BundleLinkRelationCheck.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that each Bundle.link.relation value occurs at most once. Tier 1 (Fast) validator,
/// scoped to the Bundle resource type - a closed-world structural rule with no terminology
/// dependency (link relation values are not bound to a code system we validate).
/// </summary>
public sealed class BundleLinkRelationCheck : IValidationCheck, ISingletonCheck
{
    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var links = element.Children("link");
        if (links.Count == 0)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        var seenRelations = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < links.Count; i++)
        {
            var relationChildren = links[i].Children("relation");
            if (relationChildren.Count == 0)
            {
                continue;
            }

            var relation = relationChildren[0].Value?.ToString();
            if (string.IsNullOrEmpty(relation))
            {
                continue;
            }

            if (!seenRelations.Add(relation))
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "invalid",
                    $"The link relationship type '{relation}' can only occur once",
                    links[i].Location ?? $"{element.Location}.link[{i}]"));
            }
        }

        return issues.Count > 0 ? ValidationResult.Failure(issues) : ValidationResult.Success();
    }
}
