// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// Output from SearchAndWriteChunkActivity.
/// </summary>
public record SearchAndWriteChunkOutput(
    int ResourceCount,
    string? ContinuationToken,
    long FileSizeBytes);
