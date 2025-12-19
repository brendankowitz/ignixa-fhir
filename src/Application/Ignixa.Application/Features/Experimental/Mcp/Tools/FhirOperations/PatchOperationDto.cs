// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;

/// <summary>
/// DTO for a single FHIRPath Patch operation.
/// </summary>
public class PatchOperationDto
{
    /// <summary>
    /// Operation type: "add", "replace", "delete", "insert", or "move"
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// FHIRPath expression identifying the element(s) to modify
    /// Examples: "Patient.active", "Patient.name[0].given[0]", "Patient.name.where(use='official').family"
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Value to add or replace with. Required for "add" and "replace", optional for others.
    /// Can be a primitive (string, number, boolean) or a complex JSON object.
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Array index for "insert" and "move" operations.
    /// </summary>
    public int? Index { get; init; }
}
