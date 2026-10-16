// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Patients;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Views;

/// <summary>The resources of the view's type a run reads: the folder's, or one Patient compartment's.</summary>
/// <param name="Entries">The resources, in catalog order.</param>
/// <param name="BuiltAt">When the newest catalog or index the scope was worked out from finished building.</param>
internal sealed record ViewScope(IReadOnlyList<CatalogEntry> Entries, DateTimeOffset BuiltAt)
{
    /// <summary>
    /// <see cref="Resolve"/>, abandoned when <paramref name="cancellationToken"/> is cancelled. Working out the patient compartments
    /// for the first time (a cold cache builds every search index they need) cannot be interrupted, so it runs off the calling
    /// thread and is awaited; a timeout is then reported when it is due, and the build is left to finish and persist what it can
    /// (it is shared by the session, and the process ends with the verb).
    /// </summary>
    public static Task<ViewScope> ResolveAsync(FolderSession session, string resourceType, string? compartment, List<IssueItem> issues, List<OmittedItem> omitted, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return compartment is null
            ? Task.FromResult(Resolve(session, resourceType, compartment, issues, omitted))
            : Task.Run(() => Resolve(session, resourceType, compartment, issues, omitted), CancellationToken.None).WaitAsync(cancellationToken);
    }

    /// <summary>
    /// The resources of <paramref name="resourceType"/>, limited to the compartment <paramref name="compartment"/> names when
    /// given. A compartment is the per-patient one the session persists (<see cref="FolderSession.PatientCompartments"/>), so no
    /// search index is read for one patient. The issues that qualify the scope are added to <paramref name="issues"/> and
    /// <paramref name="omitted"/>.
    /// </summary>
    /// <exception cref="ToolRequestException">A compartment that is not <c>Patient/id</c>, or a Patient the folder does not hold.</exception>
    public static ViewScope Resolve(FolderSession session, string resourceType, string? compartment, List<IssueItem> issues, List<OmittedItem> omitted)
    {
        ArgumentNullException.ThrowIfNull(session);

        issues.AddRange(DuplicateIdIssues.For(session.Catalog, [resourceType]));
        if (compartment is null)
        {
            return new ViewScope([.. session.Catalog.Entries.Where(entry => entry.ResourceType == resourceType)], session.Catalog.BuiltAt);
        }

        string[] parts = compartment.Split('/');
        if (parts.Length != 2 || parts[0] != "Patient" || parts[1].Length == 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'compartment' must be 'Patient/id', for example 'Patient/123', not '{QueryEcho.Of(compartment)}'; only Patient compartments are kept per patient.");
        }

        PatientCompartment patient = PatientCompartment.Load(session, parts[1]);
        if (resourceType != "Patient" && !session.Compartments.ResourceTypes("Patient").Contains(resourceType, StringComparer.Ordinal))
        {
            issues.Add(new IssueItem("warning", CompartmentArgument.ExcludesTypeCode, $"{resourceType} resources are never in a Patient compartment (the {session.Schema.Version} CompartmentDefinition lists no parameter for {resourceType}), so the view reads nothing.", Location: null));
        }

        if (patient.UnevaluableIssue(FhirIds.Reference(patient.Patient)) is { } unevaluable)
        {
            issues.Add(unevaluable);
        }

        patient.AddDiagnostics(session, issues, omitted);
        return new ViewScope([.. patient.OfType(resourceType)], patient.IndexBuiltAt);
    }
}
