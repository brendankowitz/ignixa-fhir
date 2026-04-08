// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;
using Microsoft.Extensions.Logging;
using Parquet.Data;
using Parquet.Schema;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Factory for creating ParquetExportStreamWriter instances with ViewDefinition support.
/// Handles schema inference from ViewDefinition and delegates to ParquetExportStreamWriter for actual I/O.
///
/// DEPRECATED: This class is now a convenience factory wrapper around ParquetExportStreamWriter.
/// The actual ViewDefinition evaluation logic has been integrated directly into ParquetExportStreamWriter
/// for better cohesion between schema and evaluation logic.
///
/// Recommended usage:
/// 1. Parse ViewDefinition to extract schema
/// 2. Create ParquetExportStreamWriter with viewDefinitionNode and schemaProvider
/// 3. Call WriteResourceAsync on the writer - it handles evaluation automatically
/// </summary>
public partial class ViewDefinitionExportStreamWriter : IExportStreamWriter
{
    private readonly ParquetExportStreamWriter _parquetWriter;

    public long BytesWritten => _parquetWriter.BytesWritten;

    /// <summary>
    /// Constructor that creates a ParquetExportStreamWriter with ViewDefinition support.
    /// Builds the Parquet schema from the ViewDefinition columns.
    /// </summary>
    public ViewDefinitionExportStreamWriter(
        IBlobStorageClient blobStorage,
        string outputPath,
        ISourceNavigator viewDefinitionNode,
        ISchema schemaProvider,
        ILoggerFactory loggerFactory,
        int rowsPerBatch = ParquetExportStreamWriter.DefaultRowsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(blobStorage);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<ViewDefinitionExportStreamWriter>();

        // Build Parquet schema from ViewDefinition using schema evaluator
        ParquetSchema? schema = null;
        Dictionary<string, string>? columnTypeMap = null;
        try
        {
            // Step 1: Parse ViewDefinition to expression tree
            var viewExpression = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);

            // Step 2: Extract schema using visitor pattern
            var schemaEvaluator = new SqlOnFhirSchemaEvaluator();
            var columns = schemaEvaluator.GetSchema(viewExpression);

            // Step 3: Build column type map for type-safe writing
            columnTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fields = new List<DataField>();
            foreach (var column in columns)
            {
                columnTypeMap[column.Name] = column.Type ?? "STRING";
                var field = MapColumnSchemaToParquetField(column);
                fields.Add(field);
            }

            schema = new ParquetSchema(fields);
            var columnCount = schema.Fields.Count;
            LogBuiltParquetSchema(logger, columnCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build Parquet schema from ViewDefinition, falling back to auto-inference");
            // schema remains null - ParquetExportStreamWriter will infer schema from first batch
        }

        // Create Parquet writer with ViewDefinition support
        // The writer will use the schema, column type map, and ViewDefinition to transform resources during WriteResourceAsync
        var parquetLogger = loggerFactory.CreateLogger<ParquetExportStreamWriter>();
        _parquetWriter = new ParquetExportStreamWriter(
            blobStorage,
            outputPath,
            parquetLogger,
            schema,
            rowsPerBatch,
            viewDefinitionNode,
            schemaProvider,
            columnTypeMap);
    }

    /// <summary>
    /// Writes a resource to the Parquet output.
    /// If this writer was created with a ViewDefinition, the resource is transformed using SQL-on-FHIR evaluation.
    /// Otherwise, a simple 3-column schema is used (resourceType, id, rawResource).
    /// </summary>
    public async Task WriteResourceAsync(SearchEntryResult resource, CancellationToken cancellationToken = default)
    {
        // Delegate to the underlying ParquetExportStreamWriter which handles both
        // ViewDefinition evaluation (if provided) and simple schema fallback
        await _parquetWriter.WriteResourceAsync(resource, cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _parquetWriter.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _parquetWriter.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Maps a SqlOnFhir ColumnSchema to a Parquet DataField.
    /// Handles type conversion from FHIR types to Parquet types.
    /// </summary>
    private static DataField MapColumnSchemaToParquetField(ColumnSchema column)
    {
        // For collection columns, map to string (JSON array representation)
        if (column.Collection)
        {
            return new DataField<string>(column.Name);
        }

        // Map SQL on FHIR types to Parquet types
        // Per SQL on FHIR v2 spec and Parquet best practices
        var sqlType = column.Type?.ToUpperInvariant();

        return sqlType switch
        {
            "STRING" => new DataField<string>(column.Name),
            "INTEGER" => new DataField<int?>(column.Name),
            "DECIMAL" => new DataField<decimal?>(column.Name),
            "BOOLEAN" => new DataField<bool?>(column.Name),
            "DATE" => new DataField<DateTime?>(column.Name),
            "DATETIME" => new DataField<DateTimeOffset?>(column.Name),
            null => new DataField<string>(column.Name), // No type specified - default to string
            _ => new DataField<string>(column.Name) // Unknown type - default to string
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Built Parquet schema from ViewDefinition: {ColumnCount} columns")]
    private static partial void LogBuiltParquetSchema(ILogger logger, int columnCount);

}
