// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates Bundle entry resources against their own StructureDefinition. Tier 2 (Spec).
/// </summary>
/// <remarks>
/// In the Bundle StructureDefinition <c>entry.resource</c> is typed as the abstract
/// <c>Resource</c>, so without this check entry resources receive only base-resource validation.
/// This is the Bundle analogue of <see cref="ContainedResourceCheck"/>: each entry resource is
/// resolved to its concrete schema and validated as an independent root
/// (<see cref="ValidationState.EnterBundleEntry"/> sets <c>%resource</c>/<c>%rootResource</c> to the
/// entry resource and keeps intra-bundle references resolvable). Entries without a resource
/// (request/response-only entries in batch/transaction bundles) are skipped.
/// </remarks>
public sealed class BundleEntryCheck(IValidationSchemaResolver schemaResolver) : IValidationCheck
{
    private readonly IValidationSchemaResolver _schemaResolver = schemaResolver ?? throw new ArgumentNullException(nameof(schemaResolver));

    /// <summary>
    /// Validates all entry resources within a Bundle element.
    /// </summary>
    /// <param name="element">The Bundle element containing the "entry" array.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state.</param>
    /// <returns>A validation result with issues from all entry resource validations.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (element.InstanceType != "Bundle")
        {
            return ValidationResult.Success();
        }

        var entries = element.Children("entry");
        if (entries.Count == 0)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();

        for (int i = 0; i < entries.Count; i++)
        {
            var resourceChildren = entries[i].Children("resource");
            if (resourceChildren.Count == 0)
            {
                // Batch/transaction request/response entries carry no resource — nothing to validate.
                continue;
            }

            var entryResource = resourceChildren[0];
            var entryPath = $"entry[{i}].resource";
            var resourceType = ResolveResourceType(entryResource);

            if (string.IsNullOrEmpty(resourceType))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "bundle-entry-missing-resourcetype",
                    $"{element.Location}.{entryPath}",
                    "Bundle entry resource must have a 'resourceType' property"));
                continue;
            }

            var entrySchema = _schemaResolver.GetSchema(resourceType);
            if (entrySchema is null)
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    "bundle-entry-invalid-resourcetype",
                    $"{element.Location}.{entryPath}",
                    $"Unknown resource type '{resourceType}' in bundle entry"));
                continue;
            }

            var entryState = state.WithLocation(entryPath).EnterBundleEntry(entryResource);
            var entryResult = entrySchema.Validate(entryResource, settings, entryState);

            if (!entryResult.IsValid)
            {
                var parentPrefix = $"{element.Location}.{entryPath}";
                foreach (var issue in entryResult.Issues)
                {
                    issues.Add(issue with { Path = RebasePath(issue.Path, resourceType, parentPrefix) });
                }
            }
        }

        return issues.Count > 0
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }

    private static string? ResolveResourceType(IElement entryResource)
    {
        var resourceType = entryResource.InstanceType;
        if (!string.IsNullOrEmpty(resourceType) && resourceType != "Resource")
        {
            return resourceType;
        }

        var resourceTypeChild = entryResource.Children("resourceType");
        return resourceTypeChild.Count == 0 ? resourceType : resourceTypeChild[0].Value?.ToString();
    }

    private static string RebasePath(string path, string resourceType, string parentPrefix)
    {
        // The nested validation returns paths relative to the entry resource (e.g. "Observation.status").
        // Re-root them under the entry path so issues point into the Bundle.
        if (path.StartsWith($"{resourceType}.", StringComparison.Ordinal))
        {
            return $"{parentPrefix}.{path[(resourceType.Length + 1)..]}";
        }

        if (path == resourceType)
        {
            return parentPrefix;
        }

        return path.StartsWith(parentPrefix, StringComparison.Ordinal)
            ? path
            : $"{parentPrefix}.{path}";
    }
}
