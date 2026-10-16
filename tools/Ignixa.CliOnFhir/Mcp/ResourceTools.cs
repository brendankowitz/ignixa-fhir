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
        A resource whose id is not a FHIR id is named by its digest ('Type/~' and 16 hex digits) in evidence and issues, and
        '_id' and 'compartment' accept that digest; the returned JSON keeps the id as written.
        Call folder_inspect first to see which resource types exist, and plan_fhir_search to check a query (and see which
        codes the folder holds) before running it.
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

    [McpServerTool(Name = "plan_fhir_search", ReadOnly = true, Idempotent = true)]
    [Description("""
        Checks a FHIR search before running it (search_fhir_resources takes the same resourceType, query, compartment and
        lenient, and parses them the same way, so the plan and the search never disagree). For each query parameter:
        how it was understood ('outcome' compiled, known-miss, ignored), its modifiers and comparison prefixes, and the search
        parameter definitions it resolved to ('resolves': url, type, FHIRPath expression; for a chain each link in order,
        with the types it follows to). Each definition says how many resources of its type in this folder have a value for
        it ('populated' of 'indexed'); one no resource populates is a 'parameter-unpopulated' warning, since that filter
        cannot match anything here. Token parameters list their most common values ('topValues', at most 10, as
        'system|code' or '|code' with resource counts) so you can pick codes that exist. Sort keys and includes are listed
        too. Problems fail exactly as the search would: 'unknown-search-parameter' with a suggestion, 'search-not-supported',
        'digest-not-supported', 'invalid-search-request'. Values in 'topValues' are folder data (codes, never displays); a
        system or code not shaped as one is shown as a digest. Pass 'execute' true to also run the search and get its
        results in 'facts.results' with the search's evidence and paging ('count', 'offset', 'elements', 'summary' as for
        search_fhir_resources).
        """)]
    public CallToolResult PlanFhirSearch(
        [Description("Resource type to search, for example 'Observation'.")] string resourceType,
        [Description("FHIR search query string without the leading '?', for example 'code=http://loinc.org|8867-4&date=ge2020'.")] string? query = null,
        [Description("Compartment to search within, 'Type/id', for example 'Patient/123'.")] string? compartment = null,
        [Description("True to ignore unknown parameter names (reported as 'parameter-ignored') instead of failing. Default false.")] bool lenient = false,
        [Description("True to also run the search and return its results. Default false.")] bool execute = false,
        [Description("With execute: resources per page. Default 10, maximum 50.")] int? count = null,
        [Description("With execute: matches to skip. Default 0.")] int offset = 0,
        [Description("With execute: 'count' to get only the total.")] string? summary = null,
        [Description("With execute: comma-separated top-level elements to keep.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(SearchPlanner.Plan(session, new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment), execute, cancellationToken));

    [McpServerTool(Name = "read_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        Reads resources by reference (for example 'Patient/123', or 'Patient/~<16 hex digits>', the digest every tool names a
        resource whose id is not a FHIR id by) straight from their files, each with its file
        location (file, line, byte offset) as evidence. A reference the folder does not hold is listed in 'absent'
        with what was checked. Resources keep their Bundle references as written (urn:uuid:...); 'facts.resolvedReferences'
        maps each one shown to the Type/id it names. Pass 'elements' to return only some top-level elements and keep responses small
        (the whole response, i.e. the decoded tool-result text, is capped at 50 KB and 'omitted' lists anything left out). Reads at most 50 references per call.
        """)]
    public CallToolResult ReadResources(
        [Description("References to read, each 'ResourceType/id', for example ['Patient/123', 'Observation/931'].")] string[] references,
        [Description("Comma-separated top-level elements to keep, for example 'code,valueQuantity'. resourceType, id and meta are always kept and trimmed resources are tagged SUBSETTED.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ResourceFetcher.Read(session, references, elements, cancellationToken));
}
