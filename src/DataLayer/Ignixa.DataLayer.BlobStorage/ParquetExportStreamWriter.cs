// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Ignixa.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.SqlOnFhir.Parsing;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Implementation of IExportStreamWriter using Parquet format with blob storage.
/// Buffers rows in memory, writes row groups periodically, then uploads the complete Parquet file.
/// Unlike NDJSON (which appends incrementally), Parquet requires buffering and a single upload.
/// </summary>
public partial class ParquetExportStreamWriter : IExportStreamWriter
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly string _outputPath;
    private readonly ILogger<ParquetExportStreamWriter> _logger;
    private readonly ParquetSchema? _providedSchema;
    private readonly int _rowsPerBatch;
    private readonly List<Dictionary<string, object?>> _rowBuffer;
    private readonly ISourceNavigator? _viewDefinitionNode;
    private readonly ISchema? _schemaProvider;
    private readonly SqlOnFhirEvaluator? _evaluator;
    private readonly Dictionary<string, string>? _columnTypeMap; // Maps column name to SQL type (STRING, INTEGER, etc.)
    private MemoryStream _parquetStream;
    private ParquetWriter? _parquetWriter;
    private ParquetSchema? _inferredSchema;
    private long _bytesWritten;
    private bool _disposed;
    private long _resourcesProcessed;
    private long _rowsGenerated;
    private readonly Stopwatch _stopwatch;

    /// <summary>
    /// Default number of rows to buffer before writing a Parquet row group.
    /// Balances memory usage with row group efficiency.
    /// </summary>
    public const int DefaultRowsPerBatch = 10_000;

    public long BytesWritten => _bytesWritten;

    public ParquetExportStreamWriter(
        IBlobStorageClient blobStorage,
        string outputPath,
        ILogger<ParquetExportStreamWriter> logger,
        ParquetSchema? schema = null,
        int rowsPerBatch = DefaultRowsPerBatch,
        ISourceNavigator? viewDefinitionNode = null,
        ISchema? schemaProvider = null,
        Dictionary<string, string>? columnTypeMap = null)
    {
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (rowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerBatch), "Rows per batch must be greater than zero");
        }

        // Validate ViewDefinition parameters consistency
        if ((viewDefinitionNode == null) != (schemaProvider == null))
        {
            throw new ArgumentException(
                "Both viewDefinitionNode and schemaProvider must be provided together or both null");
        }

        _providedSchema = schema;
        _rowsPerBatch = rowsPerBatch;
        _rowBuffer = new List<Dictionary<string, object?>>(_rowsPerBatch);
        _parquetStream = new MemoryStream();
        _bytesWritten = 0;
        _viewDefinitionNode = viewDefinitionNode;
        _schemaProvider = schemaProvider;
        _evaluator = viewDefinitionNode != null ? new SqlOnFhirEvaluator() : null;
        _columnTypeMap = columnTypeMap;
        _resourcesProcessed = 0;
        _rowsGenerated = 0;
        _stopwatch = Stopwatch.StartNew();
    }

    public async Task WriteResourceAsync(SearchEntryResult resource, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Parse resource bytes directly to ResourceJsonNode
            var jsonNode = JsonSourceNodeFactory.Parse(resource.ResourceBytes);

            if (jsonNode == null)
            {
                _logger.LogWarning(
                    "Failed to parse resource {ResourceType}/{ResourceId}, skipping",
                    resource.ResourceType,
                    resource.ResourceId);
                return;
            }

            // Log which export path we're taking (ViewDefinition vs simple)
            if (_viewDefinitionNode != null && _evaluator != null && _schemaProvider != null)
            {
                await WriteResourceWithViewDefinitionAsync(jsonNode.ToElement(_schemaProvider), resource, cancellationToken);
            }
            else
            {
                _logger.LogWarning(
                    "Using simple export path (no ViewDefinition): ViewDefinition={HasViewDef}, Evaluator={HasEval}, Provider={HasProvider}, Resource={ResourceType}/{ResourceId}",
                    _viewDefinitionNode != null,
                    _evaluator != null,
                    _schemaProvider != null,
                    resource.ResourceType,
                    resource.ResourceId);
                WriteResourceSimple(jsonNode);
            }

            // Flush batch if buffer is full
            if (_rowBuffer.Count >= _rowsPerBatch)
            {
                await FlushBatchAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing resource to Parquet buffer");
            throw;
        }
    }

    private Task WriteResourceWithViewDefinitionAsync(
        IElement resourceElement,
        SearchEntryResult resource,
        CancellationToken cancellationToken)
    {
        // Evaluate ViewDefinition against resource (returns IEnumerable<Dictionary<string, object?>>)
        var rows = _evaluator!.Evaluate(_viewDefinitionNode!, resourceElement);
        var rowList = rows?.ToList() ?? new List<Dictionary<string, object?>>(); // Materialize to count

        if (rowList.Count == 0)
        {
            _logger.LogWarning(
                "ViewDefinition evaluation returned zero rows for {ResourceType}/{ResourceId}",
                resource.ResourceType,
                resource.ResourceId);
            return Task.CompletedTask;
        }

        // Add all evaluated rows to buffer
        var rowCount = 0;
        foreach (var row in rowList)
        {
            _rowBuffer.Add(row);
            rowCount++;
        }

        _resourcesProcessed++;
        _rowsGenerated += rowCount;

        if (_resourcesProcessed % 1000 == 0)
        {
            var rate = _stopwatch.Elapsed.TotalSeconds > 0 ? _resourcesProcessed / _stopwatch.Elapsed.TotalSeconds : 0;
            LogViewDefinitionExportProgress(_logger, _resourcesProcessed, _rowsGenerated, rate);
        }

        return Task.CompletedTask;
    }

    private void WriteResourceSimple(Ignixa.Serialization.SourceNodes.ResourceJsonNode jsonNode)
    {
        // Create row dictionary with simple schema: resourceType, id, rawResource
        var row = new Dictionary<string, object?>
        {
            ["resourceType"] = jsonNode.ResourceType,
            ["id"] = jsonNode.Id,
            ["rawResource"] = jsonNode.SerializeToString()
        };

        // Add to buffer
        _rowBuffer.Add(row);
    }


    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Flush any remaining rows in buffer
            if (_rowBuffer.Count > 0)
            {
                await FlushBatchAsync(cancellationToken);
            }

            // Finalize the Parquet file
            if (_parquetWriter != null)
            {
                await _parquetWriter.DisposeAsync();
                _parquetWriter = null;
            }

            // Upload complete file to blob storage
            if (_parquetStream.Length > 0)
            {
                _parquetStream.Position = 0;
                await _blobStorage.WriteBlobAsync(_outputPath, _parquetStream, cancellationToken);

                _bytesWritten = _parquetStream.Length;

                LogUploadedParquetFile(_logger, _bytesWritten, _outputPath);

                // If ViewDefinition was used, log final stats
                if (_viewDefinitionNode != null)
                {
                    LogViewDefinitionExportCompleted(_logger, _resourcesProcessed, _rowsGenerated, _bytesWritten);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Parquet writer");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Final flush before disposal
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during final flush on dispose");
        }
        finally
        {
            if (_parquetWriter != null)
            {
                await _parquetWriter.DisposeAsync();
            }

            _parquetStream?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private async Task FlushBatchAsync(CancellationToken cancellationToken)
    {
        if (_rowBuffer.Count == 0)
        {
            return;
        }

        try
        {
            // Initialize schema and writer on first batch
            if (_parquetWriter == null)
            {
                InitializeWriter();
            }

            // Write row group
            using var groupWriter = _parquetWriter!.CreateRowGroup();

            // Write all columns defined in the schema
            // The schema fields determine which columns to write
            foreach (var field in _inferredSchema!.Fields)
            {
                await WriteColumnAsync(groupWriter, field.Name, cancellationToken);
            }

            var rowCount = _rowBuffer.Count;
            LogWroteRowGroup(_logger, rowCount);

            // Clear buffer for next batch
            _rowBuffer.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing Parquet batch");
            throw;
        }
    }

    private void InitializeWriter()
    {
        // Use provided schema or create default schema
        var schema = _providedSchema ?? CreateDefaultSchema();
        _inferredSchema = schema;

        // Create Parquet writer
        _parquetWriter = ParquetWriter.CreateAsync(schema, _parquetStream).GetAwaiter().GetResult();

        var schemaDescription = schema.ToString();
        LogInitializedWriter(_logger, schemaDescription);
    }

    private ParquetSchema CreateDefaultSchema()
    {
        // Simple schema: resourceType (string), id (string), rawResource (string)
        var fields = new DataField[]
        {
            new DataField<string>("resourceType"),
            new DataField<string>("id"),
            new DataField<string>("rawResource")
        };

        return new ParquetSchema(fields);
    }

    private async Task WriteColumnAsync(ParquetRowGroupWriter groupWriter, string columnName, CancellationToken cancellationToken)
    {
        // Get the field from schema
        var field = _inferredSchema!.Fields.First(f => f.Name == columnName);

        // Extract values from the row buffer
        var rawValues = _rowBuffer
            .Select(row => row.TryGetValue(columnName, out var value) ? value : null)
            .ToArray();

        // Determine expected SQL type from the column type map (passed from ViewDefinition schema)
        // Fallback to inference from field type if no map is available
        var sqlType = _columnTypeMap?.TryGetValue(columnName, out var type) == true
            ? type?.ToUpperInvariant()
            : null;

        // If no type mapping, infer from field's generic argument
        if (string.IsNullOrEmpty(sqlType))
        {
            var fieldType = field.GetType();
            var genericTypeArg = fieldType.GetGenericArguments().FirstOrDefault();
            sqlType = genericTypeArg?.Name switch
            {
                "String" => "STRING",
                "Boolean" => "BOOLEAN",
                "DateTime" => "DATETIME",
                "DateTimeOffset" => "DATETIME",
                "Int32" => "INTEGER",
                "Decimal" => "DECIMAL",
                _ => "STRING"
            };
        }

        // Write column based on SQL type (from schema, not reflection)
        try
        {
            await WriteColumnBySqlTypeAsync(groupWriter, field, columnName, rawValues, sqlType, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to write column {ColumnName} as type {SqlType}, falling back to string",
                columnName,
                sqlType);

            try
            {
                // Fallback to string
                var dataField = (DataField<string>)field;
                var stringValues = rawValues.Select(v => v?.ToString()).ToArray();
                var column = new DataColumn(dataField, stringValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(
                    fallbackEx,
                    "Failed to write column {ColumnName} even as string fallback",
                    columnName);
                throw;
            }
        }
    }

    private async Task WriteColumnBySqlTypeAsync(
        ParquetRowGroupWriter groupWriter,
        Field field,
        string columnName,
        object?[] rawValues,
        string sqlType,
        CancellationToken cancellationToken)
    {
        switch (sqlType)
        {
            case "STRING":
            {
                var dataField = (DataField<string>)field;
                var stringValues = rawValues.Select(v => v?.ToString()).ToArray();
                var column = new DataColumn(dataField, stringValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
                break;
            }

            case "BOOLEAN":
            {
                var dataField = (DataField<bool?>)field;
                var boolValues = rawValues.Select(v => v switch
                {
                    null => (bool?)null,
                    bool b => b,
                    _ => bool.TryParse(v.ToString(), out var result) ? result : (bool?)null
                }).ToArray();
                var column = new DataColumn(dataField, boolValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
                break;
            }

            case "DATE":
            case "DATETIME":
            {
                if (sqlType == "DATE")
                {
                    var dataField = (DataField<DateTime?>)field;
                    var dateTimeValues = rawValues.Select(v => v switch
                    {
                        null => (DateTime?)null,
                        DateTime dt => dt,
                        _ => DateTime.TryParse(v.ToString(), out var result) ? result : (DateTime?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, dateTimeValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                }
                else
                {
                    var dataField = (DataField<DateTimeOffset?>)field;
                    var dateTimeOffsetValues = rawValues.Select(v => v switch
                    {
                        null => (DateTimeOffset?)null,
                        DateTimeOffset dto => dto,
                        DateTime dt => new DateTimeOffset(dt),
                        _ => DateTimeOffset.TryParse(v.ToString(), out var result) ? result : (DateTimeOffset?)null
                    }).ToArray();
                    var column = new DataColumn(dataField, dateTimeOffsetValues);
                    await groupWriter.WriteColumnAsync(column, cancellationToken);
                }
                break;
            }

            case "INTEGER":
            {
                var dataField = (DataField<int?>)field;
                var intValues = rawValues.Select(v => v switch
                {
                    null => (int?)null,
                    int i => i,
                    _ => int.TryParse(v.ToString(), out var result) ? result : (int?)null
                }).ToArray();
                var column = new DataColumn(dataField, intValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
                break;
            }

            case "DECIMAL":
            {
                var dataField = (DataField<decimal?>)field;
                var decimalValues = rawValues.Select(v => v switch
                {
                    null => (decimal?)null,
                    decimal d => d,
                    _ => decimal.TryParse(v.ToString(), out var result) ? result : (decimal?)null
                }).ToArray();
                var column = new DataColumn(dataField, decimalValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
                break;
            }

            default:
            {
                // Unknown type - convert to string
                var dataField = (DataField<string>)field;
                var stringValues = rawValues.Select(v => v?.ToString()).ToArray();
                var column = new DataColumn(dataField, stringValues);
                await groupWriter.WriteColumnAsync(column, cancellationToken);
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "ViewDefinition export progress: {ResourcesProcessed} resources processed, {RowsGenerated} rows generated, {Rate:F1} resources/sec")]
    private static partial void LogViewDefinitionExportProgress(ILogger logger, long resourcesProcessed, long rowsGenerated, double rate);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Uploaded Parquet file ({BytesWritten} bytes) to: {OutputPath}")]
    private static partial void LogUploadedParquetFile(ILogger logger, long bytesWritten, string outputPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "ViewDefinition export completed: {ResourcesProcessed} resources processed, {RowsGenerated} rows generated, {BytesWritten} bytes written")]
    private static partial void LogViewDefinitionExportCompleted(ILogger logger, long resourcesProcessed, long rowsGenerated, long bytesWritten);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wrote Parquet row group with {RowCount} rows")]
    private static partial void LogWroteRowGroup(ILogger logger, int rowCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initialized Parquet writer with schema: {SchemaDescription}")]
    private static partial void LogInitializedWriter(ILogger logger, string? schemaDescription);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType());
    }
}

/// <summary>
/// Factory for creating ParquetExportStreamWriter instances.
/// Creates writers that output Parquet format instead of NDJSON.
/// Optionally supports ViewDefinition transformation.
/// </summary>
public class ParquetExportStreamWriterFactory : IExportStreamWriterFactory
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ParquetSchema? _schema;
    private readonly int _rowsPerBatch;
    private readonly ISourceNavigator? _viewDefinitionNode;
    private readonly ISchema? _schemaProvider;

    public ParquetExportStreamWriterFactory(
        IBlobStorageClient blobStorage,
        ILoggerFactory loggerFactory,
        ParquetSchema? schema = null,
        int rowsPerBatch = ParquetExportStreamWriter.DefaultRowsPerBatch)
    {
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        if (rowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerBatch), "Rows per batch must be greater than zero");
        }

        _schema = schema;
        _rowsPerBatch = rowsPerBatch;
        _viewDefinitionNode = null;
        _schemaProvider = null;
    }

    /// <summary>
    /// Constructor for ViewDefinition-enabled factory.
    /// </summary>
    public ParquetExportStreamWriterFactory(
        IBlobStorageClient blobStorage,
        ILoggerFactory loggerFactory,
        ISourceNavigator viewDefinitionNode,
        ISchema schemaProvider,
        ParquetSchema? schema = null,
        int rowsPerBatch = ParquetExportStreamWriter.DefaultRowsPerBatch)
    {
        ArgumentNullException.ThrowIfNull(blobStorage);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(viewDefinitionNode);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        if (rowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerBatch), "Rows per batch must be greater than zero");
        }

        _blobStorage = blobStorage;
        _loggerFactory = loggerFactory;
        _schema = schema;
        _rowsPerBatch = rowsPerBatch;
        _viewDefinitionNode = viewDefinitionNode;
        _schemaProvider = schemaProvider;
    }

    public Task<IExportStreamWriter> CreateAsync(
        int tenantId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var logger = _loggerFactory.CreateLogger<ParquetExportStreamWriter>();
        IExportStreamWriter writer = new ParquetExportStreamWriter(
            _blobStorage,
            outputPath,
            logger,
            _schema,
            _rowsPerBatch,
            _viewDefinitionNode,
            _schemaProvider);
        return Task.FromResult(writer);
    }
}
