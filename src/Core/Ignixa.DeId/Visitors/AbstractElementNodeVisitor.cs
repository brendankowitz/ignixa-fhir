// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.DeId.Visitors;

/// <summary>
/// Base class for visitors that traverse FHIR element node trees with pre-visit and post-visit hooks.
/// </summary>
internal abstract class AbstractElementNodeVisitor
{
    public virtual bool Visit(ResourceJsonNode resource, IElement node)
    {
        return true;
    }

    public virtual void EndVisit(ResourceJsonNode resource, IElement node)
    {
    }
}
