// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Import job metadata stored in job store.
/// </summary>
public class BulkImportJob
{
    public required string JobId { get; set; }
    public required int TenantId { get; set; }
    public required string Status { get; set; } // "Queued", "Running", "Completed", "Failed", "Cancelled"
    public required string InputFormat { get; set; } // "application/fhir+ndjson"
    public required string InputSource { get; set; } // Azure blob URL or local file path
    public required string Mode { get; set; } // "InitialLoad" or "IncrementalLoad"
    public required IReadOnlyList<InputFileInfo> InputFiles { get; set; }

    public string? OrchestrationInstanceId { get; set; }

    public DateTimeOffset CreateDate { get; set; }
    public DateTimeOffset QueuedDate { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }

    public int TotalResources { get; set; }
    public int TotalErrors { get; set; }

    // Phase 6: Progress tracking
    public int ProcessedResources { get; set; }
    public int EstimatedTotalResources { get; set; } // Estimated total (from file sizes or counts)
    public string? CurrentFile { get; set; } // Currently processing file
    public int ProcessedFiles { get; set; } // Number of files completed
    public double? ProgressPercentage { get; set; } // Calculated progress (0-100)

    public string? ErrorFileUrl { get; set; }
    public string? ErrorMessage { get; set; }
}
