// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for ImportBatchActivity.
/// </summary>
public record ImportBatchInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required string ResourceType { get; init; }
    public required IReadOnlyList<string> Resources { get; init; } // NDJSON lines (raw JSON strings)
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"
}
