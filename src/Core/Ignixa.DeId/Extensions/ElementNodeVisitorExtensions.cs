// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.DeId.Visitors;

namespace Ignixa.DeId.Extensions;

/// <summary>
/// Extension methods for applying the visitor pattern to FHIR element node trees.
/// </summary>
internal static class ElementNodeVisitorExtensions
{
    public static void Accept(this IElement node, ResourceJsonNode resource, AbstractElementNodeVisitor visitor)
    {
        bool shouldVisitChild = visitor.Visit(resource, node);

        if (shouldVisitChild)
        {
            foreach (var child in node.Children())
            {
                child.Accept(resource, visitor);
            }
        }

        visitor.EndVisit(resource, node);
    }
}
