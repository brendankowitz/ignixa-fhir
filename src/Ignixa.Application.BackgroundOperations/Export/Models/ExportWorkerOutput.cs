// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Export.Models;

/// <summary>
/// Output from the export worker activity.
/// Contains results of processing a single partition.
/// </summary>
public record ExportWorkerOutput(
    /// <summary>
    /// FHIR resource type that was exported.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of the surrogate ID range that was processed.
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of the surrogate ID range that was processed.
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// Total number of resources exported from this range.
    /// </summary>
    long ResourcesExported,

    /// <summary>
    /// Total bytes written to the output file.
    /// </summary>
    long BytesWritten);
