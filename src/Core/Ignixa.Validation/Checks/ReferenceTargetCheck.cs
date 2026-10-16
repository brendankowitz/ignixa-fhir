// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Validates that a <c>Reference</c> element points at a resource type its definition allows
/// (<c>ElementDefinition.type.targetProfile</c>, the core definition's or a profile's), and that a <c>Reference.type</c> agrees
/// with the reference it qualifies. Spec tier; not run at Minimal or Compatibility depth.
/// </summary>
/// <remarks>
/// <para>
/// The referenced resource's type is told, in order, from: a contained resource a <c>#id</c> names; the host's
/// <see cref="ValidationSettings.ReferenceTypeResolver"/>; an entry of the enclosing Bundle or Parameters; a relative
/// <c>Type/id</c> (optionally <c>/_history/vid</c>); an absolute http(s) URL ending in <c>Type/id</c>, where the id need not be
/// a FHIR id (another server's <c>abc_def</c>) but a page (<c>.html</c>), a query, a fragment or an id longer than 64
/// characters is not a resource reference. When it is told and <c>Reference.type</c> names another type, the reference is
/// <c>reference-type-mismatch</c>. The type checked against the allowed ones is the reference's own, else <c>Reference.type</c>.
/// </para>
/// <para>
/// A reference whose type cannot be told (an unresolved urn, an identifier only, a logical reference), one with only a
/// display, and every reference at an element whose target profile is not loaded are reported as a non-failing
/// <c>reference-target-unchecked</c> information issue, never passed: no validator can check them without the target.
/// </para>
/// </remarks>
internal sealed partial class ReferenceTargetCheck : IValidationCheck
{
    private const string CorePrefix = "http://hl7.org/fhir/StructureDefinition/";

    private readonly string _elementName;
    private readonly IReadOnlySet<string> _allowed;
    private readonly IReadOnlyList<string> _unloadedTargetProfiles;
    private readonly Func<string, bool> _isResourceType;
    private readonly string _allowedText;

    /// <param name="elementName">The concrete element name (<c>subject</c>, <c>valueReference</c>).</param>
    /// <param name="allowed">The resource types the element allows; not empty.</param>
    /// <param name="unloadedTargetProfiles">Target profiles whose resource type cannot be told because they are not loaded.</param>
    /// <param name="isResourceType">Whether a name is a resource type of the FHIR version.</param>
    public ReferenceTargetCheck(string elementName, IReadOnlySet<string> allowed, IReadOnlyList<string> unloadedTargetProfiles, Func<string, bool> isResourceType)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(unloadedTargetProfiles);
        ArgumentNullException.ThrowIfNull(isResourceType);
        if (allowed.Count == 0 && unloadedTargetProfiles.Count == 0)
        {
            throw new ArgumentException("A reference target check needs an allowed type or an unloaded target profile.", nameof(allowed));
        }

        _elementName = elementName;
        _allowed = allowed;
        _unloadedTargetProfiles = unloadedTargetProfiles;
        _isResourceType = isResourceType;
        _allowedText = string.Join(", ", allowed.Order(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        if (settings.Depth is ValidationDepth.Minimal or ValidationDepth.Compatibility)
        {
            return ValidationResult.Success();
        }

        var references = element.Children(_elementName);
        if (references.Count == 0)
        {
            return ValidationResult.Success();
        }

        var issues = new List<ValidationIssue>();
        var lookup = new Lookup(state);
        foreach (var reference in references)
        {
            Check(reference, settings, lookup, issues);
        }

        return issues.Count == 0
            ? ValidationResult.Success()
            : new ValidationResult(!issues.Any(issue => issue.Severity == IssueSeverity.Error), issues);
    }

    private void Check(IElement reference, ValidationSettings settings, Lookup lookup, List<ValidationIssue> issues)
    {
        string location = reference.Location;
        string? written = reference.Children("reference") is [{ Value: string text }, ..] ? text : null;
        string? own = string.IsNullOrEmpty(written) ? null : TypeOfReference(written, location, settings, lookup);
        string? declared = reference.Children("type") is [{ Value: string type }, ..] ? TypeName(type) : null;
        if (own is not null && declared is not null && own != declared)
        {
            issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Error, "reference-type-mismatch", location, IssueTemplates.ReferenceTypeMismatch, own, declared));
        }

