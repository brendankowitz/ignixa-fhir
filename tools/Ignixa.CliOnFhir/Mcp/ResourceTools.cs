// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>MCP tools that search and read resources in the folder this server was started on.</summary>
[McpServerToolType]
public sealed class ResourceTools(FolderSession session)
{
    [McpServerTool(Name = "search_fhir_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        FHIR search over the folder: finds resources of one type that match a standard FHIR search query.
        The query uses FHIR search syntax, for example 'family=Smith', 'birthdate=gt1980', 'subject=Patient/p1',
        'code=http://loinc.org|8867-4' or '_id=a,b'; parameters are ANDed, comma-separated values ORed.
        Returns the total number of matches, one page of resources in catalog order and the file location of each
        as evidence. Pages hold 'count' resources (default 10, maximum 50): when more exist, 'omitted' says how many
        and which 'offset' to use next. Responses are capped at 50 KB; if resources do not fit, fewer are returned
        and 'omitted' says so, so pass 'elements' to shrink them.
        Check 'issues' every time: a 'parameter-ignored' warning means that filter did NOT apply and the results are
        broader than you asked for; an 'error' issue (null facts) means the query was not answered, for example
        'search-not-supported' for _sort, _include and chained or reverse-chained parameters.
        Call folder_inspect first to see which resource types exist.
        """)]
    public ToolEnvelope<SearchFacts?> SearchFhirResources(
        [Description("Resource type to search, for example 'Observation'.")] string resourceType,
        [Description("FHIR search query string without the leading '?', for example 'code=http://loinc.org|8867-4&date=ge2020'. Omit to match every resource of the type.")] string? query = null,
        [Description("Resources per page. Default 10, maximum 50. Overrides '_count' in the query.")] int? count = null,
        [Description("Number of matches to skip, in catalog order. Default 0. Use the offset given in 'omitted' to read the next page.")] int offset = 0,
        [Description("Pass 'count' to get only the total number of matches and no resources.")] string? summary = null,
        [Description("Comma-separated top-level elements to keep, for example 'code,valueQuantity'. resourceType, id and meta are always kept and trimmed resources are tagged SUBSETTED. Use the JSON property name (valueQuantity, not value[x]).")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ResourceSearcher.Search(session, new SearchRequest(resourceType, query, count, offset, summary, elements), cancellationToken);

    [McpServerTool(Name = "read_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        Reads resources by reference (for example 'Patient/123') straight from their files, each with its file
        location (file, line, byte offset) as evidence. A reference the folder does not hold is listed in 'absent'
        with what was checked. Pass 'elements' to return only some top-level elements and keep responses small
        (responses are capped at 50 KB and 'omitted' lists anything left out). Reads at most 50 references per call.
        """)]
    public ToolEnvelope<ReadFacts?> ReadResources(
        [Description("References to read, each 'ResourceType/id', for example ['Patient/123', 'Observation/931'].")] string[] references,
        [Description("Comma-separated top-level elements to keep, for example 'code,valueQuantity'. resourceType, id and meta are always kept and trimmed resources are tagged SUBSETTED.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ResourceFetcher.Read(session, references, elements, cancellationToken);
}
