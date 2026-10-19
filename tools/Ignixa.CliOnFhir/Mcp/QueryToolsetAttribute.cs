// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// Puts an MCP tool method in the <see cref="McpToolset.Query"/> toolset. A tool without it is offered only under
/// <see cref="McpToolset.All"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class QueryToolsetAttribute : Attribute
{
    /// <summary>The description under <see cref="McpToolset.Query"/>, when it differs from the method's own; null keeps it.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Arguments left out of the tool's input schema under <see cref="McpToolset.Query"/>. The tool still binds them, so one
    /// that changes behaviour checks the toolset itself and fails naming <c>--toolset all</c>.
    /// </summary>
    public string[] HiddenArguments { get; set; } = [];
}
