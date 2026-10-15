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
        Overview of the folder of FHIR data this server was started on: resource types and counts,
        number of patients, files read, and problems found (invalid JSON, duplicate ids, unknown types).
        Call this first to orient yourself. Problems beyond the issue limit are counted in 'omitted'.
        'facts.codeSystems' lists the ten most used coding systems with names and distinct-code counts; folder_code_systems
        has the full inventory.
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

    [McpServerTool(Name = "folder_code_systems", ReadOnly = true, Idempotent = true)]
    [Description("""
        Which coding systems the folder's data uses (SNOMED CT, LOINC, RxNorm, ICD-10, HL7 v2/v3 tables, FHIR-defined
        systems, ...), with no terminology and no network: per system its URI as written, a well-known name and steward,
        distinct codes, codings, resources, codings per element (for example Observation.code versus
        Observation.component.code) and the most frequent codes as 'system|code' with counts ('top', default 5, maximum 50).
        Counted from every Coding in the resources' JSON, extensions and contained resources included ('facts.basis');
        values of FHIR code elements such as Patient.gender are listed separately under 'implicitCodes', not attributed to a
        system. Paged by usage: 'count' (default 10, maximum 50) and 'offset'. Hints, as information issues: a non-preferred
        identifier of a known system (an OID form, an alias URI), a URI that looks like a FHIR-defined system but is not its
        canonical, codings with a code but no system. URIs and codes are folder data; no display is ever shown (look codes up
        with terminology_lookup). When terminology is already loaded (or 'withTerminology' is true, which loads it and may
        download the core package unless --offline), each system also says whether it is loaded and how many codes resolve,
        are inactive or unknown.
        """)]
    public async Task<CallToolResult> FolderCodeSystems(
        [Description("Systems per page. Default 10, maximum 50.")] int? count = null,
        [Description("Systems to skip, in usage order. Default 0.")] int offset = 0,
        [Description("Most frequent codes listed per system. Default 5, maximum 50.")] int? top = null,
        [Description("Load the terminology store first so each system says how its codes resolve (default false).")] bool withTerminology = false,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(await CodeSystems.FolderCodeSystems.ListAsync(session, count, offset, top, withTerminology, cancellationToken));
}
