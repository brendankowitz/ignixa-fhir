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
    private const string QueryCodeSystemsDescription = """
        The folder's own codes whose display has a word starting a or b: text=a|b, with resource and patient counts, from the data with no terminology and no network. patient=<id> keeps one patient's codes. Without text: the code systems the codings use.
        """;

    [McpServerTool(Name = "folder_inspect", ReadOnly = true, Idempotent = true)]
    [Description("""
        Folder overview: resource types and counts, patients, top code systems, problems (invalid JSON, duplicate ids). Skip it when you know the patient id and the type.
        """)]
    public async Task<CallToolResult> FolderInspect(
        int? issueLimit = null,
        bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await FolderInspector.InspectAsync(session, issueLimit ?? FolderInspector.DefaultIssueLimit, buildIndexes: false, withTerminology, cancellationToken));

    [McpServerTool(Name = "folder_code_systems", ReadOnly = true, Idempotent = true)]
    [QueryToolset(Description = QueryCodeSystemsDescription, HiddenArguments = ["top", "withTerminology", "detail"])]
    [Description("""
        Code systems the folder's codings use (SNOMED CT, LOINC, RxNorm...), counted from the data with no terminology and no network. top=n adds top codes; detail=full more.
        text=a|b lists instead the folder's codes whose display has a word starting a or b, with counts; patient=<id> keeps one patient's.
        """)]
    public async Task<CallToolResult> FolderCodeSystems(
        int? count = null,
        int offset = 0,
        int? top = null,
        bool withTerminology = false,
        string? detail = null,
        [Description("'diabet|glyc'")] string? text = null,
        string? resourceType = null,
        string? system = null,
        string? patient = null,
        CancellationToken cancellationToken = default)
    {
        if (text is not null)
        {
            return ToolResults.From(CodeSystems.FolderCodeSystems.SearchText(session, text, resourceType, system, patient, count, offset, cancellationToken));
        }

        if (resourceType is not null || system is not null || patient is not null)
        {
            return ToolResults.From(EnvelopeAssembler.Failure<object>(
                session,
                new IssueItem("error", "invalid-parameter", "'resourceType', 'system' and 'patient' narrow a text search; give 'text' too.", Location: null),
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
