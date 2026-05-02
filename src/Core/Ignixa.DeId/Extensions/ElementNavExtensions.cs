// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;

namespace Ignixa.DeId.Extensions;

/// <summary>
/// Extension methods for navigating FHIR resource sub-structures (entries, contained resources).
/// </summary>
internal static class ElementNavExtensions
{
    public static IEnumerable<IElement> GetEntryResourceChildren(this IElement node)
    {
        return node?.Children(Constants.EntryNodeName)
                .Select(entry => entry?.Children(Constants.EntryResourceNodeName).FirstOrDefault())
                .Where(resource => resource is not null)!
            ?? [];
    }

    public static IEnumerable<IElement> GetContainedChildren(this IElement node)
    {
        return node?.Children(Constants.ContainedNodeName) ?? [];
    }

    public static IEnumerable<IElement> ResourceDescendantsWithoutSubResource(this IElement node)
    {
        foreach (var child in node.Children())
        {
            if (child.IsFhirResource())
            {
                continue;
            }

            yield return child;

            foreach (var n in child.ResourceDescendantsWithoutSubResource())
            {
                yield return n;
            }
        }
    }

    public static IEnumerable<IElement> SelfAndDescendantsWithoutSubResource(this IEnumerable<IElement> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var descendant in node.ResourceDescendantsWithoutSubResource())
            {
                yield return descendant;
            }
        }
    }
}
