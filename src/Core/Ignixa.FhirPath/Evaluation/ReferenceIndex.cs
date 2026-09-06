// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Scoped reference index that resolves FHIRPath <c>resolve()</c> targets within a single
/// resource root. Indexes contained resources (by <c>#id</c>) and, when the root is a Bundle,
/// sibling entry resources (by <c>fullUrl</c>, <c>fullUrl/_history/versionId</c>, <c>Type/id</c>,
/// and <c>Type/id/_history/versionId</c>) so both relative and absolute versioned references
/// resolve in-bundle; when the root is a Parameters resource, nested <c>parameter</c>/<c>part</c> resources (by
/// <c>Type/id</c> only - a Parameters entry has no <c>fullUrl</c>). A bare <c>#</c> resolves to the
/// container only when the current evaluation scope is itself one of that container's contained
/// resources; see <see cref="ResolveContainerScope"/>. Root-level and Bundle-entry-level scope both
/// see an empty result for bare <c>#</c>, matching the <c>locateContainer</c> local function inside
/// Firely's <c>ScopedNodeExtensions.Resolve&lt;T&gt;</c> (verified against Firely 5.13.1 and 6.0.1,
/// 2026-08; see its own <c>ScopedNodeOnBaseTests</c>).
/// </summary>
/// <remarks>
/// <para>
/// Built once per resource root (O(entries)): walks the root's own <c>contained</c> children, and,
/// for a Bundle or Parameters root, every entry/parameter (and nested <c>part</c>) resource,
/// indexing each by the keys <see cref="Resolve(string, string?)"/> looks up. Mirrors the contained
/// + bundle resolution algorithm of Firely's <c>ScopedNode.BundledResources()</c> /
/// <c>ContainedResources()</c> and the <c>locateContainer</c> local function inside
/// <c>ScopedNodeExtensions.Resolve&lt;T&gt;</c>, without per-node parent pointers.
/// </para>
/// <para>
/// Bundle entry keys have two tiers: authored keys (each entry's own <c>fullUrl</c> and
/// <c>Type/id</c>) and derived keys synthesized from them (<c>fullUrl/_history/versionId</c> and
/// <c>Type/id/_history/versionId</c>). An authored key always wins over a derived key - even one
/// synthesized by a different entry - regardless of entry order: <see cref="IndexBundleEntries"/>
/// registers every entry's authored keys in a first pass and only then registers derived keys in a
/// second pass, so a derived key's <c>TryAdd</c> can never displace an authored key that pass 1
/// already claimed. This matters because a spec-invalid <c>fullUrl</c> that already embeds
/// <c>/_history/{versionId}</c> (forbidden by Bundle invariant bdl-8, but producible by a
/// non-conformant sender) is exactly the string another entry's derived key would synthesize;
/// two entries' authored keys colliding with each other, or two entries' derived keys colliding
/// with each other, remain first-wins by entry order - that ambiguity is inherent to the input,
/// not something this index resolves.
/// </para>
/// <para>
/// Containment isolation without a parent pointer: <see cref="IElement"/> has no parent link, so
/// each container boundary - the root, plus every <c>Bundle.entry.resource</c> and
/// <c>Parameters.parameter[.part].resource</c> - keys its own contained pool by that container's
/// absolute <see cref="IElement.Location"/> prefix. A <c>#frag</c> is resolved against the pool of
/// the longest indexed prefix that encloses the focus element, so a fragment inside one entry can
/// never see a sibling entry's contained resources. Per FHIR R4 references.html §2.3.0.8, resolution
/// stops at those <c>entry.resource</c>/<c>parameter.resource</c> boundaries.
/// </para>
/// </remarks>
public sealed class ReferenceIndex
{
    private readonly Dictionary<string, IElement> _rootContainedById;
    private readonly IReadOnlyList<ContainedScope> _nestedScopes;
    private readonly Dictionary<string, IElement> _byBundleKey;
    private readonly Dictionary<string, IElement> _containerByContainedLocation;

    private ReferenceIndex(
        Dictionary<string, IElement> rootContainedById,
        IReadOnlyList<ContainedScope> nestedScopes,
        Dictionary<string, IElement> byBundleKey,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        _rootContainedById = rootContainedById;
        _nestedScopes = nestedScopes;
        _byBundleKey = byBundleKey;
        _containerByContainedLocation = containerByContainedLocation;
    }

