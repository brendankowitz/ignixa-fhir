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
/// Export stream writer that applies SQL-on-FHIR ViewDefinition transformation before writing to Parquet.
/// Uses SqlOnFhirEvaluator to transform FHIR resources into tabular rows based on ViewDefinition columns.
/// </summary>
public class ViewDefinitionExportStreamWriter : IExportStreamWriter
{
    private readonly ParquetExportStreamWriter _parquetWriter;
    private readonly SqlOnFhirEvaluator _evaluator;
    private readonly ISourceNode _viewDefinitionNode;
    private readonly IStructureDefinitionSummaryProvider _structureProvider;
    private readonly ILogger<ViewDefinitionExportStreamWriter> _logger;
    private long _resourcesProcessed;
    private long _rowsGenerated;

    public long BytesWritten => _parquetWriter.BytesWritten;

    /// <summary>
    /// Constructor that wraps an existing ParquetExportStreamWriter.
    /// </summary>
    public ViewDefinitionExportStreamWriter(
        ParquetExportStreamWriter parquetWriter,
        ISourceNode viewDefinitionNode,
        IStructureDefinitionSummaryProvider structureProvider,
        ILogger<ViewDefinitionExportStreamWriter> logger)
    {
        _parquetWriter = parquetWriter ?? throw new ArgumentNullException(nameof(parquetWriter));
        _viewDefinitionNode = viewDefinitionNode ?? throw new ArgumentNullException(nameof(viewDefinitionNode));
        _structureProvider = structureProvider ?? throw new ArgumentNullException(nameof(structureProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _evaluator = new SqlOnFhirEvaluator();
    }

    /// <summary>
    /// Constructor that creates its own ParquetExportStreamWriter with schema derived from ViewDefinition.
    /// This is the preferred constructor as it allows the Parquet schema to match the ViewDefinition columns.
    /// </summary>
    public ViewDefinitionExportStreamWriter(
        IBlobStorageClient blobStorage,
        string outputPath,
        ISourceNode viewDefinitionNode,
        IStructureDefinitionSummaryProvider structureProvider,
        ILoggerFactory loggerFactory,
        int rowsPerBatch = ParquetExportStreamWriter.DefaultRowsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(blobStorage);
        ArgumentNullException.ThrowIfNull(outputPath);
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);
        ArgumentNullException.ThrowIfNull(structureProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _viewDefinitionNode = viewDefinitionNode;
        _structureProvider = structureProvider;
        _logger = loggerFactory.CreateLogger<ViewDefinitionExportStreamWriter>();
        _evaluator = new SqlOnFhirEvaluator();

        // Build Parquet schema from ViewDefinition using schema evaluator
        ParquetSchema? schema = null;
        try
        {
            // Step 1: Parse ViewDefinition to expression tree
            var viewExpression = ViewDefinitionExpressionParser.Parse(viewDefinitionNode);

            // Step 2: Extract schema using visitor pattern
            var schemaEvaluator = new SqlOnFhirSchemaEvaluator();
            var columns = schemaEvaluator.GetSchema(viewExpression);

            // Step 3: Convert ColumnSchema to Parquet DataFields
            var fields = new List<DataField>();
            foreach (var column in columns)
            {
                var field = MapColumnSchemaToParquetField(column);
                fields.Add(field);
            }

            schema = new ParquetSchema(fields);
            _logger.LogDebug("Built Parquet schema from ViewDefinition: {ColumnCount} columns", schema.Fields.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build Parquet schema from ViewDefinition, falling back to auto-inference");
            // schema remains null - ParquetExportStreamWriter will infer schema from first batch
        }

        // Create underlying Parquet writer with schema
        var parquetLogger = loggerFactory.CreateLogger<ParquetExportStreamWriter>();
        _parquetWriter = new ParquetExportStreamWriter(
            blobStorage,
            outputPath,
            parquetLogger,
            schema,
            rowsPerBatch);
    }

    public async Task WriteResourceAsync(SearchEntryResult resource, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Parse resource bytes directly to ResourceJsonNode, then convert to ITypedElement
            var resourceNode = JsonSourceNodeFactory.Parse(resource.ResourceBytes);

            if (resourceNode == null)
            {
                _logger.LogWarning(
                    "Failed to parse resource {ResourceType}/{ResourceId}, skipping",
                    resource.ResourceType,
                    resource.ResourceId);
                return;
            }

            var typedElement = resourceNode.ToTypedElement(_structureProvider);

            // 2. Evaluate ViewDefinition against resource (returns IEnumerable<Dictionary<string, object?>>)
            var rows = _evaluator.Evaluate(_viewDefinitionNode, typedElement);

            // 3. Write each row to Parquet
            var rowCount = 0;
            foreach (var row in rows)
            {
                // ParquetExportStreamWriter.WriteRowAsync expects rows as Dictionary<string, object?>
                // The evaluator already returns this format, so we can write directly
                await _parquetWriter.WriteRowAsync(row, cancellationToken);
                rowCount++;
            }

            _resourcesProcessed++;
            _rowsGenerated += rowCount;

            if (_resourcesProcessed % 1000 == 0)
            {
                _logger.LogDebug(
                    "ViewDefinition export progress: {ResourcesProcessed} resources processed, {RowsGenerated} rows generated",
                    _resourcesProcessed,
                    _rowsGenerated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to evaluate ViewDefinition for resource {ResourceType}/{ResourceId}",
                resource.ResourceType,
                resource.ResourceId);

            // Rethrow - export should fail if ViewDefinition evaluation fails
            throw;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _parquetWriter.FlushAsync(cancellationToken);

        _logger.LogInformation(
            "ViewDefinition export completed: {ResourcesProcessed} resources processed, {RowsGenerated} rows generated, {BytesWritten} bytes written",
            _resourcesProcessed,
            _rowsGenerated,
            BytesWritten);
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
}
