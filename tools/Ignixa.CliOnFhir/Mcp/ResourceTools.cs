// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
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
        Also supported: chains ('subject:Patient.name=Smith', or 'subject.gender=female' when only one target type has
        the parameter), reverse chains ('_has:Observation:subject:code=8867-4'), '_sort=-date,_id' and
        '_include'/'_revinclude' (with ':iterate'), for example '_include=Encounter:subject'.
        Pass 'compartment' (for example 'Patient/123') to search only resources in that compartment, as
        'Patient/123/Observation?...' does: by the FHIR version's CompartmentDefinition, an Observation is in a Patient's
        compartment when its subject or performer references that patient. 'facts.compartment' echoes it.
        Bundle references (urn:uuid fullUrls) are matched as the Type/id they name; returned resources keep the urn, and
        'facts.resolvedReferences' maps each one shown to its Type/id.
        Returns the total number of matches, one page of resources (in _sort order, else catalog order) and the file
        location of each as evidence. Pages hold 'count' resources (default 10, maximum 50): when more exist, 'omitted'
        says how many and which 'offset' to use next. Included resources come back whole in 'included' (at most 50; more
        are listed in 'omitted'), are not counted in 'total' or 'returned', and have evidence too. The whole response (the decoded
        tool-result text) is capped at 50 KB; if resources do not fit, fewer are returned and 'omitted' says so (a resource
        too large to ever fit is named there), so pass 'elements' to shrink them.
        Parameter names are case-sensitive: an unknown name fails with 'unknown-search-parameter' and a suggestion, unless
        'lenient' is true. Check 'issues' every time: a 'parameter-ignored' warning means that parameter did NOT apply; an
        'error' issue (null facts) means the query was not answered, for example 'search-not-supported' for _text or _filter.
        Call folder_inspect first to see which resource types exist.
        """)]
    public CallToolResult SearchFhirResources(
        [Description("Resource type to search, for example 'Observation'.")] string resourceType,
        [Description("FHIR search query string without the leading '?', for example 'code=http://loinc.org|8867-4&date=ge2020'. Omit to match every resource of the type.")] string? query = null,
        [Description("Resources per page. Default 10, maximum 50. Overrides '_count' in the query.")] int? count = null,
        [Description("Number of matches to skip, in result order. Default 0. Use the offset given in 'omitted' to read the next page.")] int offset = 0,
        [Description("Pass 'count' to get only the total number of matches and no resources.")] string? summary = null,
        [Description("Comma-separated top-level elements to keep, for example 'code,valueQuantity'. resourceType, id and meta are always kept and trimmed resources are tagged SUBSETTED. Use the JSON property name (valueQuantity, not value[x]). Included resources are not trimmed.")] string? elements = null,
        [Description("True to ignore parameter names the search does not recognise (each reported as a 'parameter-ignored' warning) instead of failing. This can widen the results. Default false.")] bool lenient = false,
        [Description("Compartment to search within, 'Type/id', for example 'Patient/123'. Compartment types: Patient, Encounter, Practitioner, RelatedPerson, Device. A type that is never in such a compartment matches nothing, with a 'compartment-excludes-type' warning.")] string? compartment = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ResourceSearcher.Search(session, new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment), cancellationToken));

    [McpServerTool(Name = "read_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        Reads resources by reference (for example 'Patient/123') straight from their files, each with its file
        location (file, line, byte offset) as evidence. A reference the folder does not hold is listed in 'absent'
        with what was checked. Pass 'elements' to return only some top-level elements and keep responses small
        (the whole response, i.e. the decoded tool-result text, is capped at 50 KB and 'omitted' lists anything left out). Reads at most 50 references per call.
        """)]
    public CallToolResult ReadResources(
        [Description("References to read, each 'ResourceType/id', for example ['Patient/123', 'Observation/931'].")] string[] references,
        [Description("Comma-separated top-level elements to keep, for example 'code,valueQuantity'. resourceType, id and meta are always kept and trimmed resources are tagged SUBSETTED.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ResourceFetcher.Read(session, references, elements, cancellationToken));
}
