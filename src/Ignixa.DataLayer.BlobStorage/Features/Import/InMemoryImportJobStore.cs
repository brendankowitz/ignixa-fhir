// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.BlobStorage.Features.Import;

/// <summary>
/// In-memory implementation of <see cref="IImportJobStore"/>.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// </summary>
public partial class InMemoryImportJobStore : IImportJobStore
{
    private readonly ConcurrentDictionary<string, BulkImportJob> _jobs = new();
    private readonly ILogger<InMemoryImportJobStore> _logger;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Created import job {JobId} for tenant {TenantId}")]
        public static partial void CreatedImportJob(ILogger logger, string jobId, int tenantId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Updated import job {JobId} (Status: {Status})")]
        public static partial void UpdatedImportJob(ILogger logger, string jobId, string status);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} jobs for tenant {TenantId}")]
        public static partial void ListedJobsForTenant(ILogger logger, int count, int tenantId);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryImportJobStore"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public InMemoryImportJobStore(ILogger<InMemoryImportJobStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task CreateJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var key = GetJobKey(job.TenantId, job.JobId);

        if (!_jobs.TryAdd(key, job))
        {
            throw new InvalidOperationException($"Import job with ID '{job.JobId}' already exists for tenant {job.TenantId}");
        }

        Log.CreatedImportJob(_logger, job.JobId, job.TenantId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<BulkImportJob?> GetJobAsync(int tenantId, string jobId, CancellationToken cancellationToken)
    {
        var key = GetJobKey(tenantId, jobId);
        _jobs.TryGetValue(key, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task UpdateJobAsync(BulkImportJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        var key = GetJobKey(job.TenantId, job.JobId);

        if (!_jobs.ContainsKey(key))
        {
            throw new InvalidOperationException($"Import job with ID '{job.JobId}' does not exist for tenant {job.TenantId}");
        }

        _jobs[key] = job;

        Log.UpdatedImportJob(_logger, job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BulkImportJob>> ListJobsAsync(int tenantId, CancellationToken cancellationToken)
    {
        var jobs = _jobs.Values
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.CreateDate)
            .ToList();

        Log.ListedJobsForTenant(_logger, jobs.Count, tenantId);

        return Task.FromResult<IReadOnlyList<BulkImportJob>>(jobs);
    }

    /// <summary>
    /// Generates a composite key for tenant + job ID.
    /// </summary>
    private static string GetJobKey(int tenantId, string jobId) => $"{tenantId}:{jobId}";
}
