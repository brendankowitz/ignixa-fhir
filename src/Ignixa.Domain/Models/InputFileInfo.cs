// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Input file information from FHIR Parameters resource.
/// </summary>
public record InputFileInfo
{
    public required string Type { get; init; }  // "Patient", "Observation", etc.
    public required string Url { get; init; }   // Azure blob URL or local file path
    public string? ETag { get; init; }          // For validation
}