    /// <summary>
    /// Builds a reference index from a resource element, indexing its contained resources and,
    /// for a Bundle root, its entry resources.
    /// </summary>
    /// <param name="root">The resource element to index. Must not be null.</param>
    /// <returns>An index that resolves contained, intra-Bundle, and Parameters-entry references for this root.</returns>
    public static ReferenceIndex Build(IElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var byBundleKey = new Dictionary<string, IElement>(StringComparer.Ordinal);
        var containerByContainedLocation = new Dictionary<string, IElement>(StringComparer.Ordinal);
        var nestedScopes = new List<ContainedScope>();

        // Root scope: the root's own contained pool. Empty for a Bundle/Parameters container (neither
        // is a DomainResource, so neither has a `contained` element), non-empty for a DomainResource.
        var rootContainedById = IndexContained(root, containerByContainedLocation);

        if (root.InstanceType == "Bundle")
        {
            IndexBundleEntries(root, byBundleKey, nestedScopes, containerByContainedLocation);
        }
        else if (root.InstanceType == "Parameters")
        {
            IndexParametersEntries(root, byBundleKey, nestedScopes, containerByContainedLocation);
        }

        return new ReferenceIndex(rootContainedById, nestedScopes, byBundleKey, containerByContainedLocation);
    }

    /// <summary>
    /// Resolves a reference to its target element within this index. Bare <c>#</c> is never
    /// resolved here - deciding what it means needs the current evaluation scope, which this
    /// string-only overload does not have; see <see cref="ResolveContainerScope"/>. Fragment
    /// (<c>#id</c>) lookups use the ROOT container's contained pool only; callers that need
    /// entry-scoped fragment isolation must use <see cref="Resolve(string, string?)"/>.
    /// </summary>
    /// <param name="reference">A fragment reference (<c>#id</c>) or a Bundle/Parameters key (<c>fullUrl</c> / <c>Type/id</c>).</param>
    /// <returns>The matching element, or null when the reference is not present in this index.</returns>
    public IElement? Resolve(string reference)
    {
        if (string.IsNullOrEmpty(reference) || reference == "#")
        {
            return null;
        }

        return reference.StartsWith('#')
            ? _rootContainedById.GetValueOrDefault(reference[1..])
            : _byBundleKey.GetValueOrDefault(reference);
    }

    /// <summary>
    /// Resolves a reference to its target element, scoping a fragment (<c>#id</c>) lookup to the
    /// container that encloses <paramref name="focusLocation"/>. A fragment is resolved against the
    /// pool of the longest indexed container <see cref="IElement.Location"/> prefix that encloses
    /// the focus element (falling back to the root's own contained pool when no nested container
    /// encloses it), which enforces the FHIR containment rule that a fragment inside one
    /// <c>Bundle.entry.resource</c> / <c>Parameters.parameter.resource</c> never resolves against a
    /// sibling entry's contained resources. Bundle/Parameters entry keys (<c>fullUrl</c> /
    /// <c>Type/id</c>) are cross-entry by design and are resolved independently of the focus.
    /// </summary>
    /// <param name="reference">A fragment reference (<c>#id</c>) or a Bundle/Parameters key (<c>fullUrl</c> / <c>Type/id</c>).</param>
    /// <param name="focusLocation">The <see cref="IElement.Location"/> of the reference element being resolved, or null.</param>
    /// <returns>The matching element, or null when the reference is not present in the enclosing scope.</returns>
    public IElement? Resolve(string reference, string? focusLocation)
    {
        if (string.IsNullOrEmpty(reference) || reference == "#")
        {
            return null;
        }

        return reference.StartsWith('#')
            ? SelectContainedPool(focusLocation).GetValueOrDefault(reference[1..])
            : _byBundleKey.GetValueOrDefault(reference);
    }

