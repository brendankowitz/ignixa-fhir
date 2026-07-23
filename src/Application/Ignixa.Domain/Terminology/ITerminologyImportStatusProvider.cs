// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Reports whether a terminology canonical has been imported into the dedicated terminology tables.
/// </summary>
/// <remarks>
/// This seam exists to isolate the import-status probe that HybridTerminologyService routes on.
/// Routing ("use SQL when the canonical is imported, otherwise the fallback service") is the whole
/// behaviour of that class, and probing is the only thing it needs from the SQL side beyond the
/// ITerminologyService operations. Depending on this interface rather than the concrete
/// SqlTerminologyService makes the routing decision testable without constructing that service and
/// the repository factory, tenant store and DbContextOptions it pulls in behind it.
/// </remarks>
public interface ITerminologyImportStatusProvider
{
    /// <summary>
    /// Gets the terminology import status for a canonical URL.
    /// </summary>
    /// <param name="canonical">The canonical URL of the CodeSystem, ValueSet or ConceptMap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The import status, or null when the canonical is unknown to the store.</returns>
    Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
        CancellationToken cancellationToken);
}
