// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Builds an <see cref="ITerminologyImporter"/>.
/// <para>
/// <b>Why a factory rather than registering <see cref="ITerminologyImporter"/> directly.</b> The SQL Server
/// importer needs an <see cref="Ignixa.Domain.Abstractions.ISystemRepository"/>, which is built over a
/// reference-data cache that is produced asynchronously and must be the same instance the write path uses.
/// Autofac resolves synchronously, so a direct registration could only get that cache by blocking on it.
/// The one existing consumer of that cache — <c>PackageLoadedSearchParameterSyncHandler</c> — takes the
/// registry and awaits inside its handler for exactly this reason; this interface is that same pattern made
/// available to callers that must not reference a data layer.
/// </para>
/// <para>
/// <b>No tenant parameter.</b> Terminology is server-wide: the tables the importer writes live in the system
/// partition regardless of which tenant's package load triggered the import.
/// </para>
/// </summary>
public interface ITerminologyImporterFactory
{
    /// <summary>
    /// Creates an importer bound to the terminology store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ITerminologyImporter> CreateAsync(CancellationToken cancellationToken);
}
