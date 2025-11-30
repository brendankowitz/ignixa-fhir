// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for CompleteJobActivity.
/// </summary>
public record CompleteJobInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required int TotalResources { get; init; }
    public required int TotalErrors { get; init; }
    public required IReadOnlyList<ImportErrorLogEntry> ErrorLogEntries { get; init; }
    public ParametersJsonNode? StorageDetail { get; init; }

    /// <summary>
    /// Job start date/time for throughput calculation (resources/sec).
    /// </summary>
    public DateTimeOffset? StartDate { get; init; }
}
