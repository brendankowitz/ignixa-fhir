// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// Arguments of a tool offered only under <see cref="McpToolset.All"/> that its input schema leaves out, since that menu is at
/// its token budget (as <see cref="QueryToolsetAttribute.HiddenUnderAll"/> does for a tool of both toolsets). The tool binds them,
/// and its help topic names them.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class UnlistedArgumentsAttribute(params string[] names) : Attribute
{
    /// <summary>The arguments left out of the input schema.</summary>
    public IReadOnlyList<string> Names { get; } = names;
}
