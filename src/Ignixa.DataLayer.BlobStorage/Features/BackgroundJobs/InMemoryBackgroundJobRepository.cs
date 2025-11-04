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
/// Generic in-memory implementation of <see cref="IBackgroundJobRepository{T}"/>.
/// Supports multiple job types (import, export, validate, etc.) with unified storage.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// </summary>
/// <typeparam name="T">The strongly-typed job definition/input parameters.</typeparam>
public partial class InMemoryBackgroundJobRepository<T> : IBackgroundJobRepository<T> where T : class
{
    private readonly ConcurrentDictionary<string, BackgroundJob<T>> _jobs = new();
    private readonly ILogger<InMemoryBackgroundJobRepository<T>> _logger;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Created background job {JobId} for tenant {TenantId} (JobType: {JobType})")]
        public static partial void CreatedBackgroundJob(ILogger logger, string jobId, int tenantId, int jobType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Updated background job {JobId} (Status: {Status})")]
        public static partial void UpdatedBackgroundJob(ILogger logger, string jobId, string status);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} background jobs for tenant {TenantId}")]
        public static partial void ListedJobsForTenant(ILogger logger, int count, int tenantId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted background job {JobId}")]
        public static partial void DeletedBackgroundJob(ILogger logger, string jobId);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryBackgroundJobRepository{T}"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public InMemoryBackgroundJobRepository(ILogger<InMemoryBackgroundJobRepository<T>> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task CreateAsync(BackgroundJob<T> job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var key = GetJobKey(job.TenantId, job.JobId);

        if (!_jobs.TryAdd(key, job))
        {
            throw new InvalidOperationException($"Background job with ID '{job.JobId}' already exists for tenant {job.TenantId}");
        }

        Log.CreatedBackgroundJob(_logger, job.JobId, job.TenantId, job.JobType);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<BackgroundJob<T>?> GetAsync(int tenantId, string jobId, CancellationToken cancellationToken)
    {
        var key = GetJobKey(tenantId, jobId);
        _jobs.TryGetValue(key, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task UpdateAsync(BackgroundJob<T> job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var key = GetJobKey(job.TenantId, job.JobId);

        if (!_jobs.ContainsKey(key))
        {
            throw new InvalidOperationException($"Background job with ID '{job.JobId}' does not exist for tenant {job.TenantId}");
        }

        _jobs[key] = job;

        Log.UpdatedBackgroundJob(_logger, job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BackgroundJob<T>>> ListAsync(int tenantId, int? jobType = null, CancellationToken cancellationToken = default)
    {
        var jobs = _jobs.Values
            .Where(j => j.TenantId == tenantId && (jobType == null || j.JobType == jobType))
            .OrderByDescending(j => j.CreateDate)
            .ToList();

        Log.ListedJobsForTenant(_logger, jobs.Count, tenantId);

        return Task.FromResult<IReadOnlyList<BackgroundJob<T>>>(jobs);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(int tenantId, string jobId, CancellationToken cancellationToken)
    {
        var key = GetJobKey(tenantId, jobId);

        if (!_jobs.TryRemove(key, out _))
        {
            throw new InvalidOperationException($"Background job with ID '{jobId}' does not exist for tenant {tenantId}");
        }

        Log.DeletedBackgroundJob(_logger, jobId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a composite key for tenant + job ID.
    /// </summary>
    private static string GetJobKey(int tenantId, string jobId) => $"{tenantId}:{jobId}";
}
