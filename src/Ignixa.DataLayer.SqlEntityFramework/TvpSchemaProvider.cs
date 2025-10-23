// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlEntityFramework;

/// <summary>
/// Provides Table-Valued Parameter (TVP) schema information by querying SQL Server metadata.
/// This ensures row generators create DataTables with columns matching the actual TVP types.
/// </summary>
public class TvpSchemaProvider
{
    private readonly FhirDbContext _context;
    private readonly ILogger<TvpSchemaProvider> _logger;
    private readonly ConcurrentDictionary<string, TvpSchema> _schemaCache;

    /// <summary>
    /// Represents the schema of a single TVP type.
    /// </summary>
    public record TvpSchema(
        string TypeName,
        IReadOnlyList<TvpColumn> Columns);

    /// <summary>
    /// Represents a single column in a TVP type.
    /// </summary>
    public record TvpColumn(
        string Name,
        string SqlType,
        int? MaxLength,
        bool IsNullable,
        int OrdinalPosition);

    public TvpSchemaProvider(FhirDbContext context, ILogger<TvpSchemaProvider> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaCache = new ConcurrentDictionary<string, TvpSchema>();
    }

    /// <summary>
    /// Gets the schema for a specific TVP type from the database.
    /// Results are cached to avoid repeated queries.
    /// </summary>
    public async Task<TvpSchema?> GetTvpSchemaAsync(string tvpTypeName, CancellationToken cancellationToken = default)
    {
        if (_schemaCache.TryGetValue(tvpTypeName, out var cachedSchema))
            return cachedSchema;

        try
        {
            var schema = await QueryTvpSchemaAsync(tvpTypeName, cancellationToken);
            if (schema != null)
            {
                _schemaCache.TryAdd(tvpTypeName, schema);
            }
            return schema;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TVP schema for {TvpTypeName}", tvpTypeName);
            return null;
        }
    }

    /// <summary>
    /// Creates a DataTable with columns matching the TVP type schema from the database.
    /// This ensures perfect column alignment with the SQL Server TVP definition.
    /// </summary>
    public async Task<DataTable?> CreateDataTableAsync(string tvpTypeName, CancellationToken cancellationToken = default)
    {
        var schema = await GetTvpSchemaAsync(tvpTypeName, cancellationToken);
        if (schema == null)
        {
            _logger.LogWarning("Cannot create DataTable: TVP schema not found for {TvpTypeName}", tvpTypeName);
            return null;
        }

        var table = new DataTable();
        foreach (var column in schema.Columns.OrderBy(c => c.OrdinalPosition))
        {
            var dotNetType = MapSqlTypeToDotNetType(column.SqlType, column.MaxLength);
            table.Columns.Add(column.Name, dotNetType);
        }

        return table;
    }

    /// <summary>
    /// Maps SQL Server data type names to .NET CLR types.
    /// Used to create DataTable columns with correct types.
    /// </summary>
    private static Type MapSqlTypeToDotNetType(string sqlType, int? maxLength)
    {
        return sqlType.ToUpperInvariant() switch
        {
            // Numeric types
            "BIGINT" => typeof(long),
            "INT" => typeof(int),
            "SMALLINT" => typeof(short),
            "TINYINT" => typeof(byte),
            "DECIMAL" or "NUMERIC" => typeof(decimal),
            "FLOAT" => typeof(double),
            "REAL" => typeof(float),

            // String types
            "VARCHAR" or "CHAR" => typeof(string),
            "NVARCHAR" or "NCHAR" => typeof(string),
            "TEXT" or "NTEXT" => typeof(string),

            // Binary types
            "VARBINARY" or "BINARY" => typeof(byte[]),
            "IMAGE" => typeof(byte[]),

            // Date/Time types
            "DATETIME" or "DATETIME2" => typeof(DateTime),
            "DATETIMEOFFSET" => typeof(DateTimeOffset),
            "DATE" => typeof(DateTime),
            "TIME" => typeof(TimeSpan),

            // Bit type
            "BIT" => typeof(bool),

            // Default
            _ => typeof(object),
        };
    }

    /// <summary>
    /// Queries the actual TVP schema from SQL Server metadata.
    /// </summary>
    private async Task<TvpSchema?> QueryTvpSchemaAsync(string tvpTypeName, CancellationToken cancellationToken)
    {
        var sql = @"
            SELECT
                c.name AS ColumnName,
                ty.name AS DataType,
                c.max_length,
                c.is_nullable,
                c.column_id
            FROM sys.types t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.table_types tt ON t.user_type_id = tt.user_type_id
            INNER JOIN sys.columns c ON tt.type_table_object_id = c.object_id
            INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            WHERE s.name = 'dbo' AND t.is_table_type = 1 AND t.name = @TypeName
            ORDER BY c.column_id";

        var connection = _context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@TypeName", tvpTypeName));

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var columns = new List<TvpColumn>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var columnName = reader.GetString(0);
                var dataType = reader.GetString(1);
#pragma warning disable CA1849
                var maxLength = reader.IsDBNull(2) ? null : (int?)reader.GetInt16(2);
#pragma warning restore CA1849
                var isNullable = reader.GetBoolean(3);
                var ordinalPosition = reader.GetInt32(4);

                columns.Add(new TvpColumn(columnName, dataType, maxLength, isNullable, ordinalPosition));
            }

            if (columns.Count == 0)
            {
                _logger.LogWarning("TVP type {TvpTypeName} not found in database", tvpTypeName);
                return null;
            }

            return new TvpSchema(tvpTypeName, columns);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
}
