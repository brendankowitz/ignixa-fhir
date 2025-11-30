// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// Input for SearchAndWriteChunkActivity.
/// </summary>
public record SearchAndWriteChunkInput(
    int TenantId,
    string ResourceType,
    string OutputPath,
    string? ContinuationToken,
    string? TypeFilter);