    /// <summary>
    /// Resolves a bare <c>#</c> for the resource currently being evaluated. Returns that resource's
    /// container only when <paramref name="currentResource"/> is itself one of a container's
    /// contained resources - for a plain DomainResource this is the root; for a contained resource
    /// nested inside a <c>Bundle.entry.resource</c> / <c>Parameters.parameter.resource</c> it is
    /// that entry resource, not the Bundle/Parameters root (R4 references.html §2.3.0.8: "there is
    /// only one container resource"). Firely's <c>ScopedNodeExtensions.Resolve&lt;T&gt;</c> (via its
    /// local <c>locateContainer</c> function) returns the container from inside a contained
    /// resource's own scope, but null from root-level or Bundle-entry-level scope (verified against
    /// Firely 5.13.1 and 6.0.1, 2026-08; its own <c>ScopedNodeOnBaseTests</c> asserts
    /// <c>Resolve("#")</c> is null for both a Bundle and a Bundle entry resource). Membership is
    /// checked by <see cref="IElement.Location"/> rather than
    /// reference identity - callers such as <c>ContainedResourceCheck</c> re-derive the contained
    /// element via their own <c>Children("contained")</c> call, which returns a distinct wrapper
    /// instance for the same underlying node, so identity would never match; <c>Location</c> is a
    /// deterministic, instance-independent path (e.g. <c>Patient.contained[0]</c>) that is stable
    /// across separate wrappers of the same node. A null or empty <c>Location</c> is never a member,
    /// so a hand-rolled element with no location cannot falsely resolve bare <c>#</c>.
    /// </summary>
    /// <param name="currentResource">The resource currently in scope (<c>%resource</c>), or null.</param>
    /// <returns>
    /// The container element when <paramref name="currentResource"/> is one of its contained
    /// resources, otherwise null.
    /// </returns>
    public IElement? ResolveContainerScope(IElement? currentResource)
    {
        var location = currentResource?.Location;
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }

