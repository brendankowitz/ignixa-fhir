using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Repository for managing bulk export job metadata.
/// </summary>
public interface IExportJobStore
{
    /// <summary>
    /// Creates a new export job.
    /// </summary>
    /// <param name="job">The export job to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateJobAsync(BulkExportJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an export job by ID.
    /// </summary>
    /// <param name="jobId">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The export job, or null if not found.</returns>
    Task<BulkExportJob?> GetJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing export job.
    /// </summary>
    /// <param name="job">The export job to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpdateJobAsync(BulkExportJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an export job and its metadata.
    /// </summary>
    /// <param name="jobId">The job ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all export jobs for a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of export jobs.</returns>
    Task<List<BulkExportJob>> ListJobsForTenantAsync(int tenantId, CancellationToken cancellationToken = default);
}
