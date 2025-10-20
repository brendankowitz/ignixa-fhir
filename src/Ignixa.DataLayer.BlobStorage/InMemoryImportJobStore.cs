// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// In-memory implementation of <see cref="IImportJobStore"/>.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// </summary>
public class InMemoryImportJobStore : IImportJobStore
{
    private readonly ConcurrentDictionary<string, BulkImportJob> _jobs = new();
    private readonly ILogger<InMemoryImportJobStore> _logger;

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

        _logger.LogInformation("Created import job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);

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

        _logger.LogDebug("Updated import job {JobId} (Status: {Status})", job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BulkImportJob>> ListJobsAsync(int tenantId, CancellationToken cancellationToken)
    {
        var jobs = _jobs.Values
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.CreateDate)
            .ToList();

        _logger.LogDebug("Listed {Count} jobs for tenant {TenantId}", jobs.Count, tenantId);

        return Task.FromResult<IReadOnlyList<BulkImportJob>>(jobs);
    }

    /// <summary>
    /// Generates a composite key for tenant + job ID.
    /// </summary>
    private static string GetJobKey(int tenantId, string jobId) => $"{tenantId}:{jobId}";
}
