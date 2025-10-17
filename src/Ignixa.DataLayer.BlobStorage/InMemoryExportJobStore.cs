using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// In-memory implementation of <see cref="IExportJobStore"/>.
/// Suitable for development and testing. Production should use SQL Server or similar.
/// </summary>
public class InMemoryExportJobStore : IExportJobStore
{
    private readonly ConcurrentDictionary<string, BulkExportJob> _jobs = new();
    private readonly ILogger<InMemoryExportJobStore> _logger;

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

        _logger.LogInformation("Created export job {JobId} for tenant {TenantId}", job.JobId, job.TenantId);

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

        _logger.LogDebug("Updated export job {JobId} (Status: {Status})", job.JobId, job.Status);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryRemove(jobId, out var job))
        {
            _logger.LogInformation("Deleted export job {JobId}", jobId);
        }
        else
        {
            _logger.LogWarning("Attempted to delete non-existent job: {JobId}", jobId);
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

        _logger.LogDebug("Listed {Count} jobs for tenant {TenantId}", jobs.Count, tenantId);

        return Task.FromResult(jobs);
    }
}
