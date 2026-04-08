// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Ignixa.Domain.Abstractions;

namespace Ignixa.DataLayer.BlobStorage;

/// <summary>
/// Composite factory that creates either NDJSON or Parquet writers based on output path file extension.
/// Determines format by inspecting the file extension:
/// - .ndjson → BlobStorageExportStreamWriter (NDJSON format)
/// - .parquet → ParquetExportStreamWriter (Parquet format)
/// - Default/unknown → BlobStorageExportStreamWriter (backward compatibility)
/// </summary>
public partial class CompositeExportStreamWriterFactory : IExportStreamWriterFactory
{
    private readonly IBlobStorageClient _blobStorage;
    private readonly ILoggerFactory _loggerFactory;
    private readonly RecyclableMemoryStreamManager _memoryManager;
    private readonly int _ndjsonBufferSizeBytes;
    private readonly int _parquetRowsPerBatch;
    private readonly ILogger<CompositeExportStreamWriterFactory> _logger;

    public CompositeExportStreamWriterFactory(
        IBlobStorageClient blobStorage,
        ILoggerFactory loggerFactory,
        int ndjsonBufferSizeBytes = BlobStorageExportStreamWriter.DefaultBufferSizeBytes,
        int parquetRowsPerBatch = ParquetExportStreamWriter.DefaultRowsPerBatch)
    {
        _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        if (ndjsonBufferSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ndjsonBufferSizeBytes), "Buffer size must be greater than zero");
        }

        if (parquetRowsPerBatch <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parquetRowsPerBatch), "Rows per batch must be greater than zero");
        }

        _ndjsonBufferSizeBytes = ndjsonBufferSizeBytes;
        _parquetRowsPerBatch = parquetRowsPerBatch;

        // Initialize shared memory manager for NDJSON writers
        // Parquet writer uses its own internal buffering, so doesn't need the memory manager
        _memoryManager = new RecyclableMemoryStreamManager();

        _logger = _loggerFactory.CreateLogger<CompositeExportStreamWriterFactory>();
    }

    public Task<IExportStreamWriter> CreateAsync(
        int tenantId,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        // Determine format from file extension
        var extension = Path.GetExtension(outputPath).ToUpperInvariant();

        IExportStreamWriter writer = extension switch
        {
            ".PARQUET" => CreateParquetWriter(outputPath),
            ".NDJSON" or "" or _ => CreateNdjsonWriter(outputPath)
        };

        var writerType = writer.GetType().Name;
        LogCreatedWriter(_logger, writerType, outputPath);

        return Task.FromResult(writer);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created {WriterType} for output path: {OutputPath}")]
    private static partial void LogCreatedWriter(ILogger logger, string writerType, string outputPath);

    private IExportStreamWriter CreateParquetWriter(string outputPath)
    {
        var logger = _loggerFactory.CreateLogger<ParquetExportStreamWriter>();
        return new ParquetExportStreamWriter(
            _blobStorage,
            outputPath,
            logger,
            schema: null,  // Use default schema (resourceType, id, rawResource)
            _parquetRowsPerBatch);
    }

    private IExportStreamWriter CreateNdjsonWriter(string outputPath)
    {
        var logger = _loggerFactory.CreateLogger<BlobStorageExportStreamWriter>();
        return new BlobStorageExportStreamWriter(
            _blobStorage,
            outputPath,
            _memoryManager,
            logger,
            _ndjsonBufferSizeBytes);
    }
}
