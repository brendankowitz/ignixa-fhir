// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
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
        Overview of the folder of FHIR data this server was started on: resource types and counts,
        number of patients, files read, and problems found (invalid JSON, duplicate ids, unknown types).
        Call this first to orient yourself. Problems beyond the issue limit are counted in 'omitted'.
        'facts.terminology' reports, per code system the folder's codes use, how many distinct codes resolve, are inactive or
        are unknown to the loaded terminology; its status is 'not loaded' (nothing computed) until a terminology tool has
        loaded the store, or 'withTerminology' is true, which loads it first (the configured packages, else the FHIR
        version's core package, downloaded from packages.fhir.org unless stored or --offline).
        """)]
    public async Task<CallToolResult> FolderInspect(
        [Description("Maximum number of individual problems to list (default 20).")] int? issueLimit = null,
        [Description("Load the terminology store first so 'facts.terminology' is computed (default false).")] bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await FolderInspector.InspectAsync(session, issueLimit ?? FolderInspector.DefaultIssueLimit, buildIndexes: false, withTerminology, cancellationToken));
}
