// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Sparky.Extensions.Schema;
using Sparky.Search.Indexing;

namespace Sparky.Application.Infrastructure;

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
    /// <param name="fhirVersion">FHIR version string (e.g., "4.0", "5.0", "3.0").</param>
    /// <returns>Schema provider for the specified version.</returns>
    IFhirSchemaProvider GetSchemaProvider(string fhirVersion);

    /// <summary>
    /// Gets the search indexer for the specified FHIR version.
    /// </summary>
    /// <param name="fhirVersion">FHIR version string (e.g., "4.0", "5.0", "3.0").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search indexer for the specified version.</returns>
    ValueTask<ISearchIndexer> GetSearchIndexerAsync(string fhirVersion, CancellationToken cancellationToken = default);
}
