// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.Serialization.SourceNodes;

/// <summary>
/// Carries the raw primitive <see cref="JsonValue"/> for an element, distinct from the
/// content/shadow object. Requested via <c>IElement.Meta&lt;JsonPrimitiveValueNode&gt;()</c>
/// when a caller must inspect the actual primitive value's JSON kind even though the element
/// also carries a <c>"_value"</c> shadow (extensions/id), where <c>Meta&lt;JsonNode&gt;()</c>
/// would return the shadow object instead of the value.
/// </summary>
/// <param name="Value">The primitive value node (never the shadow object).</param>
public sealed record JsonPrimitiveValueNode(JsonValue Value);
