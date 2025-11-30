// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Activities;

/// <summary>
/// Input for CompleteJobActivity.
/// </summary>
public record CompleteJobInput(
    string JobId,
    int TenantId,
    bool Success,
    Dictionary<string, string> ExportedFiles,
    int TotalResourcesExported,
    string? ErrorMessage);
