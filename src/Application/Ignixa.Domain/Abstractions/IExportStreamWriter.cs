using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Abstraction for streaming FHIR resources to a file during export.
/// Writes resources as NDJSON (newline-delimited JSON) directly to blob storage without buffering the entire result set.
/// </summary>
public interface IExportStreamWriter : IAsyncDisposable
{
    /// <summary>
    /// Gets the total number of bytes written so far.
    /// Updated after each WriteResourceAsync and FlushAsync call.
    /// </summary>
    long BytesWritten { get; }

    /// <summary>
    /// Writes a single FHIR resource to the export file asynchronously.
    /// The resource bytes are written directly without re-serialization.
    /// Internally buffered and periodically flushed to blob storage.
    /// </summary>
    /// <param name="resource">The resource entry result containing raw resource bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteResourceAsync(SearchEntryResult resource, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes all buffered data to blob storage immediately.
    /// Called at the end of export processing to ensure all data is persisted.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Factory for creating IExportStreamWriter instances.
/// Abstracts the creation of export writers for different storage backends.
/// </summary>
public interface IExportStreamWriterFactory
{
    /// <summary>
    /// Creates a new stream writer for export.
    /// </summary>
    /// <param name="tenantId">The tenant ID for multi-tenancy isolation.</param>
    /// <param name="outputPath">The blob storage path where output will be written (e.g., "tenant/1/export/job123/Patient.ndjson").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new IExportStreamWriter instance.</returns>
    Task<IExportStreamWriter> CreateAsync(int tenantId, string outputPath, CancellationToken cancellationToken = default);
}
