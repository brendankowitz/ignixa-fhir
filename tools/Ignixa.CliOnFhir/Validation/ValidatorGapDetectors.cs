// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.Validation;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// Checks Ignixa.Validation skips without saying so, done by the tool over the resource's own elements so that a skipped
/// check makes the verdict <c>unknown</c> (or a violation <c>invalid</c>) instead of letting it read <c>valid</c>. Each detector
/// names the gap in <c>src/Core/Ignixa.Validation</c> it covers; when Task 9b closes a gap, delete its detector. The rules
/// are the effective ones: the core definition's, and those of every profile the resource is validated against
/// (<see cref="LoadedProfile.Elements"/>). Every issue here is the tool's own text (resource type names, shape-checked
/// canonicals), never resource content. For one resource; not thread-safe.
/// </summary>
internal sealed partial class ValidatorGapDetectors
{
    private readonly string _coreUrl;
    private readonly IReadOnlyList<(LoadedProfile Profile, string Name)> _profiles;
    private readonly Func<string, string?> _profileType;
    private readonly IReadOnlySet<string> _resourceTypes;
    private readonly IReadOnlyDictionary<string, string> _resolved;
    private readonly List<(ValidationIssue Raw, ClassifiedIssue Classified)> _found = [];

    /// <param name="coreUrl">The core StructureDefinition core issues are attributed to.</param>
    /// <param name="profiles">The profiles the resource is validated against, each with the name its issues are attributed to.</param>
    /// <param name="profileType">The resource type a target profile canonical constrains, or null when it cannot be told.</param>
    /// <param name="resourceTypes">The FHIR version's resource type names.</param>
    /// <param name="resolved">The resource's Bundle references as written and the <c>Type/id</c> they resolve to in the folder.</param>
    private ValidatorGapDetectors(string coreUrl, IReadOnlyList<(LoadedProfile Profile, string Name)> profiles, Func<string, string?> profileType, IReadOnlySet<string> resourceTypes, IReadOnlyDictionary<string, string> resolved)
    {
        _coreUrl = coreUrl;
        _profiles = profiles;
        _profileType = profileType;
        _resourceTypes = resourceTypes;
        _resolved = resolved;
    }

    /// <summary>References whose target type could not be told (an unresolved urn, a contained <c>#</c>, an identifier only): not checked.</summary>
    public int UndeterminableReferences { get; private set; }