        if (_unloadedTargetProfiles.Count > 0)
        {
            issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Information, "reference-target-unchecked", location, IssueTemplates.ReferenceTargetProfileNotLoaded, _unloadedTargetProfiles[0]));
            return;
        }

        string? target = own ?? declared;
        if (target is null)
        {
            bool displayOnly = written is null && reference.Children("identifier").Count == 0 && reference.Children("display").Count > 0;
            issues.Add(ValidationIssue.FromTemplate(
                IssueSeverity.Information,
                "reference-target-unchecked",
                location,
                displayOnly ? IssueTemplates.ReferenceTargetDisplayOnly : IssueTemplates.ReferenceTargetUndeterminable,
                _allowedText));
            return;
        }

        if (!_allowed.Contains(target))
        {
            issues.Add(ValidationIssue.FromTemplate(IssueSeverity.Error, "reference-target-type", location, IssueTemplates.ReferenceTargetType, target, _allowedText));
        }
    }

    /// <summary>The resource type <paramref name="written"/> names, or null when it cannot be told.</summary>
    private string? TypeOfReference(string written, string location, ValidationSettings settings, Lookup lookup)
    {
        if (written.StartsWith('#'))
        {
            var target = written.Length == 1 ? lookup.Index.ResolveContainerScope(lookup.Resource) : lookup.Index.Resolve(written, location);
            return target?.InstanceType is { } containedType && _isResourceType(containedType) ? containedType : null;
        }

        if (settings.ReferenceTypeResolver?.ResolveResourceType(written) is { } hosted)
        {
            return _isResourceType(hosted) ? hosted : null;
        }

        if (lookup.InContainer && lookup.Index.Resolve(written, location)?.InstanceType is { } entryType && _isResourceType(entryType))
        {
            return entryType;
        }

        Match relative = Relative().Match(written);
        if (relative.Success)
        {
            return Known(relative.Groups["type"].Value);
        }

        return Uri.TryCreate(written, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" ? TypeOfUrl(uri) : null;
    }

    /// <summary>
    /// An absolute URL's type: the segment before the id, or before <c>id/_history/vid</c>, when the URL can name a resource
    /// at all (no query, fragment or page, an id of at most 64 characters).
    /// </summary>
    private string? TypeOfUrl(Uri uri)
    {
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int typeIndex = segments.Length >= 4 && segments[^2] == "_history" ? segments.Length - 4 : segments.Length - 2;
        return typeIndex >= 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && PlausibleId(segments[typeIndex + 1])
            ? Known(segments[typeIndex])
            : null;
    }

    private static bool PlausibleId(string segment) =>
        segment.Length <= 64
        && !segment.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
        && !segment.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>Reference.type</c>: a resource type name, or the core StructureDefinition url of one.</summary>
    private string? TypeName(string declared) =>
        Known(declared.StartsWith(CorePrefix, StringComparison.Ordinal) ? declared[CorePrefix.Length..] : declared);

    private string? Known(string type) => _isResourceType(type) ? type : null;

    // A relative Type/id (a FHIR id), optionally with /_history/vid.
    [GeneratedRegex("^(?<type>[A-Z][A-Za-z]{1,63})/[A-Za-z0-9\\-\\.]{1,64}(?:/_history/[A-Za-z0-9\\-\\.]{1,64})?$")]
    private static partial Regex Relative();

    /// <summary>The in-instance reference index for one validation of the check, built only when a reference needs it.</summary>
    private sealed class Lookup(ValidationState state)
    {
        private ReferenceIndex? _index;

        public IElement Resource => state.Scope.Resource;

        public bool InContainer => state.Scope.RootResource.InstanceType is "Bundle" or "Parameters";

        public ReferenceIndex Index => _index ??= ReferenceIndex.Build(state.Scope.RootResource);
    }
}
