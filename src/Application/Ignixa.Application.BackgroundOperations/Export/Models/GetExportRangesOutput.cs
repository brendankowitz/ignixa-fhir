// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

/// <summary>
/// Output from the GetExportRanges activity.
/// Contains the surrogate ID ranges to be processed in parallel.
/// </summary>
public record GetExportRangesOutput(
    /// <summary>
    /// FHIR resource type.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// List of non-overlapping, exhaustive surrogate ID ranges.
    /// Each (StartId, EndId) tuple represents one worker's partition.
    /// </summary>
    IReadOnlyList<(long StartId, long EndId)> Ranges);
