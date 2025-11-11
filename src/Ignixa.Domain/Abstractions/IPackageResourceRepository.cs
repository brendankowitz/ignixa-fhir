// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Repository for managing FHIR conformance resources extracted from NPM packages (IGs).
/// Supports multi-version package loading, semantic version resolution, and canonical URL lookups.
/// </summary>
public interface IPackageResourceRepository
{
    /// <summary>
    /// Stores a conformance resource from a FHIR NPM package.
    /// Idempotent: Upserts based on unique constraint (PackageId + PackageVersion + ResourceType + ResourceId).
    /// </summary>
    /// <param name="packageResource">The package resource to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertAsync(PackageResource packageResource, CancellationToken cancellationToken);

    /// <summary>
    /// Batch upserts multiple package resources from a single NPM package.
    /// Uses a single transaction for efficiency.
    /// </summary>
    /// <param name="packageResources">List of package resources to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task BatchUpsertAsync(IReadOnlyList<PackageResource> packageResources, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a conformance resource by exact canonical URL and version.
    /// Used for explicit version resolution (e.g., http://example.com/SD|1.0.0).
    /// </summary>
    /// <param name="canonical">Canonical URL of the conformance resource.</param>
    /// <param name="version">Business version (from resource.version field).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package resource if found, null otherwise.</returns>
    Task<PackageResource?> GetByCanonicalAsync(
        string canonical,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a conformance resource from a specific package version.
    /// Used for package-scoped resolution (e.g., from "hl7.fhir.us.core@5.0.1").
    /// </summary>
    /// <param name="packageId">NPM package identifier (e.g., "hl7.fhir.us.core").</param>
    /// <param name="packageVersion">NPM package version (e.g., "5.0.1").</param>
    /// <param name="canonical">Canonical URL of the conformance resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package resource if found, null otherwise.</returns>
    Task<PackageResource?> GetFromPackageAsync(
        string packageId,
        string packageVersion,
        string canonical,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the latest version of a conformance resource by canonical URL.
    /// Uses semantic versioning to determine "latest" (MAJOR.MINOR.PATCH).
    /// </summary>
    /// <param name="canonical">Canonical URL of the conformance resource.</param>
    /// <param name="resourceType">Optional: Filter by resource type (e.g., "StructureDefinition").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The latest version of the package resource if found, null otherwise.</returns>
    Task<PackageResource?> GetLatestByCanonicalAsync(
        string canonical,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all conformance resources from a specific package version.
    /// Used for package auditing and inspection.
    /// </summary>
    /// <param name="packageId">NPM package identifier (e.g., "hl7.fhir.us.core").</param>
    /// <param name="packageVersion">NPM package version (e.g., "5.0.1").</param>
    /// <param name="resourceType">Optional: Filter by resource type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of package resources in the specified package.</returns>
    Task<IReadOnlyList<PackageResource>> ListPackageResourcesAsync(
        string packageId,
        string packageVersion,
        string? resourceType = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all loaded packages with their versions.
    /// Used for package management UI and auditing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of distinct (PackageId, PackageVersion) tuples.</returns>
    Task<IReadOnlyList<(string PackageId, string PackageVersion)>> ListLoadedPackagesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates all resources from a specific package version (soft delete).
    /// Sets IsActive = false for all matching resources.
    /// </summary>
    /// <param name="packageId">NPM package identifier.</param>
    /// <param name="packageVersion">NPM package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of resources deactivated.</returns>
    Task<int> DeactivatePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reactivates all resources from a specific package version.
    /// Sets IsActive = true for all matching resources.
    /// </summary>
    /// <param name="packageId">NPM package identifier.</param>
    /// <param name="packageVersion">NPM package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of resources reactivated.</returns>
    Task<int> ReactivatePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Physically deletes all resources from a specific package version.
    /// Use with caution - this is irreversible.
    /// </summary>
    /// <param name="packageId">NPM package identifier.</param>
    /// <param name="packageVersion">NPM package version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of resources deleted.</returns>
    Task<int> DeletePackageAsync(
        string packageId,
        string packageVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets all active StructureDefinition resources matching the given canonical URL.
    /// Supports multiple versions of the same IG (e.g., US Core 5.0.1 and 6.1.0).
    /// Ordered by PackageVersion DESC so newest version is first.
    /// Used by composite provider for schema resolution.
    /// </summary>
    /// <param name="canonical">Canonical URL of the StructureDefinition.</param>
    /// <param name="fhirVersion">Optional: Filter by FHIR version (e.g., "4.0.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active StructureDefinitions matching the canonical URL, newest first.</returns>
    Task<IReadOnlyList<PackageResource>> GetStructureDefinitionsByCanonicalAsync(
        string canonical,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a package version has already been loaded (has active resources).
    /// Used to skip duplicate imports and enable idempotent package loading.
    /// </summary>
    /// <param name="packageId">NPM package identifier (e.g., "hl7.fhir.us.core").</param>
    /// <param name="packageVersion">NPM package version (e.g., "5.0.1").</param>
    /// <param name="tenantId">Tenant ID for multi-tenant queries (currently Phase 1 limitation - global repository).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if package version exists and has active resources, false otherwise.</returns>
    Task<bool> PackageVersionExistsAsync(
        string packageId,
        string packageVersion,
        int tenantId = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts custom resource types from loaded packages.
    /// Returns resource types referenced by ViewDefinition and custom StructureDefinitions.
    /// </summary>
    /// <param name="fhirVersion">Optional: Filter by FHIR version (e.g., "4.0.1"). If null, returns all versions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Set of custom resource type names defined in packages (e.g., "PatientView" from ViewDefinitions).</returns>
    Task<IReadOnlySet<string>> GetCustomResourceTypesAsync(
        string? fhirVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active SearchParameter resources for a specific resource type.
    /// Returns SearchParameters that apply to the given resource type (from base[] field).
    /// Used by composite search parameter manager to merge IG-provided parameters with base spec.
    /// Ordered by PackageVersion DESC so newest version is first.
    /// </summary>
    /// <param name="resourceType">Resource type name (e.g., "Patient", "Observation").</param>
    /// <param name="fhirVersion">Optional: Filter by FHIR version (e.g., "4.0.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active SearchParameters for the resource type, newest first.</returns>
    Task<IReadOnlyList<PackageResource>> GetSearchParametersByResourceTypeAsync(
        string resourceType,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a SearchParameter by its canonical URL.
    /// Supports multiple versions of the same IG (e.g., US Core 5.0.1 and 6.1.0).
    /// Ordered by PackageVersion DESC so newest version is first.
    /// Used by composite search parameter manager for exact URL lookups.
    /// </summary>
    /// <param name="canonical">Canonical URL of the SearchParameter.</param>
    /// <param name="fhirVersion">Optional: Filter by FHIR version (e.g., "4.0.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of active SearchParameters matching the canonical URL, newest first.</returns>
    Task<IReadOnlyList<PackageResource>> GetSearchParametersByCanonicalAsync(
        string canonical,
        string? fhirVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active SearchParameter resources across all loaded packages.
    /// Used for eager loading of search parameters at startup.
    /// Ordered by PackageVersion DESC so newest version is first.
    /// </summary>
    /// <param name="fhirVersion">Optional: Filter by FHIR version (e.g., "4.0.1").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all active SearchParameters, newest first.</returns>
    Task<IReadOnlyList<PackageResource>> GetAllSearchParametersAsync(
        string? fhirVersion = null,
        CancellationToken cancellationToken = default);
}
