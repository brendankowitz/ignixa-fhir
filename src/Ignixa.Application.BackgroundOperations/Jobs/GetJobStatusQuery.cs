// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Ignixa.Application.BackgroundOperations.Jobs;

/// <summary>
/// Query to get the current status of an import or export job.
/// Retrieves detailed progress, completion status, and results.
/// </summary>
public record GetJobStatusQuery : IRequest<GetJobStatusResult>
{
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    public required string JobId { get; init; }

    /// <summary>
    /// Job type: "Import" or "Export".
    /// </summary>
    public required string JobType { get; init; }

    /// <summary>
    /// Tenant ID for validation.
    /// </summary>
    public required int TenantId { get; init; }
}
