// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Output from CompleteJobActivity.
/// </summary>
public record CompleteJobOutput
{
    public string? ErrorFileUrl { get; init; }

    /// <summary>
    /// Result of the import job to be stored in BackgroundJob.Result.
    /// </summary>
    public ImportJobResult? Result { get; init; }
}
