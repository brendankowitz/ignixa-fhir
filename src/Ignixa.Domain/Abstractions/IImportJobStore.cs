// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Stores import job metadata (status, progress, errors).
/// </summary>
public interface IImportJobStore
{
    /// <summary>
    /// Creates a new import job.
    /// </summary>
    Task CreateJobAsync(BulkImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Gets import job by ID.
    /// </summary>
    Task<BulkImportJob?> GetJobAsync(int tenantId, string jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates import job metadata.
    /// </summary>
    Task UpdateJobAsync(BulkImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all import jobs for a tenant (for admin UI).
    /// </summary>
    Task<IReadOnlyList<BulkImportJob>> ListJobsAsync(int tenantId, CancellationToken cancellationToken);
}
