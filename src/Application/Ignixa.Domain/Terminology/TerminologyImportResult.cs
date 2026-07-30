// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Domain.Terminology;

/// <summary>
/// Result of a terminology import operation.
/// </summary>
public class TerminologyImportResult
{
    /// <summary>
    /// Whether the import succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Number of concepts/codes/mappings imported.
    /// </summary>
    public int ItemCount { get; init; }

    /// <summary>
    /// Error message if import failed (null if succeeded).
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Final import status to set on PackageResource.
    /// </summary>
    public required TerminologyImportStatus Status { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static TerminologyImportResult CreateSuccess(int itemCount)
    {
        return new TerminologyImportResult
        {
            Success = true,
            ItemCount = itemCount,
            Status = TerminologyImportStatus.Completed
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static TerminologyImportResult CreateFailure(string errorMessage)
    {
        return new TerminologyImportResult
        {
            Success = false,
            ItemCount = 0,
            ErrorMessage = errorMessage,
            Status = TerminologyImportStatus.Failed
        };
    }

    /// <summary>
    /// Creates a skipped result: this resource was examined and deliberately not imported, so nothing of it
    /// is in the terminology tables. <see cref="TerminologyImportStatus.Skipped"/> is the accurate terminal
    /// status for that, and it is what routes lookups to the JSON fallback.
    /// </summary>
    public static TerminologyImportResult CreateSkipped()
    {
        return new TerminologyImportResult
        {
            Success = true,
            ItemCount = 0,
            Status = TerminologyImportStatus.Skipped
        };
    }

    /// <summary>
    /// Creates a result for content that is unchanged since a previous terminal import, where no work was
    /// done and the status already on the row is the truth about what is in the terminology tables.
    /// <para>
    /// Distinct from <see cref="CreateSkipped"/> on purpose. Reporting this case as
    /// <see cref="TerminologyImportStatus.Skipped"/> overwrote a <see cref="TerminologyImportStatus.Completed"/>
    /// row that had every one of its concepts in the database, which both lied to
    /// <c>HybridTerminologyService</c> — it routes anything other than <c>Completed</c> to the in-memory
    /// fallback, so <c>$expand</c> stopped using the tables — and broke the importer's own
    /// unchanged-content guard, which re-imported the resource in full on the next package load.
    /// </para>
    /// </summary>
    /// <param name="retainedStatus">The terminal status already recorded on the package resource.</param>
    public static TerminologyImportResult CreateUnchanged(TerminologyImportStatus retainedStatus)
    {
        return new TerminologyImportResult
        {
            Success = true,
            ItemCount = 0,
            Status = retainedStatus
        };
    }
}
