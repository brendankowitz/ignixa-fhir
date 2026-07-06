// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Checks;

/// <summary>
/// Reference-integrity check (Full tier). Flags clearly-local references that fail to resolve
/// against the scoped resolver: fragment references (<c>#id</c>) and intra-Bundle relative
/// references (<c>Type/id</c>) inside a Bundle root.
/// </summary>
/// <remarks>
/// Scoped narrowly to avoid false positives: absolute URLs (<c>http(s)://</c>), <c>urn:</c>
/// references, and any reference is left alone when no resolver is configured. External and
/// logical references legitimately do not resolve locally and are never flagged.
/// </remarks>
public sealed class ReferenceResolutionCheck : IValidationCheck
{
    /// <summary>
    /// Validates that local references within the resource resolve against the scoped resolver.
    /// </summary>
    /// <param name="element">The resource root element to validate.</param>
    /// <param name="settings">Validation settings.</param>
    /// <param name="state">Current validation state carrying the scoped resolver.</param>
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

        var resolver = state.Scope.Resolver;
        if (resolver is null)
        {
            return ValidationResult.Success();
        }

        var rootIsBundle = state.Scope.RootResource?.InstanceType == "Bundle"
            || state.Scope.Resource?.InstanceType == "Bundle";

        var issues = new List<ValidationIssue>();
        CollectUnresolved(element, resolver, rootIsBundle, issues);

        return issues.Count > 0
            ? ValidationResult.Failure(issues)
            : ValidationResult.Success();
    }

    private static void CollectUnresolved(
        IElement element,
        Func<string, IElement?> resolver,
        bool rootIsBundle,
        List<ValidationIssue> issues)
    {
        foreach (var child in element.Children())
        {
            if (child.Name == "reference" && child.Value is string reference
                && IsLocalReference(reference, rootIsBundle)
                && resolver(reference) is null)
            {
                issues.Add(ValidationIssue.InvariantFailure(
                    "ref-resolve",
                    $"Local reference '{reference}' does not resolve within the resource",
                    child.Location));
            }

            // Crossing into a nested resource (contained[], Bundle.entry.resource,
            // Parameters.parameter.resource): fragment references inside it resolve against that
            // resource's own contained set, so re-scope the resolver to the nested resource (chained
            // to the outer resolver so intra-Bundle relative references still resolve against the
            // Bundle). Without this, a valid nested fragment (#payer -> that resource's contained) is
            // falsely flagged against the outer resource's contained.
            var childResolver = child.Name is "contained" or "resource"
                ? BuildNestedResolver(child, resolver)
                : resolver;

            CollectUnresolved(child, childResolver, rootIsBundle, issues);
        }
    }

    private static Func<string, IElement?> BuildNestedResolver(
        IElement nestedResource,
        Func<string, IElement?> outerResolver)
    {
        var index = ReferenceIndex.Build(nestedResource);

        // Chain to the outer resolver on a local miss. This is deliberately correct for BOTH boundary
        // kinds we descend through:
        //  - contained[]: a contained resource has no own `contained` (FHIR forbids nested contained),
        //    so its index is empty and a fragment (#id) must fall through to the CONTAINER's pool —
        //    contained resources legitimately reference each other via #id (FHIR contained-peer refs).
        //  - Bundle.entry.resource / Parameters.parameter.resource: an independent resource resolves
        //    fragments against its own `contained` (indexed here); relative Type/id refs fall through
        //    to the Bundle. The fall-through is harmless for its fragments because those containers
        //    carry no root-level `contained` to collide with.
        return reference => index.Resolve(reference) ?? outerResolver(reference);
    }

    private static bool IsLocalReference(string reference, bool rootIsBundle)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        if (reference.StartsWith('#'))
        {
            return reference.Length > 1;
        }

        if (!rootIsBundle)
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
