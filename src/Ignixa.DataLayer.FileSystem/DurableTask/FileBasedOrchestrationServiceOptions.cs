// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.FileSystem.DurableTask;

/// <summary>
/// Configuration options for the file-based orchestration service.
/// </summary>
public class FileBasedOrchestrationServiceOptions
{
    /// <summary>
    /// Base directory for FHIR data storage (e.g., "fhir-data").
    /// Job state will be persisted to {BaseDirectory}/_jobs/.
    /// </summary>
    public string BaseDirectory { get; set; } = "fhir-data";

    /// <summary>
    /// How long a work item lock is valid before it expires and can be claimed by another worker.
    /// </summary>
    public TimeSpan WorkItemLockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How frequently to flush in-memory state changes to disk.
    /// </summary>
    public TimeSpan StateFlushInterval { get; set; } = TimeSpan.FromSeconds(1);
}
