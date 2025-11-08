// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Implementation of IExportStreamWriter using Parquet format with blob storage.
/// Buffers rows in memory, writes row groups periodically, then uploads the complete Parquet file.
/// Unlike NDJSON (which appends incrementally), Parquet requires buffering and a single upload.
/// </summary>
public class ParquetExportStreamWriter : IExportStreamWriter
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly string _outputPath;
    private readonly ILogger<ParquetExportStreamWriter> _logger;
    private readonly ParquetSchema? _providedSchema;
    private readonly int _rowsPerBatch;
    private readonly List<Dictionary<string, object?>> _rowBuffer;
    private MemoryStream _parquetStream;
    private ParquetWriter? _parquetWriter;
    private ParquetSchema? _inferredSchema;
    private long _bytesWritten;
    private bool _disposed;

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
        int rowsPerBatch = DefaultRowsPerBatch)
    {
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (rowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rowsPerBatch), "Rows per batch must be greater than zero");
        }

        _providedSchema = schema;
        _rowsPerBatch = rowsPerBatch;
        _rowBuffer = new List<Dictionary<string, object?>>(_rowsPerBatch);
        _parquetStream = new MemoryStream();
        _bytesWritten = 0;
    }

    public async Task WriteResourceAsync(SearchEntryResult resource, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            // Parse resource bytes directly to ResourceJsonNode
            var resourceNode = JsonSourceNodeFactory.Parse(resource.ResourceBytes);

            if (resourceNode == null)
            {
                _logger.LogWarning(
                    "Failed to parse resource {ResourceType}/{ResourceId}, skipping",
                    resource.ResourceType,
                    resource.ResourceId);
                return;
            }

            // Create row dictionary with simple schema: resourceType, id, rawResource
            var row = new Dictionary<string, object?>
            {
                ["resourceType"] = resourceNode.ResourceType,
                ["id"] = resourceNode.Id,
                ["rawResource"] = resourceNode.SerializeToString()
            };

            // Add to buffer
            _rowBuffer.Add(row);

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

    /// <summary>
    /// Writes a pre-evaluated row dictionary to Parquet.
    /// Used by ViewDefinitionExportStreamWriter which evaluates ViewDefinitions before writing.
    /// </summary>
    /// <param name="row">Dictionary mapping column names to values</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task WriteRowAsync(Dictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _rowBuffer.Add(row);

        // Flush batch when buffer is full
        if (_rowBuffer.Count >= _rowsPerBatch)
        {
            await FlushBatchAsync(cancellationToken);
        }
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

                _logger.LogDebug(
                    "Uploaded Parquet file ({BytesWritten} bytes) to: {OutputPath}",
                    _bytesWritten,
                    _outputPath);
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

            // Write columns
            await WriteColumnAsync(groupWriter, "resourceType", cancellationToken);
            await WriteColumnAsync(groupWriter, "id", cancellationToken);
            await WriteColumnAsync(groupWriter, "rawResource", cancellationToken);

            _logger.LogDebug("Wrote Parquet row group with {RowCount} rows", _rowBuffer.Count);

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

        _logger.LogDebug("Initialized Parquet writer with schema: {Schema}", schema);
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
        // Extract column values from row buffer
        var values = _rowBuffer
            .Select(row => row.TryGetValue(columnName, out var value) ? value as string : null)
            .ToArray();

        // Create data column
        var column = new DataColumn(
            new DataField<string>(columnName),
            values);

        // Write column to row group
        await groupWriter.WriteColumnAsync(column, cancellationToken);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, GetType());
    }
}

/// <summary>
/// Factory for creating ParquetExportStreamWriter instances.
/// Creates writers that output Parquet format instead of NDJSON.
/// </summary>
public class ParquetExportStreamWriterFactory : IExportStreamWriterFactory
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ParquetSchema? _schema;
    private readonly int _rowsPerBatch;

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
            _rowsPerBatch);
        return Task.FromResult(writer);
    }
}
