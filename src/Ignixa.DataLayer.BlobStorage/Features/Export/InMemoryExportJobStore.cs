using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.BlobStorage.Features.Export;

/// <summary>
/// In-memory implementation of <see cref="IExportJobStore"/>.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// </summary>
public partial class InMemoryExportJobStore : IExportJobStore
{
    private readonly ConcurrentDictionary<string, BulkExportJob> _jobs = new();
    private readonly ILogger<InMemoryExportJobStore> _logger;

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Created export job {JobId} for tenant {TenantId}")]
        public static partial void CreatedExportJob(ILogger logger, string jobId, int tenantId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Updated export job {JobId} (Status: {Status})")]
        public static partial void UpdatedExportJob(ILogger logger, string jobId, ExportJobStatus status);

        [LoggerMessage(Level = LogLevel.Information, Message = "Deleted export job {JobId}")]
        public static partial void DeletedExportJob(ILogger logger, string jobId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Attempted to delete non-existent job: {JobId}")]
        public static partial void AttemptedDeleteNonExistentJob(ILogger logger, string jobId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listed {Count} jobs for tenant {TenantId}")]
        public static partial void ListedJobsForTenant(ILogger logger, int count, int tenantId);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryExportJobStore"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public InMemoryExportJobStore(ILogger<InMemoryExportJobStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task CreateJobAsync(BulkExportJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_jobs.TryAdd(job.JobId, job))
        {
            throw new InvalidOperationException($"Export job with ID '{job.JobId}' already exists");
        }

        Log.CreatedExportJob(_logger, job.JobId, job.TenantId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<BulkExportJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task UpdateJobAsync(BulkExportJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_jobs.ContainsKey(job.JobId))
        {
            throw new InvalidOperationException($"Export job with ID '{job.JobId}' does not exist");
        }

        _jobs[job.JobId] = job;

        Log.UpdatedExportJob(_logger, job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryRemove(jobId, out var job))
        {
            Log.DeletedExportJob(_logger, jobId);
        }
        else
        {
            Log.AttemptedDeleteNonExistentJob(_logger, jobId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<List<BulkExportJob>> ListJobsForTenantAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        var jobs = _jobs.Values
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        Log.ListedJobsForTenant(_logger, jobs.Count, tenantId);

        return Task.FromResult(jobs);
    }
}
