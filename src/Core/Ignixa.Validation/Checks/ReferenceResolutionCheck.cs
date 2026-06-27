// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Reference-integrity check (Full tier). Flags local references that fail to resolve against the
/// scoped resolver, restricted to the cases where non-resolution is unambiguously an error:
/// fragment references (<c>#id</c>) within their enclosing resource, and bundle-relative
/// references (<c>Type/id</c>) inside <c>document</c>/<c>message</c> bundles.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately conservative to avoid false positives. A <c>Type/id</c> reference is only required
/// to resolve within the bundle for <c>document</c> and <c>message</c> bundles; <c>searchset</c>,
/// <c>transaction</c>, <c>batch</c>, and <c>collection</c> bundles legitimately reference
/// server-resident resources, so their unresolved <c>Type/id</c> references are never flagged.
/// Absolute URLs (<c>http(s)://</c>), <c>urn:</c> references, and any reference are left alone when
/// no resolver is configured.
/// </para>
/// <para>
/// The walk does not descend across resource boundaries (contained resources, bundle entry
/// resources). Each resource owns its own reference scope and is validated under its own seeded
/// scope — contained resources via <see cref="ContainedResourceCheck"/> re-validation — so a
/// reference inside a nested resource is checked exactly once, against the resolver that actually
/// indexes its enclosing resource.
/// </para>
/// </remarks>
public sealed class ReferenceResolutionCheck : IValidationCheck
{
    private readonly IReadOnlySet<string>? _validResourceTypes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReferenceResolutionCheck"/> class.
    /// </summary>
    /// <param name="validResourceTypes">
    /// Known FHIR resource type names used to classify <c>Type/id</c> bundle references. When
    /// supplied, a reference is only treated as bundle-relative if its type token is a real resource
    /// type, avoiding false positives on tokens like <c>MyCustomType/id</c>. When null, a syntactic
    /// heuristic (capitalized alphabetic token) is used instead.
    /// </param>
    public ReferenceResolutionCheck(IReadOnlySet<string>? validResourceTypes = null)
    {
        _validResourceTypes = validResourceTypes;
    }

    /// <summary>
    /// Validates that local references within the resource resolve against the scoped resolver.
    /// </summary>
    /// <param name="element">The resource root element to validate.</param>
    /// <param name="settings">Validation settings. Unused: gating to the Full tier is handled by
    /// profile-check registration, not by depth inspection here.</param>
    /// <param name="state">Current validation state carrying the scoped resolver.</param>
    /// <returns>A validation result with an issue per unresolved local reference.</returns>
    public ValidationResult Validate(IElement element, ValidationSettings settings, ValidationState state)
    {
        var resolver = state.Scope.Resolver;
        if (resolver is null)
        {
            return ValidationResult.Success();
        }

        var checkBundleRelative = RequiresInternalBundleResolution(state.Scope.Resource);

        var issues = new List<ValidationIssue>();
        CollectUnresolved(element, resolver, checkBundleRelative, atRootScope: true, issues);

        return issues.Count > 0
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }

    private static bool RequiresInternalBundleResolution(IElement? root)
    {
        if (root?.InstanceType != "Bundle")
        {
            return false;
        }

        var typeChildren = root.Children("type");
        var type = typeChildren.Count == 0 ? null : typeChildren[0].Value?.ToString();
        return type is "document" or "message";
    }

    private void CollectUnresolved(
        IElement element,
        Func<string, IElement?> resolver,
        bool checkBundleRelative,
        bool atRootScope,
        List<ValidationIssue> issues)
    {
        foreach (var child in element.Children())
        {
            if (child.Name == "reference" && child.Value is string reference
                && ShouldCheck(reference, checkBundleRelative, atRootScope)
                && resolver(reference) is null)
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "ref-resolve",
                    $"Local reference '{reference}' does not resolve within the resource",
                    child.Location));
            }

            // A nested resource (contained, bundle entry resource) starts its own reference scope;
            // its fragment references resolve against its own contained set, not this resolver.
            var childAtRootScope = atRootScope && !IsNestedResource(child);
            CollectUnresolved(child, resolver, checkBundleRelative, childAtRootScope, issues);
        }
    }

    private bool ShouldCheck(string reference, bool checkBundleRelative, bool atRootScope)
    {
        return Classify(reference, checkBundleRelative) switch
        {
            // Fragments resolve within their enclosing resource — only verifiable while the walk is
            // still within the root resource's own scope (the seeded resolver indexes its contained).
            ReferenceKind.Fragment => atRootScope,
            ReferenceKind.BundleRelative => true,
            _ => false
        };
    }

    private ReferenceKind Classify(string reference, bool checkBundleRelative)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return ReferenceKind.None;
        }

        if (reference.StartsWith('#'))
        {
            return reference.Length > 1 ? ReferenceKind.Fragment : ReferenceKind.None;
        }

        if (!checkBundleRelative)
        {
            return ReferenceKind.None;
        }

        if (reference.Contains("://", StringComparison.Ordinal)
            || reference.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return ReferenceKind.None;
        }

        var slash = reference.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == reference.Length - 1)
        {
            return ReferenceKind.None;
        }

        return IsResourceTypeToken(reference.AsSpan(0, slash))
            ? ReferenceKind.BundleRelative
            : ReferenceKind.None;
    }

    private static bool IsNestedResource(IElement element)
        => (element.Type?.Info.IsResource ?? false)
            || element.Name is "contained" or "resource";

    private bool IsResourceTypeToken(ReadOnlySpan<char> token)
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

        // Prefer the real resource-type registry when available; fall back to the syntactic shape
        // (capitalized alphabetic token) for direct callers that don't supply one.
        return _validResourceTypes is not { Count: > 0 } || _validResourceTypes.Contains(token.ToString());
    }

    private enum ReferenceKind
    {
        None,
        Fragment,
        BundleRelative
    }
}
