// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Orchestrations;

/// <summary>
/// Output from the export orchestration.
/// Maps to ExportCoordinatorOutput for compatibility.
/// </summary>
public record ExportOrchestrationOutput(
    bool Success,
    Dictionary<string, string> ExportedFiles,
    int TotalResourcesExported,
    string? ErrorMessage);
