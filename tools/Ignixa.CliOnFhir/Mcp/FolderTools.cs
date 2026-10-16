// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>MCP tools over the folder this server was started on.</summary>
[McpServerToolType]
public sealed class FolderTools(FolderSession session)
{
    [McpServerTool(Name = "folder_inspect", ReadOnly = true, Idempotent = true)]
    [Description("""
        Overview of the folder: resource types and counts, patients, the most used code systems, and problems found (invalid JSON, duplicate ids, unknown types). Not needed when you know the patient id and the type to search.
        """)]
    public async Task<CallToolResult> FolderInspect(
        [Description("Default 20.")] int? issueLimit = null,
        bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await FolderInspector.InspectAsync(session, issueLimit ?? FolderInspector.DefaultIssueLimit, buildIndexes: false, withTerminology, cancellationToken));

    [McpServerTool(Name = "folder_code_systems", ReadOnly = true, Idempotent = true)]
    [Description("""
        Which code systems the folder's codings use (SNOMED CT, LOINC, RxNorm, ICD-10, ...), counted from the data with no terminology and no network: per system its URI, alias and counts. top=n adds its top codes; detail=full adds elements and hints. Paged by usage.
        text=a|b lists instead the folder's codes whose display has a word starting a or b, with resource and patient counts.
        """)]
    public async Task<CallToolResult> FolderCodeSystems(
        [Description("Default 10, maximum 50.")] int? count = null,
        int offset = 0,
        [Description("Codes per system.")] int? top = null,
        bool withTerminology = false,
        string? detail = null,
        [Description("Words or alternatives, 'diabet|glyc'.")] string? text = null,
        [Description("With text.")] string? resourceType = null,
        [Description("With text.")] string? system = null,
        CancellationToken cancellationToken = default)
    {
        if (text is not null)
        {
            return ToolResults.From(CodeSystems.FolderCodeSystems.SearchText(session, text, resourceType, system, count, offset, cancellationToken));
        }

        if (resourceType is not null || system is not null)
        {
            return ToolResults.From(EnvelopeAssembler.Failure<object>(
                session,
                new IssueItem("error", "invalid-parameter", "'resourceType' and 'system' narrow a text search; give 'text' too.", Location: null),
                cancellationToken));
        }

        return await ListAsync(count, offset, top, withTerminology, detail, cancellationToken);
    }

    private async Task<CallToolResult> ListAsync(int? count, int offset, int? top, bool withTerminology, string? detail, CancellationToken cancellationToken) => detail switch
    {
        null or "compact" => ToolResults.From(await CodeSystems.FolderCodeSystems.ListCompactAsync(session, count, offset, top, withTerminology, cancellationToken)),
        "full" => ToolResults.From(await CodeSystems.FolderCodeSystems.ListAsync(session, count, offset, top, withTerminology, cancellationToken)),
        _ => ToolResults.From(EnvelopeAssembler.Failure<object>(
            session,
            new IssueItem("error", "invalid-parameter", $"'detail' must be 'compact' (the default) or 'full', not '{detail}'.", Location: null),
            cancellationToken)),
    };
}
