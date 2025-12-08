// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.SqlOnFhir.Parsing;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.SqlOnFhir.Writers;

/// <summary>
/// Extracts schema information from SQL on FHIR ViewDefinitions.
/// Converts ViewDefinition column definitions into Parquet schema and type mappings.
/// </summary>
public static class SchemaExtractor
{
    /// <summary>
    /// Extracts a Parquet schema from a ViewDefinition resource.
    /// </summary>
    /// <param name="viewDefinitionNode">ViewDefinition as ISourceNavigator</param>
    /// <returns>Tuple of Parquet schema and column type map</returns>
    public static (ParquetSchema Schema, Dictionary<string, string> ColumnTypeMap) ExtractParquetSchema(
        ISourceNavigator viewDefinitionNode)
    {
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);

        // Parse ViewDefinition using existing parser
        var viewDef = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);

        var fields = new List<DataField>();
        var columnTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Extract columns from all SELECT groups
        foreach (var selectGroup in viewDef.Select)
        {
            if (selectGroup.Columns.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var column in selectGroup.Columns)
            {
                var columnName = column.Name;
                var sqlType = (column.Type ?? "STRING").ToUpperInvariant();

                // Map SQL type to Parquet DataField
                var field = MapSqlTypeToParquetField(columnName, sqlType);
                fields.Add(field);

                // Store type mapping for later use
                columnTypeMap[columnName] = sqlType;
            }
        }

        // If no columns defined, create a minimal schema
        if (fields.Count == 0)
        {
            fields.Add(new DataField<string>("id"));
            columnTypeMap["id"] = "STRING";
        }

        var schema = new ParquetSchema(fields);
        return (schema, columnTypeMap);
    }

    /// <summary>
    /// Extracts column names and types from a ViewDefinition.
    /// </summary>
    /// <param name="viewDefinitionNode">ViewDefinition as ISourceNavigator</param>
    /// <returns>Dictionary mapping column names to SQL types</returns>
    public static Dictionary<string, string> ExtractColumnTypes(ISourceNavigator viewDefinitionNode)
    {
        var (_, columnTypeMap) = ExtractParquetSchema(viewDefinitionNode);
        return columnTypeMap;
    }

    /// <summary>
    /// Maps SQL type to Parquet DataField.
    /// </summary>
    private static DataField MapSqlTypeToParquetField(string columnName, string sqlType)
    {
        return sqlType switch
        {
            "STRING" => new DataField<string>(columnName),
            "BOOLEAN" => new DataField<bool?>(columnName),
            "INTEGER" => new DataField<int?>(columnName),
            "DECIMAL" => new DataField<decimal?>(columnName),
            "DATE" => new DataField<DateTime?>(columnName),
            "DATETIME" => new DataField<DateTimeOffset?>(columnName),
            _ => new DataField<string>(columnName) // Default to string for unknown types
        };
    }
}
