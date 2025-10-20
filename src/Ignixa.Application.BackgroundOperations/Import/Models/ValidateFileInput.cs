// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Import.Models;

/// <summary>
/// Input for ValidateFileActivity.
/// </summary>
public record ValidateFileInput
{
    public required string JobId { get; init; }
    public required int TenantId { get; init; }
    public required IReadOnlyList<InputFileInfo> InputFiles { get; init; }
}