    /// <summary>The skipped-check issues of <paramref name="resource"/>, each with its summary form, and the references it could not check.</summary>
    public static (IReadOnlyList<(ValidationIssue Raw, ClassifiedIssue Classified)> Issues, int UndeterminableReferences) Detect(
        IElement resource,
        string coreUrl,
        IReadOnlyList<(LoadedProfile Profile, string Name)> profiles,
        Func<string, string?> profileType,
        IReadOnlySet<string> resourceTypes,
        IReadOnlyDictionary<string, string> resolved)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var detectors = new ValidatorGapDetectors(coreUrl, profiles, profileType, resourceTypes, resolved);
        detectors.Walk(resource, resource.InstanceType ?? resource.Name);
        return (detectors._found, detectors.UndeterminableReferences);
    }

    /// <summary>Visits every element with its definition path (indexes dropped, a choice variant as <c>value[x]</c>).</summary>
    private void Walk(IElement element, string definitionPath)
    {
        foreach (IElement child in element.Children())
        {
            // A contained resource's elements are named from its own type, which no profile of the container constrains.
            if (child.Name == "contained")
            {
                Walk(child, child.InstanceType ?? child.Name);
                continue;
            }

            string name = child.Type?.Info is { IsChoiceElement: true } info ? info.Name + "[x]" : child.Name;
            string path = $"{definitionPath}.{name}";
            Visit(child, path);
            Walk(child, path);
        }
    }

    private void Visit(IElement element, string definitionPath)
    {
        if (element.Name is "extension" or "modifierExtension")
        {
            Extension(element);
        }

        if (element.InstanceType == "CodeableConcept" && element.Children("coding").Count == 0)
        {
            TextOnlyConcept(element, definitionPath);
        }

        if (element.InstanceType == "Reference")
        {
            ReferenceTarget(element, definitionPath);
        }
    }

    /// <summary>
    /// Gap: <c>Checks/BindingCheck.cs</c> (ValidateCodeableConcept) returns Success for a CodeableConcept with no coding, so a
    /// text-only concept under a required binding (the core definition's, or one a profile adds) passes without any check.
    /// Here it is <c>binding-not-checked</c>, once per required ValueSet.
    /// </summary>
    private void TextOnlyConcept(IElement element, string definitionPath)
    {
        List<(string ValueSet, string Profile)> required = [];
        if (element.Type is ITypeExtended { Binding: { ValueSet: { Length: > 0 } core } binding } && string.Equals(binding.Strength, "required", StringComparison.OrdinalIgnoreCase))
        {
            required.Add((core, _coreUrl));
        }

        required.AddRange(_profiles
            .Where(applied => applied.Profile.Elements.GetValueOrDefault(definitionPath)?.RequiredValueSet is { Length: > 0 })
            .Select(applied => (applied.Profile.Elements[definitionPath].RequiredValueSet!, applied.Name)));

        foreach ((string url, string profile) in required.DistinctBy(item => item.ValueSet))
        {
            string valueSet = Named(url);
            var raw = new ValidationIssue(
                IssueSeverity.Warning,
                StoreTerminologyService.NotCheckedRule,
                element.Location,
                $"{StoreTerminologyService.NotCheckedRule}: the concept has text only, so membership in the required ValueSet {valueSet} was not checked");
            _found.Add((raw, new ClassifiedIssue("warning", profile, StoreTerminologyService.NotCheckedRule, null, valueSet, IssuePath.Normalize(element.Location))));
        }
    }

    /// <summary>
    /// Gap: <c>Schema/StructureDefinitionSchemaBuilder.cs</c> never builds a nested schema for <c>extension</c> or
    /// <c>modifierExtension</c>, so extensions (their definitions, value types and bindings) are not validated at any depth.
    /// Here each is <c>extensions-not-validated</c>.
    /// </summary>
    private void Extension(IElement element)
    {
        var raw = new ValidationIssue(
            IssueSeverity.Warning,
            ValidationRules.ExtensionsNotValidated,
            element.Location,
            $"{ValidationRules.ExtensionsNotValidated}: the validator does not check extensions, so this one was not validated");
        _found.Add((raw, new ClassifiedIssue("warning", _coreUrl, ValidationRules.ExtensionsNotValidated, null, null, IssuePath.Normalize(element.Location))));
    }

    /// <summary>
    /// Task 9b gap (reference-target-types): Ignixa.Validation has no reference target check (<c>ElementDefinition.type.targetProfile</c> is never evaluated),
    /// so <c>Observation.subject</c> referencing an Organization passes. Here a reference whose target type can be told (a
    /// relative <c>Type/id</c>, an absolute RESTful URL, a <c>urn:</c> the folder's Bundles resolve, or <c>Reference.type</c>) and
    /// that the element's allowed types (the core definition's, narrowed by each profile's target profiles) do not include is
    /// <c>reference-target-type</c>, an error. One whose type cannot be told (an unresolved urn, a contained <c>#</c>, an
    /// identifier only) is counted in <see cref="UndeterminableReferences"/> and leaves the verdict as it is: no validator can
    /// check it without the target.
    /// </summary>
    private void ReferenceTarget(IElement element, string definitionPath)
    {
        List<(IReadOnlyCollection<string>? Allowed, string Profile)> constraints = [];
        if (element.Type is ITypeExtended { ReferenceTargets: { Count: > 0 } core } && !core.Contains("Resource"))
        {
            constraints.Add((core, _coreUrl));
        }

        foreach ((LoadedProfile profile, string profileName) in _profiles)
        {
            if (profile.Elements.GetValueOrDefault(definitionPath)?.TargetProfiles is not { Count: > 0 } targets)
            {
                continue;
            }

            List<string?> types = [.. targets.Select(_profileType).Select(type => type is not null && (_resourceTypes.Contains(type) || type == "Resource") ? type : null)];
            constraints.Add((types.Contains("Resource") ? null : types.Any(type => type is null) ? [] : [.. types.OfType<string>()], profileName));
        }

        if (constraints.Count == 0 || constraints.All(constraint => constraint.Allowed is null))
        {
            return;
        }

        List<string> written = [.. TypesOf(element)];
        if (written.Count == 0 || constraints.Any(constraint => constraint.Allowed is { Count: 0 }))
        {
            // The target's type, or what a profile allows, cannot be told.
            UndeterminableReferences++;
            return;
        }

        foreach ((IReadOnlyCollection<string>? allowed, string profile) in constraints.Where(constraint => constraint.Allowed is not null))
        {
            if (written.FirstOrDefault(type => !allowed!.Contains(type)) is { } wrong)
            {
                var raw = new ValidationIssue(
                    IssueSeverity.Error,
                    ValidationRules.ReferenceTargetType,
                    element.Location,
                    $"{ValidationRules.ReferenceTargetType}: the reference is to a {wrong}, which this element does not allow ({string.Join(", ", allowed!.Order(StringComparer.Ordinal))})");
                _found.Add((raw, new ClassifiedIssue("error", profile, ValidationRules.ReferenceTargetType, null, null, IssuePath.Normalize(element.Location))));
                return;
            }
        }
    }

    /// <summary>The resource types the reference names: by its <c>reference</c> and by its <c>type</c>; empty when neither tells.</summary>
    private IEnumerable<string> TypesOf(IElement reference)
    {
        if (reference.Children("reference") is [{ Value: string written }, ..] && TypeOfReference(written) is { } byReference)
        {
            yield return byReference;
        }

        if (reference.Children("type") is [{ Value: string declared }, ..] && TypeName(declared) is { } byType)
        {
            yield return byType;
        }
    }

    private string? TypeOfReference(string written)
    {
        string target = _resolved.GetValueOrDefault(written) ?? written;
        Match match = Restful().Match(target);
        return match.Success && _resourceTypes.Contains(match.Groups["type"].Value) ? match.Groups["type"].Value : null;
    }

    /// <summary><c>Reference.type</c>: a resource type name, or the core StructureDefinition url of one.</summary>
    private string? TypeName(string declared)
    {
        const string CorePrefix = "http://hl7.org/fhir/StructureDefinition/";
        string name = declared.StartsWith(CorePrefix, StringComparison.Ordinal) ? declared[CorePrefix.Length..] : declared;
        return _resourceTypes.Contains(name) ? name : null;
    }

    private static string Named(string canonical)
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(canonical);
        return TerminologyNames.Canonical(url, version);
    }

    // A relative Type/id or an absolute RESTful URL ending in Type/id, optionally with /_history/vid.
    [GeneratedRegex("^(?:https?://[^\\s]+/)?(?<type>[A-Z][A-Za-z]{1,63})/[A-Za-z0-9\\-\\.]{1,64}(?:/_history/[A-Za-z0-9\\-\\.]{1,64})?$")]
    private static partial Regex Restful();
}
