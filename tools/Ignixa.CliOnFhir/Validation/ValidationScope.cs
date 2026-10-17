// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Validation;

/// <summary>
/// The resources a validation covers, in scope order: the folder (catalog order); a type, optionally narrowed by a search query
/// and a patient's compartment (search order, which is catalog order unless the query sorts); a patient's whole compartment
/// (the Patient, then its members type by type); or explicit references (request order).
/// </summary>
/// <param name="Facts">The scope as reported.</param>
/// <param name="Entries">The resources in scope.</param>
/// <param name="Issues">Notes on the scope: the query's warnings, unindexable resources a search could not consider.</param>
/// <param name="Absent">References asked for that the folder does not hold.</param>
/// <param name="BuiltAt">When the catalog, or the newest index the scope was read from, was built.</param>
internal sealed record ValidationScope(ValidationScopeFacts Facts, IReadOnlyList<CatalogEntry> Entries, IReadOnlyList<IssueItem> Issues, IReadOnlyList<AbsentItem> Absent, DateTimeOffset BuiltAt)
{
    /// <summary>The search's warnings left out of <see cref="Issues"/> (a search scope's capped <c>parameter-ignored</c> warnings).</summary>
    public IReadOnlyList<OmittedItem> Omitted { get; init; } = [];

    /// <exception cref="ToolRequestException">The scope arguments contradict each other, or name something that cannot be resolved.</exception>
    public static ValidationScope Resolve(FolderSession session, ValidateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Resolve(session, request.ResourceType, request.Query, request.Patient, request.References);
    }

    /// <summary>
    /// The resources a tool that reads a scope of the folder covers (<c>validate</c>, <c>deid</c>): the folder; a type, optionally
    /// narrowed by <paramref name="query"/> and <paramref name="patient"/>'s compartment; a patient's whole compartment; or exactly
    /// <paramref name="references"/>.
    /// </summary>
    /// <exception cref="ToolRequestException">The scope arguments contradict each other, or name something that cannot be resolved.</exception>
    public static ValidationScope Resolve(FolderSession session, string? resourceType, string? query, string? patient, IReadOnlyList<string>? references)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (references is { Count: > 0 })
        {
            if (resourceType is not null || query is not null || patient is not null)
            {
                throw ToolRequestException.Error("invalid-parameter", "'references' names the resources in scope exactly, so it cannot be combined with 'resourceType', 'query' or 'patient'.");
            }

            return References(session, references);
        }

        if (query is not null && resourceType is null)
        {
            throw ToolRequestException.Error("invalid-parameter", "'query' needs 'resourceType': a search query is over one resource type.");
        }

        if (patient is not null)
        {
            PatientIds.Validate(patient);
        }

        if (resourceType is not null)
        {
            return Search(session, resourceType, query, patient);
        }

        return patient is not null ? Compartment(session, patient) : Folder(session);
    }

    private static ValidationScope Folder(FolderSession session) =>
        new(new ValidationScopeFacts("folder", null, null, null, session.Catalog.Entries.Count), session.Catalog.Entries, [], [], session.Catalog.BuiltAt);

    private static ValidationScope Compartment(FolderSession session, string patient)
    {
        PatientCompartment compartment = PatientCompartment.Load(session, patient);
        List<CatalogEntry> entries = [.. compartment.Members];
        string reference = FhirIds.Reference(compartment.Patient);
        IssueItem[] issues = compartment.UnevaluableIssue(reference) is { } unevaluable ? [unevaluable] : [];
        DateTimeOffset builtAt = compartment.IndexBuiltAt;
        return new ValidationScope(new ValidationScopeFacts("patient", null, null, reference, entries.Count), entries, issues, [], builtAt);
    }

    private static ValidationScope Search(FolderSession session, string type, string? query, string? patient)
    {
        SearchMatch match = ResourceSearcher.MatchAll(session, new SearchRequest(type, query, Compartment: patient is null ? null : $"Patient/{patient}"));
        List<IssueItem> issues = [.. match.Issues];
        if (match.Parsed.CountOnly || match.Parsed.RequestedCount is not null || match.Parsed.Elements is not null || match.Parsed.Options.Include.Count > 0 || match.Parsed.Options.RevInclude.Count > 0)
        {
            issues.Add(new IssueItem("warning", "parameter-ignored", "_count, _summary, _elements, _include and _revinclude do not change which resources are validated: every match is in scope, and 'limit' and 'offset' page through them.", Location: null));
        }

        List<OmittedItem> diagnosticsOmitted = [];
        ResourceSearcher.AddDiagnostics(session, match.Source, issues, diagnosticsOmitted);
        issues.AddRange(diagnosticsOmitted.Select(item => new IssueItem("information", "diagnostics-omitted", $"{item.What} of the searched indexes are not listed.", Location: null)));

        List<CatalogEntry> entries = [.. match.Matches.Select(resource => resource.Entry)];
        string? patientReference = patient is null ? null : session.FindByName("Patient", patient) is { } owner ? FhirIds.Reference(owner) : $"Patient/{patient}";
        string kind = query is null ? (patient is null ? "type" : "patient") : "search";
        DateTimeOffset builtAt = match.Source.IndexesRead.Select(index => index.BuiltAt).DefaultIfEmpty(session.Catalog.BuiltAt).Max();
        List<OmittedItem> omitted = [.. match.Omitted];
        if (QueryEcho.IsCut(query))
        {
            omitted.Add(QueryEcho.Cut(query!, "facts.scope.query"));
        }

        return new ValidationScope(new ValidationScopeFacts(kind, type, query is null ? null : QueryEcho.Of(query), patientReference, entries.Count), entries, issues, [], builtAt) { Omitted = omitted };
    }

    private static ValidationScope References(FolderSession session, IReadOnlyList<string> references)
    {
        List<CatalogEntry> found = [];
        List<AbsentItem> absent = [];
        foreach ((string type, string id) in references.Select(reference => ResourceFetcher.ParseReference(session, reference)).Distinct())
        {
            if (session.FindByName(type, id) is not { } entry)
            {
                int ofType = session.Catalog.Entries.Count(candidate => candidate.ResourceType == type);
                absent.Add(new AbsentItem(FhirIds.Reference(type, id), $"catalog of {session.RootPath}: {ofType} {type} resources in {session.Catalog.FilesScanned} files", session.Catalog.BuiltAt));
            }
            else if (!found.Contains(entry))
            {
                found.Add(entry);
            }
        }

        return new ValidationScope(new ValidationScopeFacts("references", null, null, null, found.Count), found, [], absent, session.Catalog.BuiltAt);
    }
}