        return _containerByContainedLocation.GetValueOrDefault(location);
    }

    private Dictionary<string, IElement> SelectContainedPool(string? focusLocation)
    {
        if (string.IsNullOrEmpty(focusLocation))
        {
            return _rootContainedById;
        }

        Dictionary<string, IElement>? best = null;
        var bestLength = -1;

        foreach (var scope in _nestedScopes)
        {
            if (scope.Prefix.Length > bestLength && IsInScope(focusLocation, scope.Prefix))
            {
                best = scope.ById;
                bestLength = scope.Prefix.Length;
            }
        }

        return best ?? _rootContainedById;
    }

    private static bool IsInScope(string location, string prefix)
    {
        // An empty prefix must never behave as a wildcard that captures every focus element.
        if (prefix.Length == 0 || !location.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Guard the boundary so a container's prefix only encloses its own descendants, e.g.
        // "Bundle.entry[1].resourceX" (a sibling field, not a child) must not match the prefix
        // "Bundle.entry[1].resource". This is not reachable via bracket-indexed siblings from real
        // parsed Locations (e.g. entry[1] vs entry[10] already diverge at the digit inside the
        // brackets), but Resolve(reference, focusLocation) is public and its focusLocation
        // parameter is a plain string, not a re-derived IElement.Location - a caller is not
        // required to pass a genuine one.
        return location.Length == prefix.Length || location[prefix.Length] == '.';
    }

    private static Dictionary<string, IElement> IndexContained(
        IElement container,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        var byId = new Dictionary<string, IElement>(StringComparer.Ordinal);

        foreach (var contained in container.Children("contained"))
        {
            var location = contained.Location;
            if (!string.IsNullOrEmpty(location))
            {
                // WHY: bare '#' from inside a contained resource resolves to its container.
                containerByContainedLocation.TryAdd(location, container);
            }

            var id = FirstChildValue(contained, "id");
            if (!string.IsNullOrEmpty(id))
            {
                byId.TryAdd(id, contained);
            }
        }

        return byId;
    }

    private static void AddNestedContainer(
        IElement container,
        List<ContainedScope> nestedScopes,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        var byId = IndexContained(container, containerByContainedLocation);

        // Only a container with contained resources AND a usable Location prefix can scope a
        // fragment lookup; an empty prefix would match every focus element, so it is skipped.
        if (byId.Count > 0 && !string.IsNullOrEmpty(container.Location))
        {
            nestedScopes.Add(new ContainedScope(container.Location, byId));
        }
    }

    private static void IndexBundleEntries(
        IElement bundle,
        Dictionary<string, IElement> byBundleKey,
        List<ContainedScope> nestedScopes,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        // Pass 1: authored keys - each entry's own fullUrl and Type/id, first-wins among entries.
        // AddNestedContainer runs here, exactly once per entry with a resource. This also captures
        // what pass 2 needs (fullUrl, id, versionId, and the resource itself), so the bundle's
        // entries are enumerated once even though key registration happens in two passes.
        var entryKeys = new List<BundleEntryKeySource>();

        foreach (var entry in bundle.Children("entry"))
        {
            var resourceChildren = entry.Children("resource");
            if (resourceChildren.Count == 0)
            {
                continue;
            }

            var resource = resourceChildren[0];

            AddNestedContainer(resource, nestedScopes, containerByContainedLocation);

            var fullUrl = FirstChildValue(entry, "fullUrl");
            if (!string.IsNullOrEmpty(fullUrl))
            {
                byBundleKey.TryAdd(fullUrl, resource);
            }

            var id = FirstChildValue(resource, "id");
            if (!string.IsNullOrEmpty(id))
            {
                byBundleKey.TryAdd($"{resource.InstanceType}/{id}", resource);
            }

            entryKeys.Add(new BundleEntryKeySource(resource, fullUrl, id, MetaVersionId(resource)));
        }

        // Pass 2: derived keys (fullUrl/_history/versionId and Type/id/_history/versionId),
        // synthesized so absolute and relative versioned references resolve in-bundle without a
        // host round-trip - independent of the resource having an `id` for the fullUrl-based key. A
        // derived key is a string another entry could have authored verbatim: a spec-invalid
        // fullUrl that already embeds "/_history/{versionId}" (forbidden by Bundle invariant bdl-8,
        // but producible by a non-conformant sender) is exactly what another entry's derived key
        // would synthesize. Every authored key from pass 1 is already in byBundleKey before this
        // loop runs, so TryAdd can never let a derived key displace an authored one, regardless of
        // which entry was visited first. Two entries' derived keys colliding with each other remain
        // first-wins by entry order, same as two entries' authored keys colliding in pass 1.
        foreach (var keys in entryKeys)
        {
            // Neither derived key is meaningful without a versionId; skip both rather than
            // register a malformed key ending in "/_history/" (an empty-string interpolation).
            if (string.IsNullOrEmpty(keys.VersionId))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(keys.FullUrl))
            {
                byBundleKey.TryAdd($"{keys.FullUrl}/_history/{keys.VersionId}", keys.Resource);
            }

            if (!string.IsNullOrEmpty(keys.Id))
            {
                byBundleKey.TryAdd($"{keys.Resource.InstanceType}/{keys.Id}/_history/{keys.VersionId}", keys.Resource);
            }
        }
    }

    private static void IndexParametersEntries(
        IElement parameters,
        Dictionary<string, IElement> byBundleKey,
        List<ContainedScope> nestedScopes,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        IndexParameterList(parameters.Children("parameter"), byBundleKey, nestedScopes, containerByContainedLocation);
    }

    private static void IndexParameterList(
        IReadOnlyList<IElement> parameterEntries,
        Dictionary<string, IElement> byBundleKey,
        List<ContainedScope> nestedScopes,
        Dictionary<string, IElement> containerByContainedLocation)
    {
        foreach (var parameter in parameterEntries)
        {
            var resourceChildren = parameter.Children("resource");
            if (resourceChildren.Count > 0)
            {
                var resource = resourceChildren[0];

                AddNestedContainer(resource, nestedScopes, containerByContainedLocation);

                var id = FirstChildValue(resource, "id");
                if (!string.IsNullOrEmpty(id))
                {
                    byBundleKey.TryAdd($"{resource.InstanceType}/{id}", resource);
                }
            }

            // Parameters nest via parameter.part; a resource can live at any depth.
            var parts = parameter.Children("part");
            if (parts.Count > 0)
            {
                IndexParameterList(parts, byBundleKey, nestedScopes, containerByContainedLocation);
            }
        }
    }

    private static string? FirstChildValue(IElement element, string childName)
    {
        var children = element.Children(childName);
        return children.Count == 0 ? null : children[0].Value?.ToString();
    }

    private static string? MetaVersionId(IElement resource)
    {
        var meta = resource.Children("meta");
        return meta.Count == 0 ? null : FirstChildValue(meta[0], "versionId");
    }

    /// <summary>
    /// A single container boundary's contained pool, keyed for longest-prefix scope selection by the
    /// container's absolute <see cref="IElement.Location"/>.
    /// </summary>
    private sealed record ContainedScope(string Prefix, Dictionary<string, IElement> ById);

    /// <summary>
    /// Raw per-entry inputs captured by <see cref="IndexBundleEntries"/>'s authored-key pass and
    /// consumed by its derived-key pass to synthesize keys, so the bundle's entries are enumerated
    /// once even though authored and derived keys are registered in two separate passes. These are
    /// source values (the entry's own <c>fullUrl</c>, <c>id</c>, <c>meta.versionId</c>, and
    /// resource), not keys themselves - pass 2 builds the actual dictionary keys from them.
    /// </summary>
    private readonly record struct BundleEntryKeySource(IElement Resource, string? FullUrl, string? Id, string? VersionId);
}
