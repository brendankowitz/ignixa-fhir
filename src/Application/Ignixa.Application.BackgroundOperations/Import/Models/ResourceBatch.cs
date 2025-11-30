// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Batch of resources from NDJSON file.
/// </summary>
public record ResourceBatch
{
    public required int BatchNumber { get; init; }
    public required IReadOnlyList<string> Resources { get; init; } // NDJSON lines (raw JSON strings)
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
}
