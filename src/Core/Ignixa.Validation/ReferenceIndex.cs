// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Validation;

/// <summary>
/// Scoped reference index that resolves FHIRPath <c>resolve()</c> targets within a single
/// resource root. Indexes contained resources (by <c>#id</c>) and, when the root is a Bundle,
/// sibling entry resources (by <c>fullUrl</c>, <c>Type/id</c>, and <c>Type/id/_history/versionId</c>).
/// </summary>
/// <remarks>
/// Built once per resource root (O(entries)); the closure injected as the FHIRPath element
/// resolver chains a contained-of-current scope to its parent scope. Mirrors the
/// contained + bundle resolution algorithm of Firely's <c>ScopedNode.BundledResources()</c> /
/// <c>ContainedResources()</c> without per-node parent pointers.
/// </remarks>
public sealed class ReferenceIndex
{
    private readonly Dictionary<string, IElement> _byContainedId;
    private readonly Dictionary<string, IElement> _byBundleKey;

    private ReferenceIndex(
        Dictionary<string, IElement> byContainedId,
        Dictionary<string, IElement> byBundleKey)
    {
        _byContainedId = byContainedId;
        _byBundleKey = byBundleKey;
    }

    /// <summary>
    /// Builds a reference index from a resource element, indexing its contained resources and,
    /// for a Bundle root, its entry resources.
    /// </summary>
    /// <param name="root">The resource element to index. Must not be null.</param>
    /// <returns>An index that resolves contained and intra-Bundle references for this root.</returns>
    public static ReferenceIndex Build(IElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var byContainedId = new Dictionary<string, IElement>(StringComparer.Ordinal);
        var byBundleKey = new Dictionary<string, IElement>(StringComparer.Ordinal);

        IndexContained(root, byContainedId);

        if (root.InstanceType == "Bundle")
        {
            IndexBundleEntries(root, byBundleKey);
        }
        else if (root.InstanceType == "Parameters")
        {
            IndexParametersEntries(root, byBundleKey);
        }

        return new ReferenceIndex(byContainedId, byBundleKey);
    }

    /// <summary>
    /// Resolves a reference to its target element within this index.
    /// </summary>
    /// <param name="reference">A fragment reference (<c>#id</c>) or a Bundle key (<c>fullUrl</c> / <c>Type/id</c>).</param>
    /// <returns>The matching element, or null when the reference is not present in this index.</returns>
    public IElement? Resolve(string reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }

        return reference.StartsWith('#')
            ? _byContainedId.GetValueOrDefault(reference[1..])
            : _byBundleKey.GetValueOrDefault(reference);
    }

    private static void IndexContained(IElement root, Dictionary<string, IElement> byContainedId)
    {
        foreach (var contained in root.Children("contained"))
        {
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
