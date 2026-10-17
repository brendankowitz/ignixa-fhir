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
public sealed class ResourceTools(FolderSession session, McpToolset toolset = McpToolset.All)
{
    private const string QuerySearchDescription = """
        FHIR search over one resource type. Every search parameter, _sort and _count included, goes inside 'query': 'code=http://loinc.org|4548-4&date=ge2020&_sort=-date'.
        - compartment=Patient/<id>: one patient, no lookup first.
        - columns: a table of FHIRPath values per match; .resolve() follows references: {"drug":"medication.ofType(Reference).resolve().code.coding.first().display","by":"requester.resolve().name.first().family"}
        - summary=count: how many.
        - MedicationRequest code= matches medicationCodeableConcept only; medicationReference: 'medication:Medication.code=...'.
        Check 'issues'. help: search, columns, paging, issues.
        """;

    /// <summary>The argument names <c>search_fhir_resources</c> takes, as the client sends them.</summary>
    private static readonly IReadOnlySet<string> SearchArguments = ToolArguments.Names(typeof(ResourceTools).GetMethod(nameof(SearchFhirResources))!);

    /// <summary>The argument names <c>plan_fhir_search</c> takes, as the client sends them.</summary>
    private static readonly IReadOnlySet<string> PlanArguments = ToolArguments.Names(typeof(ResourceTools).GetMethod(nameof(PlanFhirSearch))!);

    [McpServerTool(Name = "search_fhir_resources", ReadOnly = true, Idempotent = true)]
    [QueryToolset(Description = QuerySearchDescription, HiddenArguments = ["fhirpath", "maxBytes"], HiddenUnderAll = ["asOf"])]
    [Description("""
        FHIR search over one resource type. Every search parameter, _sort and _count included, goes inside 'query': 'code=http://loinc.org|4548-4&date=ge2020&_sort=-date'.
        - compartment=Patient/<id>: one patient, no lookup first.
        - columns: a table of FHIRPath values per match; .resolve() follows references: {"drug":"medication.ofType(Reference).resolve().code.coding.first().display","by":"requester.resolve().name.first().family"}
        - fhirpath: one value over a Bundle of all matches: 'entry.resource.value.ofType(Quantity).value.max()'.
        - summary=count: how many.
        - MedicationRequest code= matches medicationCodeableConcept only; medicationReference: 'medication:Medication.code=...'.
        Check 'issues'. help: search, columns, fhirpath, paging, issues.
        """)]
    public CallToolResult SearchFhirResources(
        string resourceType,
        string? query = null,
        int? count = null,
        int offset = 0,
        string? summary = null,
        string? elements = null,
        bool lenient = false,
        [Description("'Patient/123'")] string? compartment = null,
        int? maxBytes = null,
        Dictionary<string, string>? columns = null,
        string? fhirpath = null,
        [Description("latest, earliest or first: one match per patient.")] string? perPatient = null,
        string? asOf = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default)
    {
        var request = new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment, maxBytes, columns is null ? null : SearchColumn.From(columns), fhirpath, perPatient, asOf);
        if (toolset == McpToolset.Query && OutsideQueryToolset(request) is { } outside)
        {
            return ToolResults.Lean(EnvelopeAssembler.Failure<SearchFacts>(session, new IssueItem("error", "invalid-parameter", outside, Location: null), cancellationToken));
        }

        ToolEnvelope<SearchFacts?> envelope = ToolArguments.CheckSearch(session, SearchArguments, context?.Params?.Arguments?.Keys, lenient, () => ResourceSearcher.Search(session, request, cancellationToken), cancellationToken);
        return ToolResults.Lean(toolset == McpToolset.Query ? envelope with { Next = [.. envelope.Next.Where(call => !call.Args.ContainsKey("fhirpath"))] } : envelope);
    }

    /// <summary>Why <paramref name="request"/> needs <see cref="McpToolset.All"/>: the <c>fhirpath</c> mode, or <c>patientSearch()</c> in a column.</summary>
    private static string? OutsideQueryToolset(SearchRequest request)
    {
        if (request.FhirPath is not null && request.Columns is null)
        {
            return "'fhirpath' is offered with --toolset all. Use columns (a row per match), summary=count, or patient_cohort for population counts.";
        }

        return request.Columns?.Any(column => column.Path.Contains(PatientSearch.FunctionName + "(", StringComparison.Ordinal)) == true
            ? $"{PatientSearch.FunctionName}() is offered with --toolset all. patient_cohort counts populations in one call."
            : null;
    }

    [McpServerTool(Name = "plan_fhir_search", ReadOnly = true, Idempotent = true)]
    [UnlistedArguments("asOf")]
    [Description("""
        Explains a search without running it: how each parameter resolves, how many resources populate it, top codes. Takes search_fhir_resources' arguments; execute=true runs it too. help topic=plan.
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
        string? asOf = null,
        RequestContext<CallToolRequestParams>? context = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ToolArguments.CheckSearch(
            session,
            PlanArguments,
            context?.Params?.Arguments?.Keys,
            lenient,
            () => SearchPlanner.Plan(session, new SearchRequest(resourceType, query, count, offset, summary, elements, lenient, compartment, AsOf: asOf), execute, cancellationToken),
            cancellationToken));

    [McpServerTool(Name = "read_resources", ReadOnly = true, Idempotent = true)]
    [QueryToolset(HiddenArguments = ["maxBytes"])]
    [Description("""
        Reads up to 50 resources by reference ('Type/id') with file evidence; 'absent' lists those not held. 'elements' keeps responses small. Report and document text attachments come back decoded in facts.texts, document version chains in facts.versions.
        """)]
    public CallToolResult ReadResources(
        string[] references,
        string? elements = null,
        int? maxBytes = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ResourceFetcher.Read(session, references, elements, maxBytes, cancellationToken));
}
