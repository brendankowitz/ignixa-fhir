// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Error log entry for a failed resource import.
/// </summary>
public record ImportErrorLogEntry
{
    public required string ResourceType { get; init; }
    public required string ResourceId { get; init; }
    public required string ErrorCode { get; init; }
    public required string ErrorMessage { get; init; }
    public required string ResourceJson { get; init; }
}
