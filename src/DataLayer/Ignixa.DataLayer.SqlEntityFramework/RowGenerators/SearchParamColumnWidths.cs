// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql.Catalog;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// The widths at which the row generators split an overflowing value, sourced from the real DDL in
/// Resources/97.sql via <see cref="SqlCatalog"/>.
/// </summary>
/// <remarks>
/// microsoft/fhir-server derives the same split from its generated column metadata
/// (<c>VLatest.TokenSearchParam.Code.Metadata.MaxLength</c>), so taking it from the catalog rather than a
/// literal is what keeps rows written here byte-compatible with a database that server populated -- the
/// zero-data-migration promise in this package's README. A literal that drifted from the DDL would make
/// every overflowing value silently unmatchable, because the search compiler splits at the same width.
///
/// One width covers every token slot because fhir-server's composite generators delegate to its leaf
/// token generator, and 97.sql declares every composite Code1/Code2 identically to TokenSearchParam.Code.
/// </remarks>
internal static class SearchParamColumnWidths
{
    /// <summary>
    /// Split point for a token code: Code keeps the leading characters, CodeOverflow the remainder.
    /// </summary>
    internal static readonly int TokenCode = Width("TokenSearchParam", "Code");

    /// <summary>
    /// Split point for a searchable string: Text keeps a redundant leading prefix so the index can still
    /// seek, and TextOverflow holds the WHOLE value -- not the remainder.
    /// </summary>
    internal static readonly int StringText = Width("StringSearchParam", "Text");

    private static int Width(string table, string column) =>
        SqlCatalog.Default.Table(table).Column(column).MaxLength
        ?? throw new InvalidOperationException($"{table}.{column} has no MaxLength in SqlCatalog.");
}
