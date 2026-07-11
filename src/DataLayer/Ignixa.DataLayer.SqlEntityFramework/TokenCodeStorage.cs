// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Encodes the storage conventions for token search parameter system/code columns, shared by the
/// write path (RowGenerators) and read path (Search query generators) so the two are never allowed
/// to drift, per the SQL data layer cleanup plan's Task 4.
/// </summary>
public static class TokenCodeStorage
{
    /// <summary>
    /// Token codes at or under this length are stored inline in the Code column;
    /// longer codes are truncated to this length with the remainder in CodeOverflow.
    /// Must match the TokenSearchParam.Code column's VARCHAR(256) width (FhirDbContext also declares
    /// a CHK_TokenSearchParam_CodeOverflow constraint requiring LEN(Code) = 256 when CodeOverflow is
    /// set, but no migration currently materializes it in a real database — it exists only in the EF
    /// model). A mismatch here doesn't fail at write time; it silently desyncs the write and read
    /// paths, causing codes near the threshold to stop matching search.
    /// </summary>
    public const int MaxInlineCodeLength = 256;

    /// <summary>
    /// An empty or null system string means the token explicitly has no system —
    /// stored as a NULL SystemId, matched via the FHIR "|code" convention.
    /// </summary>
    public static bool IsExplicitNoSystem([NotNullWhen(false)] string? system) => string.IsNullOrEmpty(system);

    /// <summary>
    /// Splits a token code into its inline and overflow parts per <see cref="MaxInlineCodeLength"/>.
    /// </summary>
    public static (string Code, string? CodeOverflow) SplitCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        return code.Length > MaxInlineCodeLength
            ? (code[..MaxInlineCodeLength], code[MaxInlineCodeLength..])
            : (code, null);
    }
}
