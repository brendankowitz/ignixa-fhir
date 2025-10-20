// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Output from ImportBatchActivity.
/// </summary>
public record ImportBatchOutput
{
    public required int SuccessCount { get; init; }
    public required int ErrorCount { get; init; }
    public required IReadOnlyList<ImportErrorLogEntry> Errors { get; init; }
}
