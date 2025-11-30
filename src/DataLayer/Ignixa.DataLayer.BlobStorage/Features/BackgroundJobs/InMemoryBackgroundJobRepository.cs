// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;

/// <summary>
/// Generic in-memory implementation of <see cref="IBackgroundJobRepository{T}"/> (system-wide, partition 0).
/// Supports multiple job types (import, export, validate, etc.) with unified storage.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// Enforces tenant isolation based on deployment mode (Isolated = validate, Distributed = skip).
/// T is constrained to IJobDefinition for compile-time tenant access (no reflection required).
/// </summary>
/// <typeparam name="T">The strongly-typed job definition/input parameters that implement IJobDefinition.</typeparam>
public partial class InMemoryBackgroundJobRepository<T> : IBackgroundJobRepository<T> where T : class, IJobDefinition
{
    private readonly ConcurrentDictionary<string, BackgroundJob<T>> _jobs = new();
    private readonly ITenantConfigurationStore _tenantConfigStore;
    private readonly ILogger<InMemoryBackgroundJobRepository<T>> _logger;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Created background job {JobId} (JobType: {JobType})")]
        public static partial void CreatedBackgroundJob(ILogger logger, string jobId, int jobType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Updated background job {JobId} (Status: {Status})")]
        public static partial void UpdatedBackgroundJob(ILogger logger, string jobId, string status);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} background jobs")]
        public static partial void ListedJobs(ILogger logger, int count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted background job {JobId}")]
        public static partial void DeletedBackgroundJob(ILogger logger, string jobId);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryBackgroundJobRepository{T}"/> class.
    /// </summary>
    /// <param name="tenantConfigStore">Tenant configuration store for mode detection.</param>
    /// <param name="logger">Logger instance.</param>
    public InMemoryBackgroundJobRepository(
        ITenantConfigurationStore tenantConfigStore,
        ILogger<InMemoryBackgroundJobRepository<T>> logger)
    {
        _tenantConfigStore = tenantConfigStore ?? throw new ArgumentNullException(nameof(tenantConfigStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_jobs.TryAdd(job.JobId, job))
        {
            throw new InvalidOperationException($"Background job with ID '{job.JobId}' already exists");
        }

        Log.CreatedBackgroundJob(_logger, job.JobId, job.JobType);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<BackgroundJob<T>?> GetAsync(string jobId, int tenantId, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult<BackgroundJob<T>?>(null);
        }

        // Validate tenant ownership based on deployment mode
        // Isolated mode (multi-tenant): MUST validate since BackgroundJobs are in partition 0 (shared)
        // Distributed mode (single-customer sharding): Skip validation (all data belongs to same customer)
        if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
        {
            _logger.LogWarning("Job {JobId} access denied for tenant {TenantId}", jobId, tenantId);
            return Task.FromResult<BackgroundJob<T>?>(null); // Hide job existence from unauthorized tenants
        }

        return Task.FromResult<BackgroundJob<T>?>(job);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(BackgroundJob<T> job, int tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_jobs.ContainsKey(job.JobId))
        {
            throw new InvalidOperationException($"Background job with ID '{job.JobId}' does not exist");
        }

        // Validate tenant ownership based on deployment mode
        if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
        {
            _logger.LogWarning("Job {JobId} update denied for tenant {TenantId}", job.JobId, tenantId);
            throw new InvalidOperationException($"Not authorized to update job {job.JobId}");
        }

        _jobs[job.JobId] = job;

        Log.UpdatedBackgroundJob(_logger, job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int? jobType = null, CancellationToken cancellationToken = default)
    {
        var jobs = _jobs.Values
            .Where(j => jobType == null || j.JobType == jobType)
            .OrderByDescending(j => j.CreateDate)
            .ToList();

        Log.ListedJobs(_logger, jobs.Count);

        return Task.FromResult<IReadOnlyList<BackgroundJob<T>>>(jobs);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string jobId, int tenantId, CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            throw new InvalidOperationException($"Background job with ID '{jobId}' does not exist");
        }

        // Validate tenant ownership based on deployment mode
        if (ShouldValidateTenant() && !ValidateTenantOwnership(job, tenantId))
        {
            _logger.LogWarning("Job {JobId} delete denied for tenant {TenantId}", jobId, tenantId);
            throw new InvalidOperationException($"Not authorized to delete job {jobId}");
        }

        if (!_jobs.TryRemove(jobId, out _))
        {
            throw new InvalidOperationException($"Background job with ID '{jobId}' does not exist");
        }

        Log.DeletedBackgroundJob(_logger, jobId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if tenant validation should be enforced based on deployment mode.
    /// Isolated mode (multi-tenant) = validate (TRUE)
    /// Distributed mode (single-customer sharding) = skip validation (FALSE)
    /// </summary>
    private bool ShouldValidateTenant()
    {
        return _tenantConfigStore.Mode == TenantMode.Isolated;
    }

    /// <summary>
    /// Validates that the job belongs to the specified tenant by checking the TenantId in the Definition payload.
    /// Uses direct property access via IJobDefinition constraint (no reflection needed).
    /// </summary>
    private bool ValidateTenantOwnership(BackgroundJob<T> job, int tenantId)
    {
        // IJobDefinition constraint guarantees job.Definition.TenantId is always available
        return job.Definition.TenantId == tenantId;
    }
}
