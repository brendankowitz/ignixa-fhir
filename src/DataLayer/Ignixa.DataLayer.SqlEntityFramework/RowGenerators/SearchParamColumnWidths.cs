// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Search.Sql.Catalog;

namespace Ignixa.DataLayer.SqlEntityFramework.RowGenerators;

/// <summary>
/// The width at which a row generator divides an overflowing value between an inline column and its
/// overflow companion, sourced from the real DDL in Resources/97.sql via <see cref="SqlCatalog"/>.
/// </summary>
/// <remarks>
/// microsoft/fhir-server derives the same split from its generated column metadata
/// (<c>VLatest.TokenSearchParam.Code.Metadata.MaxLength</c>), so taking it from the catalog rather than a
/// literal is what keeps rows written here byte-compatible with a database that server populated -- the
/// zero-data-migration promise in this package's README.
///
/// Each caller names the column it is about to write, because that is the column the reader resolves the
/// same width from: <c>TokenColumnEquality</c> and <c>TokenStringLoweringRule</c> look up the width on the
/// table they are querying. Deriving both sides from the same catalog entry is what makes them agree.
/// Sharing one leaf width across every slot would only agree while the DDL happened to stay uniform, which
/// is the same class of coupling as the literal this replaced.
/// </remarks>
internal static class SearchParamColumnWidths
{
    internal static int For(string table, string column) =>
        SqlCatalog.Default.Table(table).Column(column).MaxLength
        ?? throw new InvalidOperationException(
            $"{table}.{column} declares no width, so the overflow split point is unknown.");
}
