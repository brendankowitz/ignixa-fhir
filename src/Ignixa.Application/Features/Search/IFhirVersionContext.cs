// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
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
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <returns>Schema provider for the specified version.</returns>
    IFhirSchemaProvider GetBaseSchemaProvider(FhirSpecification fhirVersion);

    /// <summary>
    /// Gets the schema provider for the specified FHIR version and tenant.
    /// When tenantId is provided, returns a composite provider that includes custom resource types from loaded packages.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <param name="tenantId">Tenant ID for custom resource type resolution (null for base provider only).</param>
    /// <returns>Schema provider for the specified version and tenant.</returns>
    IFhirSchemaProvider GetSchemaProvider(FhirSpecification fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Gets the search indexer for the specified FHIR version.
    /// Initializes synchronously using pre-generated search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <returns>Search indexer for the specified version.</returns>
    ISearchIndexer GetSearchIndexer(FhirSpecification fhirVersion);

    /// <summary>
    /// Gets the search indexer for the specified FHIR version and tenant.
    /// When tenantId is provided, returns an indexer that uses IG-provided search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <param name="tenantId">Tenant ID for IG-specific search parameters (null for base indexer only).</param>
    /// <returns>Search indexer for the specified version and tenant.</returns>
    ISearchIndexer GetSearchIndexer(FhirSpecification fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Gets the search parameter definition manager for the specified FHIR version.
    /// Initializes synchronously using pre-generated search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <returns>Search parameter definition manager for the specified version.</returns>
    ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirSpecification fhirVersion);

    /// <summary>
    /// Gets the search parameter definition manager for the specified FHIR version and tenant.
    /// When tenantId is provided, returns a composite manager that includes IG-provided search parameters.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <param name="tenantId">Tenant ID for IG-specific search parameters (null for base manager only).</param>
    /// <returns>Search parameter definition manager for the specified version and tenant.</returns>
    ISearchParameterDefinitionManager GetSearchParameterDefinitionManager(FhirSpecification fhirVersion, Nullable<int> tenantId);

    /// <summary>
    /// Gets the compartment definition manager for the specified FHIR version.
    /// Initializes synchronously using pre-generated compartment definitions.
    /// </summary>
    /// <param name="fhirVersion">FHIR version enum (e.g., FhirSpecification.R4).</param>
    /// <returns>Compartment definition manager for the specified version.</returns>
    ICompartmentDefinitionManager GetCompartmentDefinitionManager(FhirSpecification fhirVersion);
}
