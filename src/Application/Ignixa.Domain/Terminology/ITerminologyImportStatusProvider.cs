// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Reports whether a canonical's terminology content has been imported into the dedicated terminology
/// tables. Separate from <c>ITerminologyService</c> because it answers a storage question rather than a
/// terminology one: it is what lets a routing decorator pick between a SQL-backed service and a fallback
/// without depending on a concrete implementation.
/// </summary>
public interface ITerminologyImportStatusProvider
{
    /// <summary>
    /// Gets the terminology import status for a canonical URL (CodeSystem, ValueSet or ConceptMap).
    /// </summary>
    /// <param name="canonical">The canonical URL to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The import status, or null when the canonical is unknown or its recorded status is unreadable.
    /// </returns>
    Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
        CancellationToken cancellationToken);
}
