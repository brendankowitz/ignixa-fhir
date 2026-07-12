// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Encodes the storage conventions for string search parameter text columns, shared by the write path
/// (RowGenerators) and read path (Search query generators) so the two are never allowed to drift - the
/// string-column sibling of <see cref="TokenCodeStorage"/>.
/// </summary>
public static class StringStorage
{
    /// <summary>
    /// String values at or under this length are stored inline in the Text column; longer values are
    /// truncated to this length with the remainder in TextOverflow. Must match the StringSearchParam.Text
    /// and TokenStringCompositeSearchParam.Text2 columns' NVARCHAR(256) width.
    /// </summary>
    public const int InlineWidth = 256;

    /// <summary>
    /// Collation for FHIR string search's default (no modifier) and :contains/:starts-with matching -
    /// case-insensitive, accent-insensitive.
    /// </summary>
    public const string DefaultCollation = "Latin1_General_100_CI_AI";

    /// <summary>
    /// Collation for FHIR string search's :exact modifier - case-sensitive, accent-sensitive.
    /// </summary>
    public const string ExactCollation = "Latin1_General_100_CS_AS";

    /// <summary>
    /// Splits a string value into its inline and overflow parts per <see cref="InlineWidth"/>.
    /// </summary>
    public static (string Inline, string? Overflow) Split(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Length > InlineWidth
            ? (value[..InlineWidth], value[InlineWidth..])
            : (value, null);
    }
}
