// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ignixa.DataLayer.SqlEntityFramework.Entities;
using Ignixa.Search.Expressions;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Generates optimized EF Core queries for FHIR Patient $everything and Group $everything operations.
/// Uses a single-query approach with batched IN clauses for optimal performance.
/// Supports multiple patients (for Group $everything), date filtering, incremental updates, and referenced resources.
/// </summary>
public class PatientEverythingQueryGenerator
{
    private readonly FhirDbContext _context;
    private readonly CompartmentSearchQueryGenerator _compartmentQueryGenerator;
    private readonly ILogger<PatientEverythingQueryGenerator> _logger;

    // Resource types to include as "referenced resources" outside the patient compartment
    private static readonly HashSet<string> ReferencedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Practitioner",
        "Organization",
        "Location",
        "Medication"
    };

    public PatientEverythingQueryGenerator(
        FhirDbContext context,
        CompartmentSearchQueryGenerator compartmentQueryGenerator,
        ILogger<PatientEverythingQueryGenerator> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _compartmentQueryGenerator = compartmentQueryGenerator ?? throw new ArgumentNullException(nameof(compartmentQueryGenerator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates an optimized query for Patient $everything or Group $everything operations.
    /// Returns a single query that retrieves all related resources using UNION operations.
    /// </summary>
    /// <param name="expression">The PatientEverythingExpression containing patient IDs and filters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A queryable of matching resource surrogate IDs.</returns>
    public async Task<IQueryable<long>> GeneratePatientEverythingQueryAsync(
        PatientEverythingExpression expression,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(expression);

        var patientIds = expression.PatientIds;
        var patientCount = patientIds.Count;

        _logger.LogDebug(
            "Generating Patient $everything query for {PatientCount} patient(s): {PatientIds}",
            patientCount,
            patientCount <= 5 ? string.Join(", ", patientIds) : $"{string.Join(", ", patientIds.Take(5))}...");

        // Step 1: Get Patient resources themselves
        var patientSurrogateIds = await GetPatientSurrogateIdsAsync(patientIds, ct);

        // Step 2: Get compartment resources for all patients using optimized compartment search
        // This handles the multi-patient case by passing multiple IDs to the compartment generator
        var compartmentResourceIds = await GetCompartmentResourceIdsAsync(
            patientIds,
            expression.FilteredResourceTypes,
            ct);

        // Step 3: Apply date filters if specified
        if (expression.StartDate.HasValue || expression.EndDate.HasValue)
        {
            _logger.LogDebug(
                "Applying date filters: start={Start}, end={End}",
                expression.StartDate?.ToString("yyyy-MM-dd"),
                expression.EndDate?.ToString("yyyy-MM-dd"));

            compartmentResourceIds = ApplyDateFilters(
                compartmentResourceIds,
                expression.StartDate,
                expression.EndDate);
        }

        // Step 4: Apply _since filter if specified
        if (expression.SinceDate.HasValue)
        {
            _logger.LogDebug(
                "Applying _since filter: {Since}",
                expression.SinceDate.Value.ToString("o"));

            compartmentResourceIds = ApplySinceFilter(
                compartmentResourceIds,
                expression.SinceDate.Value);
        }

        // Step 5: Get referenced resources (Practitioners, Organizations, etc.) if requested
        IQueryable<long>? referencedResourceIds = null;
        if (expression.IncludeReferencedResources)
        {
            _logger.LogDebug("Including referenced resources (Practitioner, Organization, Location, Medication)");
            referencedResourceIds = GetReferencedResourceIds(compartmentResourceIds);
        }

        // Step 6: UNION all results
        // Patient resources + Compartment resources + Referenced resources
        var result = patientSurrogateIds.Union(compartmentResourceIds);
        if (referencedResourceIds != null)
        {
            result = result.Union(referencedResourceIds);
        }

        _logger.LogDebug("Patient $everything query generation complete");

        return result;
    }

    /// <summary>
    /// Gets the surrogate IDs for the specified patient IDs.
    /// </summary>
    private async Task<IQueryable<long>> GetPatientSurrogateIdsAsync(
        IReadOnlyList<string> patientIds,
        CancellationToken ct)
    {
        // Get Patient resource type ID
        var patientTypeId = await _context.ResourceTypes
            .Where(rt => rt.Name == "Patient")
            .Select(rt => rt.ResourceTypeId)
            .FirstOrDefaultAsync(ct);

        if (patientTypeId == 0)
        {
            _logger.LogWarning("Patient resource type not found in database");
            return Enumerable.Empty<long>().AsQueryable();
        }

        // Query for patient resources using batched IN clause
        // Use EF.Constant() to force inlining instead of JSON parameterization
        var query = from resource in _context.Resources
                    where resource.ResourceTypeId == patientTypeId
                        && EF.Constant(patientIds.ToList()).Contains(resource.ResourceId)
                        && resource.IsDeleted == false
                    select resource.ResourceSurrogateId;

        return query;
    }

    /// <summary>
    /// Gets compartment resource IDs for the specified patient IDs using the optimized compartment query generator.
    /// Supports multiple patients for Group $everything.
    /// </summary>
    private async Task<IQueryable<long>> GetCompartmentResourceIdsAsync(
        IReadOnlyList<string> patientIds,
        ISet<string>? filteredResourceTypes,
        CancellationToken ct)
    {
        // For single patient, use existing compartment search directly
        if (patientIds.Count == 1)
        {
            return await _compartmentQueryGenerator.GenerateCompartmentQueryAsync(
                compartmentType: "Patient",
                compartmentId: patientIds[0],
                resourceTypesToSearch: filteredResourceTypes,
                ct);
        }

        // For multiple patients (Group $everything), we need to generate UNION of compartment queries
        // or modify the compartment query to use IN clause for patient IDs
        // For MVP, we'll UNION multiple compartment queries
        // TODO: Optimize this by modifying CompartmentSearchQueryGenerator to accept multiple IDs
        IQueryable<long>? unionedQuery = null;

        foreach (var patientId in patientIds)
        {
            var compartmentQuery = await _compartmentQueryGenerator.GenerateCompartmentQueryAsync(
                compartmentType: "Patient",
                compartmentId: patientId,
                resourceTypesToSearch: filteredResourceTypes,
                ct);

            unionedQuery = unionedQuery == null
                ? compartmentQuery
                : unionedQuery.Union(compartmentQuery);
        }

        return unionedQuery ?? Enumerable.Empty<long>().AsQueryable();
    }

    /// <summary>
    /// Applies date filters (start/end) to compartment resources.
    /// Filters resources based on clinical date search parameters.
    /// </summary>
    private IQueryable<long> ApplyDateFilters(
        IQueryable<long> baseQuery,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate)
    {
        // Filter using DateTimeSearchParam table for resources with clinical dates
        // Resources with date parameters: Encounter.period, Observation.effective[x], Procedure.performed[x], etc.
        var dateFilteredQuery = from resourceId in baseQuery
                                join dateParam in _context.DateTimeSearchParams
                                    on resourceId equals dateParam.ResourceSurrogateId
                                where (startDate == null || dateParam.EndDateTime >= startDate.Value)
                                    && (endDate == null || dateParam.StartDateTime <= endDate.Value)
                                select resourceId;

        return dateFilteredQuery.Distinct();
    }

    /// <summary>
    /// Applies _since filter to resources based on meta.lastUpdated.
    /// Used for incremental updates to retrieve only changed resources.
    /// </summary>
    private IQueryable<long> ApplySinceFilter(
        IQueryable<long> baseQuery,
        DateTimeOffset sinceDate)
    {
        // Filter using Resource.LastUpdated column
        var sinceFilteredQuery = from resourceId in baseQuery
                                 join resource in _context.Resources
                                     on resourceId equals resource.ResourceSurrogateId
                                 where resource.LastUpdated >= sinceDate
                                 select resourceId;

        return sinceFilteredQuery;
    }

    /// <summary>
    /// Gets referenced resource IDs (Practitioners, Organizations, Locations, Medications)
    /// that are referenced by resources in the patient compartment.
    /// These resources are outside the patient compartment but are included per FHIR spec.
    /// </summary>
    private IQueryable<long> GetReferencedResourceIds(IQueryable<long> compartmentResourceIds)
    {
        // Get resource type IDs for referenced resource types
        var referencedTypeIds = from rt in _context.ResourceTypes
                                where EF.Constant(ReferencedResourceTypes.ToList()).Contains(rt.Name)
                                select rt.ResourceTypeId;

        // Query ReferenceSearchParam for outbound references from compartment resources
        // to Practitioner, Organization, Location, Medication resources
        var referencedIds = from refParam in _context.ReferenceSearchParams
                            where compartmentResourceIds.Contains(refParam.ResourceSurrogateId)
                                && referencedTypeIds.Contains(refParam.ReferenceResourceTypeId)
                            select refParam.ReferenceResourceSurrogateId;

        return referencedIds.Distinct();
    }
}
