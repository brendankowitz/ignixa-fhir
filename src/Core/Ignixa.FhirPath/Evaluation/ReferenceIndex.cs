// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation;

/// <summary>
/// Scoped reference index that resolves FHIRPath <c>resolve()</c> targets within a single
/// resource root. Indexes contained resources (by <c>#id</c>) and, when the root is a Bundle,
/// sibling entry resources (by <c>fullUrl</c>, <c>Type/id</c>, and <c>Type/id/_history/versionId</c>);
/// when the root is a Parameters resource, nested <c>parameter</c>/<c>part</c> resources (by
/// <c>Type/id</c> only - a Parameters entry has no <c>fullUrl</c>). A bare <c>#</c> resolves to the
/// root only when the current evaluation scope is itself one of the root's contained resources;
/// see <see cref="ResolveContainerScope"/>. Root-level and Bundle-entry-level scope both see an
/// empty result for bare <c>#</c>, matching Firely's <c>ScopedNode</c> (empirically verified against
/// Firely 5.13.1/6.0.1 and asserted by its own <c>ScopedNodeOnBaseTests</c>).
/// </summary>
/// <remarks>
/// Built once per resource root (O(entries)); the closure injected as the FHIRPath element
/// resolver chains a contained-of-current scope to its parent scope. Mirrors the
/// contained + bundle resolution algorithm of Firely's <c>ScopedNode.BundledResources()</c> /
/// <c>ContainedResources()</c> / <c>locateContainer()</c> without per-node parent pointers.
/// </remarks>
public sealed class ReferenceIndex
{
    private readonly IElement _root;
    private readonly Dictionary<string, IElement> _byContainedId;
    private readonly Dictionary<string, IElement> _byBundleKey;
    private readonly HashSet<string> _containedLocations;

    private ReferenceIndex(
        IElement root,
        Dictionary<string, IElement> byContainedId,
        Dictionary<string, IElement> byBundleKey,
        HashSet<string> containedLocations)
    {
        _root = root;
        _byContainedId = byContainedId;
        _byBundleKey = byBundleKey;
        _containedLocations = containedLocations;
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

        var byContainedId = new Dictionary<string, IElement>(StringComparer.Ordinal);
        var byBundleKey = new Dictionary<string, IElement>(StringComparer.Ordinal);
        var containedLocations = new HashSet<string>(StringComparer.Ordinal);

        IndexContained(root, byContainedId, containedLocations);

        if (root.InstanceType == "Bundle")
        {
            IndexBundleEntries(root, byBundleKey);
        }
        else if (root.InstanceType == "Parameters")
        {
            IndexParametersEntries(root, byBundleKey);
        }

        return new ReferenceIndex(root, byContainedId, byBundleKey, containedLocations);
    }

    /// <summary>
    /// Resolves a reference to its target element within this index. Bare <c>#</c> is never
    /// resolved here - deciding what it means needs the current evaluation scope, which this
    /// string-only overload does not have; see <see cref="ResolveContainerScope"/>.
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
            ? _byContainedId.GetValueOrDefault(reference[1..])
            : _byBundleKey.GetValueOrDefault(reference);
    }

    /// <summary>
    /// Resolves a bare <c>#</c> for the resource currently being evaluated. Returns the root only
    /// when <paramref name="currentResource"/> is itself one of the root's contained resources -
    /// Firely's ScopedNode returns the container from inside a contained resource's own scope, but
    /// null from root-level or Bundle-entry-level scope (its <c>ScopedNodeOnBaseTests</c> asserts
    /// <c>Resolve("#")</c> is null for both a Bundle and a Bundle entry resource). Membership is
    /// checked by <see cref="IElement.Location"/> rather than reference identity - callers such as
    /// <c>ContainedResourceCheck</c> re-derive the contained element via their own
    /// <c>Children("contained")</c> call, which returns a distinct wrapper instance for the same
    /// underlying node, so identity would never match; <c>Location</c> is a deterministic,
    /// instance-independent path (e.g. <c>Patient.contained[0]</c>) that is stable across separate
    /// wrappers of the same node. Checked against every contained child - including one with no
    /// <c>id</c>, which <see cref="Resolve"/>'s <c>#id</c> lookup can never reach.
    /// </summary>
    /// <param name="currentResource">The resource currently in scope (<c>%resource</c>), or null.</param>
    /// <returns>
    /// The root/container element when <paramref name="currentResource"/> is one of its contained
    /// resources, otherwise null.
    /// </returns>
    public IElement? ResolveContainerScope(IElement? currentResource) =>
        currentResource is not null && _containedLocations.Contains(currentResource.Location) ? _root : null;

    private static void IndexContained(
        IElement root,
        Dictionary<string, IElement> byContainedId,
        HashSet<string> containedLocations)
    {
        foreach (var contained in root.Children("contained"))
        {
            containedLocations.Add(contained.Location);

            var id = FirstChildValue(contained, "id");
            if (!string.IsNullOrEmpty(id))
            {
                byContainedId.TryAdd(id, contained);
            }
        }
    }

    private static void IndexBundleEntries(IElement bundle, Dictionary<string, IElement> byBundleKey)
    {
        foreach (var entry in bundle.Children("entry"))
        {
            var resourceChildren = entry.Children("resource");
            if (resourceChildren.Count == 0)
            {
                continue;
            }

            var resource = resourceChildren[0];

            var fullUrl = FirstChildValue(entry, "fullUrl");
            if (!string.IsNullOrEmpty(fullUrl))
            {
                byBundleKey.TryAdd(fullUrl, resource);
            }

            var id = FirstChildValue(resource, "id");
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var type = resource.InstanceType;
            byBundleKey.TryAdd($"{type}/{id}", resource);

            var versionId = MetaVersionId(resource);
            if (!string.IsNullOrEmpty(versionId))
            {
                byBundleKey.TryAdd($"{type}/{id}/_history/{versionId}", resource);
            }
        }
    }

    private static void IndexParametersEntries(IElement parameters, Dictionary<string, IElement> byBundleKey)
    {
        IndexParameterList(parameters.Children("parameter"), byBundleKey);
    }

    private static void IndexParameterList(
        IReadOnlyList<IElement> parameterEntries,
        Dictionary<string, IElement> byBundleKey)
    {
        foreach (var parameter in parameterEntries)
        {
            var resourceChildren = parameter.Children("resource");
            if (resourceChildren.Count > 0)
            {
                var resource = resourceChildren[0];
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
                IndexParameterList(parts, byBundleKey);
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
}
