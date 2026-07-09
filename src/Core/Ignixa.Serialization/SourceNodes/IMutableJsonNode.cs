// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using System.Text.Json.Nodes;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Advanced raw mutable JSON escape hatch.
/// Prefer typed facades, serialization helpers, or <c>ISourceNavigator.Meta&lt;JsonNode&gt;()</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IMutableJsonNode
{
    /// <summary>
    /// Gets the raw mutable JSON object backing this node.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    JsonObject MutableNode { get; }
}
