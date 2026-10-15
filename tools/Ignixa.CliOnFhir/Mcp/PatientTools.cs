// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.ComponentModel;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Session;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ignixa.CliOnFhir.Mcp;

/// <summary>MCP tools that orient in the folder's patients and navigate one patient's context.</summary>
[McpServerToolType]
public sealed class PatientTools(FolderSession session)
{
    [McpServerTool(Name = "patient_list", ReadOnly = true, Idempotent = true)]
    [Description("""
        Lists the folder's patients so you can choose one: id, name (first official, else usual name), gender, birthDate,
        resourceCount (the Patient plus every resource in its Patient compartment, by the FHIR version's CompartmentDefinition),
        earliestDate/latestDate (each resource's main clinical date; 'facts.dateRule' gives the FHIRPath per type) and the
        domains present (problems, medications, allergies, encounters, labs, ...). Paged: 'count' (default 10, maximum 50)
        and 'offset'; when more exist, 'omitted' says how many and 'next' has the call for the next page. Sort by 'name'
        (default) or 'latest' (most recent clinical date first). Then call context_get_root with a patient id.
        """)]
    public CallToolResult PatientList(
        [Description("Patients per page. Default 10, maximum 50.")] int? count = null,
        [Description("Patients to skip, in sort order. Default 0.")] int offset = 0,
        [Description("'name' (default) or 'latest'.")] string? sort = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(PatientLister.List(session, count, offset, sort, cancellationToken));

    [McpServerTool(Name = "context_get_root", ReadOnly = true, Idempotent = true)]
    [Description("""
        The root digest of one patient's context, computed deterministically from the folder (no model involved): demographics,
        then one stub per domain, each with 'count', its first items (at most 5), a 'nodeKey' for context_expand and the 'rule'
        that decided it: problems (active Conditions by clinicalStatus), medications (active MedicationRequests and
        MedicationStatements, with the medication's display), allergies (every AllergyIntolerance; when there are none, 'absent'
        says what was checked, which is not the same as 'no known allergies'), encounters (the 5 most recent up to 'asOf') and
        labs (the 5 most frequent lab and vital-sign codes, each with its latest value, unit, date and a trend up/down/flat/n/a).
        'excluded' counts what a rule left out; 'window' gives the time bounds; 'otherTypes' counts compartment resources no stub
        covers. 'next' lists the context_expand calls to go deeper. Items are summaries: free text (narrative, notes,
        attachments) is never copied into them; read resources with context_expand or read_resources. Check 'issues': a
        'domain-rule-unsupported-version' warning means the rules were written for R4 and were not applied.
        """)]
    public CallToolResult ContextGetRoot(
        [Description("The Patient's id, for example from patient_list.")] string patientId,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ContextRoot.Get(session, patientId, cancellationToken));

    [McpServerTool(Name = "context_expand", ReadOnly = true, Idempotent = true)]
    [Description("""
        Opens one node of a patient's context, paged, returning summary items and the page's resources as they are in the folder
        (resources keep their urn:uuid references; 'facts.resolvedReferences' maps each to the Type/id it names). Node keys come
        from context_get_root, context_expand or context_find: 'problems', 'medications', 'allergies', 'encounters', 'labs'
        (lab codes with latest value and trend), 'problem/Condition-<id>' (the condition plus every medication whose
        reasonReference or reasonCode points at it; 'linkedBy' says which), 'encounter/Encounter-<id>' (the encounter plus every
        resource in its Encounter compartment) and 'labs/<system>|<code>' (the full series of one code, newest first). An unknown
        or malformed node key fails with an 'error' issue. Pages hold 'count' items (default 10, maximum 50) within a 50 KB
        response; 'omitted' and 'next' give the next offset. Pass 'elements' to trim the returned resources.
        """)]
    public CallToolResult ContextExpand(
        [Description("The Patient's id.")] string patientId,
        [Description("The node to open, for example 'problems' or 'labs/http://loinc.org|8302-2'.")] string nodeKey,
        [Description("Items per page. Default 10, maximum 50.")] int? count = null,
        [Description("Items to skip. Default 0; use the offset from 'next'.")] int offset = 0,
        [Description("Comma-separated top-level elements to keep in the returned resources, for example 'code,valueQuantity'.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ContextExpander.Expand(session, patientId, nodeKey, count, offset, elements, cancellationToken));

    [McpServerTool(Name = "context_find", ReadOnly = true, Idempotent = true)]
    [Description("""
        Finds resources in one patient's context by their main code (Condition.code, Observation.code, the medication,
        Encounter.type, ...). 'system|code' (for example 'http://snomed.info/sct|195967001') matches that code only; a bare
        token such as '195967001' or 'asthma' matches codes in any system first, then texts; a query with spaces matches texts
        only (case-insensitive substring of the code's text or a coding display). No semantic or embedding search. Returns
        the matching items (code matches first, 'matchedBy' says which), their distinct 'nodeKeys' for context_expand, and the
        page's resources. Paged like context_expand. When nothing matches, 'absent' says what was searched.
        """)]
    public CallToolResult ContextFind(
        [Description("The Patient's id.")] string patientId,
        [Description("A code ('system|code', '|code' for no system, or a bare code) or a text.")] string query,
        [Description("Matches per page. Default 10, maximum 50.")] int? count = null,
        [Description("Matches to skip. Default 0.")] int offset = 0,
        [Description("Comma-separated top-level elements to keep in the returned resources.")] string? elements = null,
        CancellationToken cancellationToken = default) =>
        ToolResults.From(ContextFinder.Find(session, patientId, query, count, offset, elements, cancellationToken));
}
