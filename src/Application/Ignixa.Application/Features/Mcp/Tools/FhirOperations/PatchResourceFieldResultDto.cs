// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Application.Features.Mcp.Tools.FhirOperations;

/// <summary>
/// DTO for patch field result.
/// </summary>
public class PatchResourceFieldResultDto
{
    /// <summary>
    /// Whether the patch operation succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed, null if successful.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The patched resource if successful, null if operation failed.
    /// </summary>
    public ResourceJsonNode? PatchedResource { get; init; }
}
