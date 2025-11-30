// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Service for importing FHIR terminology resources (CodeSystem, ValueSet, ConceptMap) into SQL tables.
/// Extracts structured data from JSON resources for fast terminology operations.
/// </summary>
public interface ITerminologyImporter
{
    /// <summary>
    /// Imports a CodeSystem resource into TermCodeSystem and TermConcept tables.
    /// Extracts metadata, flattens hierarchy, and uses bulk insert for large CodeSystems.
    /// </summary>
    /// <param name="packageResource">The PackageResource containing CodeSystem JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with status and concept count.</returns>
    Task<TerminologyImportResult> ImportCodeSystemAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports a ValueSet resource into TermValueSet and TermValueSetExpansion tables.
    /// Computes expansion by resolving compose rules and CodeSystem references.
    /// </summary>
    /// <param name="packageResource">The PackageResource containing ValueSet JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with status and expansion count.</returns>
    Task<TerminologyImportResult> ImportValueSetAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports a ConceptMap resource into TermConceptMap and TermConceptMapElement tables.
    /// Extracts mapping elements (source → target) for $translate operations.
    /// </summary>
    /// <param name="packageResource">The PackageResource containing ConceptMap JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import result with status and mapping count.</returns>
    Task<TerminologyImportResult> ImportConceptMapAsync(
        int tenantId,
        PackageResource packageResource,
        CancellationToken cancellationToken);
}
