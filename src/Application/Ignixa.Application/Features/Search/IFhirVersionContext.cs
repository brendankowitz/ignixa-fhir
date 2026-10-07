// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Specification;

namespace Ignixa.Application.Features.Search;

/// <summary>
/// Provides version-specific FHIR context (schema provider, search indexer, etc.).
/// Similar to HAPI FHIR's FhirContext pattern.
/// Caches instances per FHIR version for performance.
/// </summary>
public interface IFhirVersionContext
{
    /// <summary>
    /// Gets the schema provider for the specified FHIR version.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <returns>Schema provider for the specified version.</returns>
    IFhirSchemaProvider GetBaseSchemaProvider(FhirVersion fhirVersion);

    /// <summary>
    /// Gets the schema provider for the specified FHIR version and tenant.
    /// When tenantId is provided, returns a composite provider that includes custom resource types from loaded packages.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <param name="tenantId">Tenant ID for custom resource type resolution (null for base provider only).</param>
    /// <returns>Schema provider for the specified version and tenant.</returns>
    IFhirSchemaProvider GetSchemaProvider(FhirVersion fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Gets the search indexer for the specified FHIR version.
    /// Initializes synchronously using pre-generated search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <returns>Search indexer for the specified version.</returns>
    ISearchIndexer GetSearchIndexer(FhirVersion fhirVersion);

    /// <summary>
    /// Gets the search indexer for the specified FHIR version and tenant.
    /// When tenantId is provided, returns an indexer that uses IG-provided search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <param name="tenantId">Tenant ID for IG-specific search parameters (null for base indexer only).</param>
    /// <returns>Search indexer for the specified version and tenant.</returns>
    ISearchIndexer GetSearchIndexer(FhirVersion fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Acquires one immutable indexer/position pair for a complete write extraction.
    /// </summary>
    DefinitionsHandle GetDefinitionsHandle(FhirVersion fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Builds a handle without making it visible to writers.
    /// </summary>
    DefinitionsHandle CreateDefinitionsHandle(
        FhirVersion fhirVersion,
        Nullable<int> tenantId,
        long definitionsEventId);

    /// <summary>
    /// Publishes a newly refreshed write-extraction handle atomically.
    /// </summary>
    void PublishDefinitionsHandle(
        FhirVersion fhirVersion,
        Nullable<int> tenantId,
        DefinitionsHandle handle);

    void PublishDefinitionsHandle(FhirVersion fhirVersion, Nullable<int> tenantId, long definitionsEventId);

    /// <summary>
    /// Builds a complete tenant definition set from a detached conformance projection.
    /// </summary>
    ConformanceDefinitionsSnapshot CreateConformanceDefinitionsSnapshot(
        FhirVersion fhirVersion,
        int tenantId,
        ConformanceStateSnapshot stateSnapshot,
        long generation);

    /// <summary>
    /// Publishes a complete tenant definition set if it is newer than the currently visible generation.
    /// </summary>
    void PublishConformanceDefinitionsSnapshot(
        FhirVersion fhirVersion,
        int tenantId,
        ConformanceDefinitionsSnapshot snapshot);

    /// <summary>
    /// Gets the search parameter definition manager for the specified FHIR version.
    /// Initializes synchronously using pre-generated search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <returns>Search parameter definition manager for the specified version.</returns>
    ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirVersion fhirVersion);

    /// <summary>
    /// Gets the search parameter definition manager for the specified FHIR version and tenant.
    /// When tenantId is provided, returns a composite manager that includes IG-provided search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <param name="tenantId">Tenant ID for IG-specific search parameters (null for base manager only).</param>
    /// <returns>Search parameter definition manager for the specified version and tenant.</returns>
    ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirVersion fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Gets the search parameter definitions that are safe for query resolution.
    /// This is intentionally separate from <see cref="GetSearchParameterDefinitionManager(FhirVersion, Nullable{int})"/>,
    /// which serves extraction and therefore retains parameters while they are pending reindexing.
    /// </summary>
    ISearchParameterDefinitionManager GetSearchableSearchParameterDefinitionManager(
        FhirVersion fhirVersion,
        Nullable<int> tenantId,
        Func<bool>? includePartiallyIndexedSearchParameters = null);

    /// <summary>
    /// Gets the compartment definition manager for the specified FHIR version.
    /// Initializes synchronously using pre-generated compartment definitions.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirVersion.R4).</param>
    /// <returns>Compartment definition manager for the specified version.</returns>
    ICompartmentDefinitionManager GetCompartmentDefinitionManager(FhirVersion fhirVersion);

    /// <summary>
    /// Invalidates cached search parameter managers, forcing them to reload from ConformanceState.
    /// Should be called when packages are activated/deactivated.
    /// </summary>
    void InvalidateSearchParameterCaches();
}
