// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Serialization.TestSupport;

/// <summary>
/// Test-only convenience for reaching the raw mutable JSON backing a <see cref="IMutableJsonNode"/>.
/// Not part of the public SDK surface — see <see cref="IMutableJsonNode"/> for why direct JSON
/// mutation is discouraged outside of tests.
/// </summary>
public static class MutableJsonNodeTestExtensions
{
    public static JsonObject MutableNode(this IMutableJsonNode resource) => resource.MutableNode;
}
