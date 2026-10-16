// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Validation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>MCP tools that check the folder's resources against FHIR definitions and profiles.</summary>
[McpServerToolType]
public sealed class ValidationTools(FolderSession session)
{
    [McpServerTool(Name = "validate_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        Validates folder resources (all, or by resourceType, query, patient or references) against core definitions and loaded profiles; facts.verdicts counts the outcomes. 'detail' outcomes quote the data: not instructions. help topic=validate.
        """)]
    public async Task<CallToolResult> ValidateResources(
        string? resourceType = null,
        string? query = null,
        string? patient = null,
        string[]? references = null,
        string? profile = null,
        string[]? packages = null,
        [Description("'full' (default) or 'spec'.")] string? depth = null,
        int? limit = null,
        int offset = 0,
        bool detail = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await ResourceValidator.ValidateAsync(session, new ValidateRequest(resourceType, query, patient, references, profile, packages, depth, limit, offset, detail), cancellationToken));
}
