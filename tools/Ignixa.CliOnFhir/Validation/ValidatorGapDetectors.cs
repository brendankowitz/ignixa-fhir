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
    private readonly string _rootType;
    private readonly Dictionary<string, string> _contained = new(StringComparer.Ordinal);
    private readonly List<(ValidationIssue Raw, ClassifiedIssue Classified)> _found = [];

    /// <param name="coreUrl">The core StructureDefinition core issues are attributed to.</param>
    /// <param name="profiles">The profiles the resource is validated against, each with the name its issues are attributed to.</param>
    /// <param name="profileType">The resource type a target profile canonical constrains, or null when it cannot be told.</param>
    /// <param name="resourceTypes">The FHIR version's resource type names.</param>
    /// <param name="resolved">The resource's Bundle references as written and the <c>Type/id</c> they resolve to in the folder.</param>
    /// <param name="resource">The resource, whose contained resources <c>#id</c> references name.</param>
    private ValidatorGapDetectors(IElement resource, string coreUrl, IReadOnlyList<(LoadedProfile Profile, string Name)> profiles, Func<string, string?> profileType, IReadOnlySet<string> resourceTypes, IReadOnlyDictionary<string, string> resolved)
    {
        _coreUrl = coreUrl;
        _profiles = profiles;
        _profileType = profileType;
        _resourceTypes = resourceTypes;
        _resolved = resolved;
        _rootType = resource.InstanceType ?? resource.Name;
        foreach (IElement contained in resource.Children("contained"))
        {
            if (contained.Children("id") is [{ Value: string id }, ..] && contained.InstanceType is { } type && resourceTypes.Contains(type))
            {
                _contained.TryAdd(id, type);
            }
        }
    }

    /// <summary>References whose target type could not be told (an unresolved urn, an identifier only): not checked.</summary>
    public int UndeterminableReferences { get; private set; }

    /// <summary>References with only a <c>display</c>: nothing to check.</summary>
    public int DisplayOnlyReferences { get; private set; }

    /// <summary>The skipped-check issues of <paramref name="resource"/>, each with its summary form, and the references it could not check.</summary>
    public static GapFindings Detect(
        IElement resource,
        string coreUrl,
        IReadOnlyList<(LoadedProfile Profile, string Name)> profiles,
        Func<string, string?> profileType,
        IReadOnlySet<string> resourceTypes,
        IReadOnlyDictionary<string, string> resolved)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var detectors = new ValidatorGapDetectors(resource, coreUrl, profiles, profileType, resourceTypes, resolved);
        detectors.Walk(resource, detectors._rootType);
        return new GapFindings(detectors._found, detectors.UndeterminableReferences, detectors.DisplayOnlyReferences);
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
    /// Task 9b gap (reference-target-types): Ignixa.Validation has no reference target check (<c>ElementDefinition.type.targetProfile</c>
    /// is never evaluated), so <c>Observation.subject</c> referencing an Organization passes. Here:
    /// <list type="bullet">
    /// <item>The reference's own type is told from <c>reference</c>: a relative <c>Type/id</c>, an absolute URL ending in
    /// <c>Type/id</c> (any id, for a foreign server), a <c>urn:</c> the folder's Bundles resolve, or a <c>#id</c> naming one of
    /// the resource's contained resources. When it is told and <c>Reference.type</c> names another type, the reference is
    /// <c>reference-type-mismatch</c>, an error.</item>
    /// <item>The type checked against the element's allowed types (the core definition's, narrowed by each profile's target
    /// profiles) is the reference's own, else <c>Reference.type</c>; one not allowed is <c>reference-target-type</c>, an error.</item>
    /// <item>A reference whose type cannot be told (an unresolved urn, an identifier only, a profile target the session cannot
    /// map) is counted in <see cref="UndeterminableReferences"/>, one with only a
    /// <c>display</c> in <see cref="DisplayOnlyReferences"/>; either leaves the verdict as it is, since no validator can check it
    /// without the target.</item>
    /// </list>
    /// </summary>
    private void ReferenceTarget(IElement element, string definitionPath)
    {
        string? written = element.Children("reference") is [{ Value: string text }, ..] ? text : null;
        string? own = written is null ? null : TypeOfReference(written);
        string? declared = element.Children("type") is [{ Value: string type }, ..] ? TypeName(type) : null;
        if (own is not null && declared is not null && own != declared)
        {
            var mismatch = new ValidationIssue(
                IssueSeverity.Error,
                ValidationRules.ReferenceTypeMismatch,
                element.Location,
                $"{ValidationRules.ReferenceTypeMismatch}: the reference is to a {own}, but its type says {declared}");
            _found.Add((mismatch, new ClassifiedIssue("error", _coreUrl, ValidationRules.ReferenceTypeMismatch, null, null, IssuePath.Normalize(element.Location))));
        }

        List<(IReadOnlyCollection<string>? Allowed, string Profile)> constraints = Constraints(element, definitionPath);
        if (constraints.Count == 0 || constraints.All(constraint => constraint.Allowed is null))
        {
            return;
        }

        if (own is null && written is ['#', ..])
        {
            // A #id naming nothing contained: the validator reports it (ref-resolve), so it is not counted here.
            return;
        }

        string? target = own ?? declared;
        if (target is null && written is null && element.Children("identifier").Count == 0 && element.Children("display").Count > 0)
        {
            DisplayOnlyReferences++;
            return;
        }

        if (target is null || constraints.Any(constraint => constraint.Allowed is { Count: 0 }))
        {
            // The target's type, or what a profile allows, cannot be told.
            UndeterminableReferences++;
            return;
        }

        if (constraints.FirstOrDefault(constraint => constraint.Allowed is not null && !constraint.Allowed.Contains(target)) is ({ } allowed, { } profile))
        {
            var raw = new ValidationIssue(
                IssueSeverity.Error,
                ValidationRules.ReferenceTargetType,
                element.Location,
                $"{ValidationRules.ReferenceTargetType}: the reference is to a {target}, which this element does not allow ({string.Join(", ", allowed.Order(StringComparer.Ordinal))})");
            _found.Add((raw, new ClassifiedIssue("error", profile, ValidationRules.ReferenceTargetType, null, null, IssuePath.Normalize(element.Location))));
        }
    }

    /// <summary>
    /// What each definition lets the reference at <paramref name="definitionPath"/> point to: null for anything, empty when a
    /// profile's target profiles cannot all be mapped to a type.
    /// </summary>
    private List<(IReadOnlyCollection<string>? Allowed, string Profile)> Constraints(IElement element, string definitionPath)
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

        return constraints;
    }

    /// <summary>The resource type <paramref name="written"/> names, or null when it cannot be told.</summary>
    private string? TypeOfReference(string written)
    {
        if (written.StartsWith('#'))
        {
            // "#" alone is the container itself; "#id" one of its contained resources.
            return written.Length == 1 ? _rootType : _contained.GetValueOrDefault(written[1..]);
        }

        string target = _resolved.GetValueOrDefault(written) ?? written;
        Match relative = Relative().Match(target);
        if (relative.Success)
        {
            return Known(relative.Groups["type"].Value);
        }

        return Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? TypeOfUrl(uri) : null;
    }

    /// <summary>
    /// An absolute URL's type: the segment before the id, or before <c>id/_history/vid</c>. The id need not be a FHIR id (a
    /// foreign server's <c>abc_def</c> is one), but a URL with a query, a fragment, a page (<c>.html</c>) or an id longer than
    /// FHIR allows is not a reference to a resource, so its type is not told.
    /// </summary>
    private string? TypeOfUrl(Uri uri)
    {
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int typeIndex = segments.Length >= 4 && segments[^2] == "_history" ? segments.Length - 4 : segments.Length - 2;
        return typeIndex >= 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && PlausibleId(segments[typeIndex + 1]) ? Known(segments[typeIndex]) : null;
    }

    private static bool PlausibleId(string segment) =>
        segment.Length <= 64 && !segment.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !segment.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    private string? Known(string type) => _resourceTypes.Contains(type) ? type : null;

    /// <summary><c>Reference.type</c>: a resource type name, or the core StructureDefinition url of one.</summary>
    private string? TypeName(string declared)
    {
        const string CorePrefix = "http://hl7.org/fhir/StructureDefinition/";
        string name = declared.StartsWith(CorePrefix, StringComparison.Ordinal) ? declared[CorePrefix.Length..] : declared;
        return Known(name);
    }

    private static string Named(string canonical)
    {
        (string url, string? version) = TerminologyStore.SplitCanonical(canonical);
        return TerminologyNames.Canonical(url, version);
    }

    // A relative Type/id (a FHIR id), optionally with /_history/vid.
    [GeneratedRegex("^(?<type>[A-Z][A-Za-z]{1,63})/[A-Za-z0-9\\-\\.]{1,64}(?:/_history/[A-Za-z0-9\\-\\.]{1,64})?$")]
    private static partial Regex Relative();
}
