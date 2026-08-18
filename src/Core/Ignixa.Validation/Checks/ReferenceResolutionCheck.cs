// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Reference-integrity check (Full tier). Flags clearly-local references that fail to resolve
/// within the resource being validated: fragment references (<c>#id</c>) and intra-Bundle relative
/// references (<c>Type/id</c>) inside a Bundle root.
/// </summary>
/// <remarks>
/// <para>
/// Scoped narrowly to avoid false positives: absolute URLs (<c>http(s)://</c>), <c>urn:</c>
/// references, and every reference when no resource scope has been seeded, are all left alone.
/// External and logical references legitimately do not resolve locally and are never flagged.
/// </para>
/// <para>
/// This is the ONLY consumer in validation that resolves references outside a FHIRPath evaluation,
/// which is why it builds its own <see cref="ReferenceIndex"/>. Everything reached through FHIRPath
/// (<c>resolve()</c> in an invariant or a slicing discriminator) is served instead by
/// <c>EvaluationContext.ReferenceIndexCache</c>, which builds the same index from the same root.
/// The two are separate builds, not separate implementations - both go through
/// <see cref="ReferenceIndex.Build"/> and <see cref="ReferenceIndex.Resolve(string, string?)"/>, so
/// the containment rules cannot drift between the invariant path and this one.
/// </para>
/// </remarks>
public sealed class ReferenceResolutionCheck : IValidationCheck
{
    /// <summary>
    /// Validates that local references within the resource resolve inside the seeded resource scope.
    /// </summary>
    /// <param name="element">The resource root element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state carrying the resource scope.</param>
    /// <returns>A validation result with an issue per unresolved local reference.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        // Full-tier only. The schema builder registers this in profileChecks (Full), so it is already
        // gated there; this guard makes the check self-protecting for direct callers and keeps the
        // potentially-expensive reference walk off the Spec/Compatibility paths.
        if (settings.Depth < ValidationDepth.Full)
        {
            return ValidationResult.Success();
        }

        // Index the OUTERMOST resource of the current scope, matching the root that resolve() indexes
        // (FhirSpecificFunctions.Resolve uses RootResource ?? Resource; here RootResource is always set,
        // so the fallback never fires). At contained scope RootResource is the containing parent, which
        // is what makes a contained resource's reference to a contained PEER (#id) resolve: the peers
        // live in the parent's contained pool, not the contained resource's own (FHIR forbids nested
        // contained, so that pool is always empty).
        var root = state.Scope.RootResource;

        // Bundle and Parameters are both containers whose entries carry independent resources that
        // reference each other by relative Type/id. Inside either, an unresolved relative reference is
        // a genuine error (the reference validator flags it); outside a container, a bare Type/id is an
        // external reference we must not touch.
        var rootIsContainer = IsContainer(state.Scope.RootResource) || IsContainer(state.Scope.Resource);

        var issues = new List<ValidationIssue>();
        CollectUnresolved(element, ReferenceIndex.Build(root), rootIsContainer, issues);

        return issues.Count > 0
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }

    private static bool IsContainer(IElement? resource) =>
        resource?.InstanceType is "Bundle" or "Parameters";

    private static void CollectUnresolved(
        IElement element,
        ReferenceIndex index,
        bool rootIsContainer,
        List<ValidationIssue> issues)
    {
        foreach (var child in element.Children())
        {
            // Pass the reference's own Location so the index scopes the fragment lookup to the
            // container that encloses it: a #id inside one Bundle.entry.resource resolves against
            // that entry's contained pool and never a sibling entry's. Locations are absolute
            // (rooted at the indexed resource), which is what makes one index enough for the whole
            // walk - the resolver no longer has to be re-chained at each nested-resource boundary.
            if (child.Name == "reference" && child.Value is string reference
                && IsLocalReference(reference, rootIsContainer)
                && index.Resolve(reference, child.Location) is null)
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "ref-resolve",
                    $"Local reference '{reference}' does not resolve within the resource",
                    child.Location));
            }

            CollectUnresolved(child, index, rootIsContainer, issues);
        }
    }

    private static bool IsLocalReference(string reference, bool rootIsContainer)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        if (reference.StartsWith('#'))
        {
            return reference.Length > 1;
        }

        if (!rootIsContainer)
        {
            return false;
        }

        if (reference.Contains("://", StringComparison.Ordinal)
            || reference.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == reference.Length - 1)
        {
            return false;
        }

        return IsResourceTypeToken(reference.AsSpan(0, slash));
    }

    private static bool IsResourceTypeToken(ReadOnlySpan<char> token)
    {
        if (token.Length == 0 || !char.IsUpper(token[0]))
        {
            return false;
        }

        foreach (var c in token)
        {
            if (!char.IsLetter(c))
            {
                return false;
            }
        }

        return true;
    }
}
