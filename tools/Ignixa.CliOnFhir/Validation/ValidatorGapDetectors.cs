// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Checks Ignixa.Validation skips without saying so, found by the tool over the resource's own elements so that a skipped
/// check makes the verdict <c>unknown</c> instead of letting it read <c>valid</c>. Each detector names the gap in
/// <c>src/Core/Ignixa.Validation</c> it covers; when Task 9b closes a gap, delete its detector. Every issue here is the tool's
/// own (fixed text, a shape-checked ValueSet), never resource content.
/// </summary>
internal static class ValidatorGapDetectors
{
    /// <summary>The skipped-check issues of <paramref name="resource"/>, each with its summary form.</summary>
    /// <param name="resource">The resource, read with the schema it is validated against.</param>
    /// <param name="profile">The core StructureDefinition the issues are attributed to.</param>
    public static IEnumerable<(ValidationIssue Raw, ClassifiedIssue Classified)> Detect(IElement resource, string profile)
    {
        ArgumentNullException.ThrowIfNull(resource);

        List<IElement> descendants = [.. resource.Select("descendants()")];
        return [.. TextOnlyRequiredConcepts(descendants, profile), .. Extensions(descendants, profile)];
    }

    /// <summary>
    /// Gap: <c>Checks/BindingCheck.cs</c> (ValidateCodeableConcept) returns Success for a CodeableConcept with no coding, so a
    /// text-only concept under a required binding passes without any check. Here it is <c>binding-not-checked</c>.
    /// </summary>
    private static IEnumerable<(ValidationIssue, ClassifiedIssue)> TextOnlyRequiredConcepts(List<IElement> descendants, string profile)
    {
        foreach (IElement element in descendants)
        {
            if (element.InstanceType == "CodeableConcept"
                && element.Type is ITypeExtended { Binding: { ValueSet: { Length: > 0 } url } binding }
                && string.Equals(binding.Strength, "required", StringComparison.OrdinalIgnoreCase)
                && element.Children("coding").Count == 0)
            {
                (string canonical, string? version) = TerminologyStore.SplitCanonical(url);
                string valueSet = TerminologyNames.Canonical(canonical, version);
                var raw = new ValidationIssue(
                    IssueSeverity.Warning,
                    StoreTerminologyService.NotCheckedRule,
                    element.Location,
                    $"{StoreTerminologyService.NotCheckedRule}: the concept has text only, so membership in the required ValueSet {valueSet} was not checked");
                yield return (raw, new ClassifiedIssue("warning", profile, StoreTerminologyService.NotCheckedRule, null, valueSet, IssuePath.Normalize(element.Location)));
            }
        }
    }

    /// <summary>
    /// Gap: <c>Schema/StructureDefinitionSchemaBuilder.cs</c> never builds a nested schema for <c>extension</c> or
    /// <c>modifierExtension</c>, so extensions (their definitions, value types and bindings) are not validated at any depth.
    /// Here each is <c>extensions-not-validated</c>.
    /// </summary>
    private static IEnumerable<(ValidationIssue, ClassifiedIssue)> Extensions(List<IElement> descendants, string profile)
    {
        foreach (IElement element in descendants.Where(element => element.Name is "extension" or "modifierExtension"))
        {
            var raw = new ValidationIssue(
                IssueSeverity.Warning,
                ValidationRules.ExtensionsNotValidated,
                element.Location,
                $"{ValidationRules.ExtensionsNotValidated}: the validator does not check extensions, so this one was not validated");
            yield return (raw, new ClassifiedIssue("warning", profile, ValidationRules.ExtensionsNotValidated, null, null, IssuePath.Normalize(element.Location)));
        }
    }
}
