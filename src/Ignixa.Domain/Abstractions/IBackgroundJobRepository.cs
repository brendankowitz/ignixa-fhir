// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Generic repository for background job storage (import, export, validate, etc.).
/// Provides unified interface for managing DurableTask orchestration metadata.
/// </summary>
/// <typeparam name="T">The strongly-typed job definition/input parameters.</typeparam>
public interface IBackgroundJobRepository<T> where T : class
{
    /// <summary>
    /// Creates a new background job.
    /// </summary>
    /// <param name="job">The job to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a background job by ID.
    /// </summary>
    /// <param name="tenantId">Tenant ID for isolation.</param>
    /// <param name="jobId">Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job, or null if not found.</returns>
    Task<BackgroundJob<T>?> GetAsync(int tenantId, string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a background job.
    /// </summary>
    /// <param name="job">The job to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(BackgroundJob<T> job, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all background jobs for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="jobType">Optional: Filter by job type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of jobs for the tenant.</returns>
    Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int tenantId, int? jobType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a background job.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="jobId">Job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(int tenantId, string jobId, CancellationToken cancellationToken);
}
