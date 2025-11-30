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
    /// Creates a skipped result (content hash unchanged).
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
}
