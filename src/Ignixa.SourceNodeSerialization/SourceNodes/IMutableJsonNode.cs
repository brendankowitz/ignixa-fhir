// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.SourceNodeSerialization.SourceNodes;

public interface IMutableJsonNode
{
    JsonObject MutableNode { get; }
}
