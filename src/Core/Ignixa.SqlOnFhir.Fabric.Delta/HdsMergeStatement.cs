// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Builds the HDS conditional upsert: update a matched row only when the source is strictly newer, insert an
/// unmatched row that has <c>meta_lastUpdated</c>, and leave <c>msftCreatedDatetime</c> as first written. Every
/// identifier is double-quoted, so camelCase and hyphenated column names keep their case and parse as one name; names
/// are restricted to <c>[A-Za-z0-9_-]</c> by <see cref="FabricSilverTableBatch"/>, so no name can contain a quote.
/// </summary>
internal static class HdsMergeStatement
{
    public static string Build(IReadOnlyList<string> columnNames)
    {
        var updates = columnNames
            .Where(name => name != HdsColumns.CreatedDatetime)
            .Select(name => $"{Quote(name)} = source.{Quote(name)}");

        return new StringBuilder()
            .Append("MERGE INTO target USING source ON target.").Append(Quote(HdsColumns.Id))
            .Append(" = source.").Append(Quote(HdsColumns.Id))
            .Append(" WHEN MATCHED AND target.").Append(Quote(HdsColumns.MetaLastUpdated))
            .Append(" < source.").Append(Quote(HdsColumns.MetaLastUpdated))
            .Append(" THEN UPDATE SET ").AppendJoin(", ", updates)
            .Append(" WHEN NOT MATCHED AND source.").Append(Quote(HdsColumns.MetaLastUpdated))
            .Append(" IS NOT NULL THEN INSERT (").AppendJoin(", ", columnNames.Select(Quote))
            .Append(") VALUES (").AppendJoin(", ", columnNames.Select(name => $"source.{Quote(name)}"))
            .Append(')')
            .ToString();
    }

    private static string Quote(string name) => $"\"{name}\"";
}
