namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Abstraction for blob storage operations.
/// Supports local filesystem and cloud storage (Azure Blob, S3, etc.).
/// </summary>
public interface IBlobStorageClient
{
    /// <summary>
    /// Writes content to a blob asynchronously.
    /// </summary>
    /// <param name="path">Relative path to the blob (e.g., "tenant/1/export/job123/Patient.ndjson").</param>
    /// <param name="content">Stream containing the content to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WriteBlobAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends content to an existing blob, or creates it if it doesn't exist.
    /// Used for streaming export where chunks are written incrementally.
    /// </summary>
    /// <param name="path">Relative path to the blob.</param>
    /// <param name="content">Stream containing the content to append.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AppendBlobAsync(string path, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads content from a blob asynchronously.
    /// </summary>
    /// <param name="path">Relative path to the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the blob content.</returns>
    Task<Stream> ReadBlobAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob asynchronously.
    /// </summary>
    /// <param name="path">Relative path to the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteBlobAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a blob exists.
    /// </summary>
    /// <param name="path">Relative path to the blob.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the blob exists, false otherwise.</returns>
    Task<bool> BlobExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all blobs under a given path prefix.
    /// </summary>
    /// <param name="pathPrefix">Prefix to filter blobs (e.g., "tenant/1/export/job123/").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of blob paths.</returns>
    Task<List<string>> ListBlobsAsync(string pathPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a public or SAS URL for a blob (used in FHIR $export manifest).
    /// </summary>
    /// <param name="path">Relative path to the blob.</param>
    /// <param name="expiresIn">Optional expiration duration for SAS URLs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Absolute URL to access the blob.</returns>
    Task<string> GetBlobUrlAsync(string path, TimeSpan? expiresIn = null, CancellationToken cancellationToken = default);
}
