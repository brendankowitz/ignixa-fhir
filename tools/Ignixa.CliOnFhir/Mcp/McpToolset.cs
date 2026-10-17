// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>
/// Which tools, operations and modes the MCP server offers (<c>--toolset</c>). Every request of a model carries the tool menu,
/// so the default offers only what agents use to answer questions (ADR 2610); every CLI verb stays whatever the toolset.
/// </summary>
public enum McpToolset
{
    /// <summary>
    /// The tools marked <see cref="QueryToolsetAttribute"/>, without the arguments and operations it hides; a call to one of
    /// those fails naming <c>--toolset all</c>.
    /// </summary>
    Query,

    /// <summary>Every tool, operation and mode.</summary>
    All,
}
