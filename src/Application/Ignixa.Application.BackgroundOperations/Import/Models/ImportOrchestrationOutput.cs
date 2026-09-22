// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Output from import orchestration.
/// </summary>
public record ImportOrchestrationOutput
{
    public required string JobId { get; init; }
    public required string Status { get; init; } // "Completed", "Failed", "Cancelled"
    public int TotalResources { get; init; }
    public int TotalErrors { get; init; }
    public bool CountsAreComplete { get; init; } = true;
    public int? ResourcesWithUnknownOutcome { get; init; } = 0;
    public string? ErrorFileUrl { get; init; }
    public string? ErrorMessage { get; init; }
}
