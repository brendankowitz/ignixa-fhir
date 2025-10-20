// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for UpdateProgressActivity.
/// </summary>
public record UpdateProgressInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required int ProcessedResources { get; init; }
    public required int ProcessedFiles { get; init; }
    public required int TotalFiles { get; init; }
    public string? CurrentFile { get; init; }
}
