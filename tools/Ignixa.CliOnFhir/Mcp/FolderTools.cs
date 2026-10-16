// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.CodeSystems;
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
        [Description("Load terminology first to report how codes resolve.")] bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await FolderInspector.InspectAsync(session, issueLimit ?? FolderInspector.DefaultIssueLimit, buildIndexes: false, withTerminology, cancellationToken));

    [McpServerTool(Name = "folder_code_systems", ReadOnly = true, Idempotent = true)]
    [Description("""
        Which code systems the folder's codings use (SNOMED CT, LOINC, RxNorm, ICD-10, ...), counted from the data with no terminology and no network: per system its URI as written, name, distinct codes, the elements that use it and the most frequent codes. Paged by usage.
        """)]
    public async Task<CallToolResult> FolderCodeSystems(
        [Description("Default 10, maximum 50.")] int? count = null,
        int offset = 0,
        [Description("Codes listed per system, default 5.")] int? top = null,
        [Description("Load terminology first to report how codes resolve.")] bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await CodeSystems.FolderCodeSystems.ListAsync(session, count, offset, top, withTerminology, cancellationToken));
}
