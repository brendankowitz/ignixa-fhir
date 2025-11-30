// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents an expression for the Patient $everything operation.
/// Retrieves all resources related to one or more patients, including:
/// - The patient resource(s) themselves
/// - All resources in the patient compartment
/// - Referenced resources (Practitioners, Organizations, Locations, Medications)
/// Supports date filtering, incremental updates (_since), and resource type filtering (_type).
/// </summary>
public class PatientEverythingExpression : Expression
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatientEverythingExpression"/> class for a single patient.
    /// </summary>
    /// <param name="patientId">The patient ID.</param>
    /// <param name="startDate">Optional lower bound for clinical dates.</param>
    /// <param name="endDate">Optional upper bound for clinical dates.</param>
    /// <param name="sinceDate">Optional filter for resources modified after this timestamp.</param>
    /// <param name="filteredResourceTypes">Optional set of resource types to limit search to.</param>
    /// <param name="includeReferencedResources">Whether to include referenced resources (Practitioners, Organizations, etc.).</param>
    public PatientEverythingExpression(
        string patientId,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        DateTimeOffset? sinceDate = null,
        ISet<string> filteredResourceTypes = null,
        bool includeReferencedResources = true)
        : this(new[] { patientId }, startDate, endDate, sinceDate, filteredResourceTypes, includeReferencedResources)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PatientEverythingExpression"/> class for multiple patients.
    /// Used for Group $everything operation.
    /// </summary>
    /// <param name="patientIds">The patient IDs.</param>
    /// <param name="startDate">Optional lower bound for clinical dates.</param>
    /// <param name="endDate">Optional upper bound for clinical dates.</param>
    /// <param name="sinceDate">Optional filter for resources modified after this timestamp.</param>
    /// <param name="filteredResourceTypes">Optional set of resource types to limit search to.</param>
    /// <param name="includeReferencedResources">Whether to include referenced resources (Practitioners, Organizations, etc.).</param>
    public PatientEverythingExpression(
        IReadOnlyList<string> patientIds,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        DateTimeOffset? sinceDate = null,
        ISet<string> filteredResourceTypes = null,
        bool includeReferencedResources = true)
    {
        EnsureArg.IsNotNull(patientIds, nameof(patientIds));
        EnsureArg.HasItems(patientIds, nameof(patientIds));

        PatientIds = patientIds;
        StartDate = startDate;
        EndDate = endDate;
        SinceDate = sinceDate;
        FilteredResourceTypes = filteredResourceTypes ?? new HashSet<string>();
        IncludeReferencedResources = includeReferencedResources;
    }

    /// <summary>
    /// The patient IDs.
    /// For single-patient $everything, this contains one ID.
    /// For Group $everything, this contains multiple patient IDs.
    /// </summary>
    public IReadOnlyList<string> PatientIds { get; }

    /// <summary>
    /// Optional lower bound for clinical dates.
    /// Filters resources to those with clinical dates >= StartDate.
    /// Applied to resources with date search parameters (Encounter.period, Observation.effective[x], etc.).
    /// </summary>
    public DateTimeOffset? StartDate { get; }

    /// <summary>
    /// Optional upper bound for clinical dates.
    /// Filters resources to those with clinical dates <= EndDate.
    /// Applied to resources with date search parameters (Encounter.period, Observation.effective[x], etc.).
    /// </summary>
    public DateTimeOffset? EndDate { get; }

    /// <summary>
    /// Optional filter for incremental updates.
    /// Only includes resources where meta.lastUpdated >= SinceDate.
    /// Used for delta queries to retrieve only changed resources.
    /// </summary>
    public DateTimeOffset? SinceDate { get; }

    /// <summary>
    /// Optional set of resource types to limit search to.
    /// If empty, all resource types in the patient compartment are included.
    /// Corresponds to the _type parameter in FHIR.
    /// </summary>
    public ISet<string> FilteredResourceTypes { get; }

    /// <summary>
    /// Whether to include referenced resources outside the patient compartment.
    /// When true, includes Practitioners, Organizations, Locations, and Medications
    /// referenced by resources in the patient compartment.
    /// Default: true (per FHIR spec, servers SHOULD include referenced resources).
    /// </summary>
    public bool IncludeReferencedResources { get; }

    public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        return visitor.VisitPatientEverything(this, context);
    }

    public override string ToString()
    {
        var patientIdsStr = PatientIds.Count == 1
            ? $"'{PatientIds[0]}'"
            : $"[{string.Join(", ", PatientIds.Select(id => $"'{id}'"))}]";

        var filters = new List<string>();
        if (StartDate.HasValue)
        {
            filters.Add($"start={StartDate:yyyy-MM-dd}");
        }

        if (EndDate.HasValue)
        {
            filters.Add($"end={EndDate:yyyy-MM-dd}");
        }

        if (SinceDate.HasValue)
        {
            filters.Add($"_since={SinceDate:o}");
        }

        if (FilteredResourceTypes.Count > 0)
        {
            filters.Add($"_type={string.Join(",", FilteredResourceTypes)}");
        }

        var filtersStr = filters.Count > 0 ? $" {string.Join(" ", filters)}" : string.Empty;

        return $"(PatientEverything {patientIdsStr}{filtersStr})";
    }

    public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
    {
        hashCode.Add(typeof(PatientEverythingExpression));
        hashCode.Add(PatientIds.Count);
        hashCode.Add(StartDate.HasValue);
        hashCode.Add(EndDate.HasValue);
        hashCode.Add(SinceDate.HasValue);
        hashCode.Add(IncludeReferencedResources);
    }

    public override bool ValueInsensitiveEquals(Expression other)
    {
        return other is PatientEverythingExpression everythingExpr &&
               everythingExpr.PatientIds.Count == PatientIds.Count &&
               everythingExpr.StartDate.HasValue == StartDate.HasValue &&
               everythingExpr.EndDate.HasValue == EndDate.HasValue &&
               everythingExpr.SinceDate.HasValue == SinceDate.HasValue &&
               everythingExpr.IncludeReferencedResources == IncludeReferencedResources;
    }
}
