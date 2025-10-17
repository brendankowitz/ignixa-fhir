// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Factory for creating tenant-specific FHIR repository instances.
/// Implements caching to provide O(1) repository lookup after first access.
/// </summary>
public interface IFhirRepositoryFactory
{
    /// <summary>
    /// Gets a repository for the specified tenant.
    /// Creates and caches the repository on first access, returns cached instance on subsequent calls.
    /// </summary>
    /// <param name="tenantId">The tenant identifier (0, 1, 2, ...)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A tenant-specific IFhirRepository instance</returns>
    /// <exception cref="InvalidOperationException">Thrown if tenant does not exist or is inactive</exception>
    Task<IFhirRepository> GetRepositoryAsync(
        int tenantId,
        CancellationToken ct = default);
}
