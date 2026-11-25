// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// A batch would cross one of the writer's fail-closed ceilings, or a ceiling could not be proven. Thrown before any
/// table is created or changed.
/// </summary>
/// <remarks>The message names the table path, the ceiling and the measured value; it never contains resource data.</remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every limit failure names its table and ceiling; a parameterless or message-only constructor would create one that does not.")]
public sealed class FabricSilverLimitExceededException : Exception
{
    internal FabricSilverLimitExceededException(
        string tablePath,
        FabricSilverLimit limit,
        long limitValue,
        long? actual,
        Exception? innerException = null)
        : base(Describe(tablePath, limit, limitValue, actual), innerException)
    {
        TablePath = tablePath;
        Limit = limit;
        LimitValue = limitValue;
        Actual = actual;
    }

    /// <summary>The full path of the table whose ceiling failed.</summary>
    public string TablePath { get; }

    /// <summary>Which ceiling failed.</summary>
    public FabricSilverLimit Limit { get; }

    /// <summary>The configured ceiling.</summary>
    public long LimitValue { get; }

    /// <summary>The measured value, or <see langword="null"/> when it could not be determined.</summary>
    public long? Actual { get; }

    private static string Describe(string tablePath, FabricSilverLimit limit, long limitValue, long? actual) => limit switch
    {
        FabricSilverLimit.SourceResources =>
            $"The batch for table '{tablePath}' holds {actual} resources, more than the limit of {limitValue}; split the batch.",
        FabricSilverLimit.TargetRows =>
            $"Table '{tablePath}' holds {actual} rows, more than the limit of {limitValue} a merge stays within the memory budget for.",
        FabricSilverLimit.TargetRowsUnknown =>
            $"The row count of table '{tablePath}' could not be determined, so the limit of {limitValue} rows cannot be proven.",
        FabricSilverLimit.TargetFiles =>
            $"Table '{tablePath}' holds {actual} data files, more than the limit of {limitValue}; the table must be compacted (OPTIMIZE) before writing.",
        FabricSilverLimit.TargetFilesUnknown =>
            $"The data file count of table '{tablePath}' could not be determined, so the limit of {limitValue} files cannot be proven.",
        _ => throw new ArgumentOutOfRangeException(nameof(limit)),
    };
}
