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
    /// <summary>The argument names <c>search_fhir_resources</c> takes, as the client sends them.</summary>
    private static readonly IReadOnlySet<string> SearchArguments = ToolArguments.Names(typeof(ResourceTools).GetMethod(nameof(SearchFhirResources))!);

    [McpServerTool(Name = "search_fhir_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        FHIR search over the folder for one resource type. Every search parameter, _sort and _count included, goes inside 'query' in FHIR syntax, for example 'code=http://loinc.org|4548-4&date=ge2020&_sort=-date'; chains, _has, _include and _revinclude work. An argument this tool does not define fails the call.
        - compartment=Patient/<id> scopes to one patient; it needs only the id, no lookup first.
        - 'columns' returns a table of FHIRPath values instead of resources. .resolve() follows references, urn:uuid and conditional ones too, so _include is rarely needed. A medication list, whichever medication[x] an order uses: columns={"date":"authoredOn","drug":"(medication.ofType(CodeableConcept) | medication.ofType(Reference).resolve().code).coding.first().display","prescriber":"requester.resolve().name.first().family"}
        - 'fhirpath' returns one value over a Bundle of every match: 'entry.count()', 'entry.resource.value.ofType(Quantity).value.max()'.
        - summary=count answers "how many"; _sort=-date with count=1 gives the latest.
        - perPatient=latest|earliest|first keeps one match per patient (by date, or first in _sort order) before count, columns, fhirpath; a cohort is one call: perPatient=latest with fhirpath 'entry.resource.where(value.ofType(Quantity).value >= 130 and subject.resolve().deceased.exists().not()).count()'.
        - MedicationRequest code= matches medicationCodeableConcept only; for medicationReference orders use 'medication:Medication.code=...'.
        Responses are held to about 12 KB (at most 16 KB); when cut, 'next' has the call for the rest. Check 'issues': parameter-ignored means a filter did not apply. help topics: search, columns, fhirpath, paging, references, issues.
        """)]
    public CallToolResult SearchFhirResources(
        [Description("For example 'Observation'.")] string resourceType,
        [Description("FHIR search query without '?'. Every search parameter goes here.")] string? query = null,
        [Description("Page size: resources default 10, maximum 50; rows default 50, maximum 200.")] int? count = null,
        int offset = 0,
        [Description("'count' for the total only.")] string? summary = null,
        [Description("Top-level elements to keep, comma-separated JSON names, for example 'code,valueQuantity'.")] string? elements = null,
        [Description("Ignore unknown parameter names with a warning instead of failing; can widen results.")] bool lenient = false,
        [Description("'Type/id', for example 'Patient/123'.")] string? compartment = null,
        [Description("Default 12288, maximum 16384; narrowing is better.")] int? maxBytes = null,
        [Description("Column name to FHIRPath, evaluated per match.")] Dictionary<string, string>? columns = null,
        [Description("One FHIRPath over a Bundle of every match.")] string? fhirpath = null,
        [Description("'latest', 'earliest' or 'first': one match per patient.")] string? perPatient = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment, maxBytes, columns is null ? null : SearchColumn.From(columns), fhirpath, perPatient);
        return ToolResults.From(ToolArguments.CheckSearch(session, SearchArguments, context?.Params?.Arguments?.Keys, lenient, () => ResourceSearcher.Search(session, request, cancellationToken), cancellationToken));
    }

    [McpServerTool(Name = "plan_fhir_search", ReadOnly = true, Idempotent = true)]
    [Description("""
        Explains a search without running it: how each parameter resolves, how many folder resources populate it, its top codes. Takes search_fhir_resources' arguments; execute=true runs it too. help topic=plan.
        """)]
    public CallToolResult PlanFhirSearch(
        string resourceType,
        string? query = null,
        string? compartment = null,
        bool lenient = false,
        bool execute = false,
        int? count = null,
        int offset = 0,
        string? summary = null,
        string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(SearchPlanner.Plan(session, new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment), execute, cancellationToken));

    [McpServerTool(Name = "read_resources", ReadOnly = true, Idempotent = true)]
    [Description("""
        Reads up to 50 resources by reference ('Type/id'), each with its file location as evidence; 'absent' lists those the folder does not hold. Pass 'elements' to keep responses small (about 12 KB by default).
        """)]
    public CallToolResult ReadResources(
        [Description("For example ['Patient/123', 'Observation/931'].")] string[] references,
        [Description("Top-level elements to keep, comma-separated.")] string? elements = null,
        [Description("Default 12288, maximum 16384.")] int? maxBytes = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ResourceFetcher.Read(session, references, elements, maxBytes, cancellationToken));
}
