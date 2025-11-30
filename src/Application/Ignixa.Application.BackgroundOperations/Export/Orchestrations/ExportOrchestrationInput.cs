// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Orchestrations;

/// <summary>
/// Input for the export orchestration.
/// Uses same signature as ExportCoordinatorInput for compatibility.
/// </summary>
public record ExportOrchestrationInput(
    string JobId,
    int TenantId,
    IReadOnlyCollection<string> ResourceTypes,
    DateTimeOffset? Since = null,
    IReadOnlyDictionary<string, string>? TypeFilters = null);
