// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates each resource a Bundle or Parameters resource carries (<c>Bundle.entry.resource</c>,
/// <c>Bundle.entry.response.outcome</c>, <c>Parameters.parameter.resource</c> and its parts' resources, and any other
/// element of type Resource except <c>contained</c>) against its own type's schema and its <c>meta.profile</c>, with every
/// issue rooted at the entry (<c>Bundle.entry[3].resource.subject</c>). Spec tier, once per resource.
/// </summary>
/// <remarks>
/// An entry is an independent resource: its <c>%resource</c> and <c>%rootResource</c> are the entry itself, while its
/// references to sibling entries resolve against the container (<see cref="ValidationState.EnclosingContainer"/>). An entry
/// whose <c>resourceType</c> is missing or unknown is an error. Contained resources are left to
/// <see cref="ContainedResourceCheck"/>.
/// </remarks>
internal sealed class EntryResourceCheck(IValidationSchemaResolver schemaResolver) : IValidationCheck, ISingletonCheck
{
    private readonly IElementSchemaResolver _resolver = schemaResolver as IElementSchemaResolver
        ?? new ProfileAwareValidationSchemaResolver(schemaResolver);

    /// <summary>Whether <paramref name="child"/> is a resource carried by an element of type Resource, other than a contained one.</summary>
    public static bool IsEntryResource(IElement child) =>
        child.Name != "contained" && child.Type is ITypeExtended extended && DeclaredType(extended) == "Resource";

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth == ValidationDepth.Minimal)
        {
            return ValidationResult.Success();
        }

        List<IElement> entries = [];
        Collect(element, entries);
        if (entries.Count == 0)
        {
            return ValidationResult.Success();
        }

        if (!state.TryDescend(out var descended))
        {
            return new ValidationResult(true, [ValidationIssue.NestingLimitExceeded(state.Location.InstancePath, "resource")]);
        }

        var issues = new List<ValidationIssue>();
        var isValid = true;
        foreach (var entry in entries)
        {
            var schema = string.IsNullOrEmpty(entry.InstanceType) ? null : _resolver.ResolveForElement(entry);
            if (schema is null)
            {
                issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Error, "entry-invalid-resourcetype", entry.Location, IssueTemplates.EntryResourceTypeUnknown));
                isValid = false;
                continue;
            }

            var result = schema.Validate(entry, settings, descended.WithLocation(entry.Location).EnterEntryResource(entry));
            isValid &= result.IsValid;
            issues.AddRange(NestedResourceIssues.Rebase(
                result.IssuesOrSynthesizedFailure(entry.Location, $"entry '{schema.ResourceType}' resource"),
                schema.ResourceType,
                entry.Location));
        }

        return new ValidationResult(isValid, issues);
    }

    private static void Collect(IElement node, List<IElement> entries)
    {
        foreach (var child in node.Children())
        {
            if (child.Name == "contained")
            {
                continue;
            }

            if (IsEntryResource(child))
            {
                entries.Add(child);
                continue;
            }

            Collect(child, entries);
        }
    }

    private static string? DeclaredType(ITypeExtended extended) =>
        !string.IsNullOrEmpty(extended.DefaultTypeName) ? extended.DefaultTypeName : extended.Types is [{ } first, ..] ? first.Code : null;
}
