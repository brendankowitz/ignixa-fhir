// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Job lookups shared by the bulk-delete handlers and activities.
/// </summary>
internal static class BulkDeleteJobs
{
    /// <summary>
    /// Loads a bulk-delete job owned by <paramref name="tenantId"/>, or null. The type is filtered by the
    /// repository (before the definition is deserialized), and ownership is checked here as well because
    /// the repository validates the tenant only in Isolated mode: a job of another type or tenant is
    /// reported as absent rather than exposed. The reserved system partition never owns a bulk-delete job
    /// (creation rejects it), so it is reported as absent without a lookup.
    /// </summary>
    public static async Task<BackgroundJob<BulkDeleteJobDefinition>?> FindAsync(
        IBackgroundJobRepository<BulkDeleteJobDefinition> repository,
        int tenantId,
        string jobId,
        CancellationToken cancellationToken)
    {
        if (tenantId == SystemConstants.SystemPartitionId)
        {
            return null;
        }

        var job = await repository.GetAsync(jobId, tenantId, (int)BackgroundJobType.BulkDelete, cancellationToken);
        return job is not null && job.Definition.TenantId == tenantId ? job : null;
    }

    public static bool IsTerminal(string status) => status is "Completed" or "Failed" or "Cancelled";
}
