// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Repository for managing System entities (code system URIs, identifier system URIs).
/// Provides normalization and deduplication of system URLs.
/// </summary>
public interface ISystemRepository
{
    /// <summary>
    /// Gets or creates a System entity for the given URI.
    /// Normalizes the URI and ensures only one entry exists per unique system.
    /// Thread-safe for concurrent imports.
    /// </summary>
    /// <param name="systemUri">The system URI (e.g., "http://loinc.org", "http://snomed.info/sct").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SystemId for the given URI (existing or newly created).</returns>
    Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the SystemId for an existing system URI, or null if not found.
    /// </summary>
    /// <param name="systemUri">The system URI to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The SystemId if found, null otherwise.</returns>
    Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken);
}
