// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Ignixa.Serialization.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for import orchestration.
/// </summary>
public record ImportOrchestrationInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required IReadOnlyList<InputFileInfo> InputFiles { get; init; }
    public required string Mode { get; init; } // "InitialLoad" or "IncrementalLoad"
    public ParametersJsonNode? StorageDetail { get; init; } // SAS tokens, etc.
}
