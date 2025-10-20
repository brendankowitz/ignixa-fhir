// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Output from CompleteJobActivity.
/// </summary>
public record CompleteJobOutput
{
    public string? ErrorFileUrl { get; init; }
}
