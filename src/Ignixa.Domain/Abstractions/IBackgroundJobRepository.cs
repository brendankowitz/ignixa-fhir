// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Generic repository for background job storage (system-wide, partition 0).
/// Provides unified interface for managing DurableTask orchestration metadata.
/// TenantId is stored in the Definition/payload, not queried as a schema column.
/// Requires T to implement IJobDefinition for compile-time tenant access (no reflection needed).
/// </summary>
/// <typeparam name="T">The strongly-typed job definition/input parameters that implement IJobDefinition.</typeparam>
public interface IBackgroundJobRepository<T> where T : class, IJobDefinition
{
    /// <summary>
    /// Creates a new background job.
    /// </summary>
    /// <param name="job">The job to create. TenantId should be stored in Definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a background job by ID with tenant validation.
    /// BackgroundJobs are stored in partition 0 (global), so tenant ownership must be validated.
    /// </summary>
    /// <param name="jobId">Job ID.</param>
    /// <param name="tenantId">Tenant ID for authorization check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job if found and owned by the tenant, null otherwise.</returns>
    Task<BackgroundJob<T>?> GetAsync(string jobId, int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a background job with tenant validation.
    /// </summary>
    /// <param name="job">The job to update. Must include TenantId in Definition.</param>
    /// <param name="tenantId">Tenant ID for authorization check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateAsync(BackgroundJob<T> job, int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all background jobs (system-wide).
    /// </summary>
    /// <param name="jobType">Optional: Filter by job type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all jobs (filter by tenantId in Definition if needed).</returns>
    Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int? jobType = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a background job with tenant validation.
    /// </summary>
    /// <param name="jobId">Job ID.</param>
    /// <param name="tenantId">Tenant ID for authorization check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string jobId, int tenantId, CancellationToken cancellationToken);
}
