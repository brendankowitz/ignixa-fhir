// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Ignixa.SourceNodeSerialization.ElementModel;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;

namespace Ignixa.SourceNodeSerialization.Extensions;

public static class SourceNodeExtensions
{
    // depth-first walk of the node tree
    public static IEnumerable<ISourceNode> Descendants(this ISourceNode node)
    {
        EnsureArg.IsNotNull(node, nameof(node));

        foreach (ISourceNode child in node.Children())
        {
            yield return child;
            foreach (ISourceNode g in child.Descendants())
            {
                yield return g;
            }
        }
    }

    public static IEnumerable<ITypedElement> Descendants(this ITypedElement node)
    {
        EnsureArg.IsNotNull(node, nameof(node));

        foreach (ITypedElement child in node.Children())
        {
            yield return child;
            foreach (ITypedElement g in child.Descendants())
            {
                yield return g;
            }
        }
    }

    public static bool RemoveExtension(this MetaJsonNode node, string url)
    {
        EnsureArg.IsNotNull(node, nameof(node));
        EnsureArg.IsNotNullOrWhiteSpace(url, nameof(url));

        if (node.Extensions != null)
        {
            ExtensionJsonNode extensionToRemove = node.Extensions.FirstOrDefault(e => string.Equals(e.Url, url, StringComparison.OrdinalIgnoreCase));
            if (extensionToRemove != null)
            {
                return node.Extensions.Remove(extensionToRemove);
            }
        }

        return false;
    }
}
