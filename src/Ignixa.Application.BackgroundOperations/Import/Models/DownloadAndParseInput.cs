// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for DownloadAndParseActivity.
/// </summary>
public record DownloadAndParseInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required string FileUrl { get; init; }
    public required string ResourceType { get; init; }
    public required int BatchSize { get; init; } // Default: 100
}
