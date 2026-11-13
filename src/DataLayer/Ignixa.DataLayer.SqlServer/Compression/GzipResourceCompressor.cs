// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Compression;
using System.Runtime.InteropServices;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlServer.Compression;

/// <summary>
/// Compresses and decompresses FHIR resource JSON using Gzip.
/// Provides ~70% storage reduction for typical FHIR resources.
/// Uses RecyclableMemoryStream for efficient memory management.
/// </summary>
public class GzipResourceCompressor(RecyclableMemoryStreamManager memoryStreamManager)
{
    private readonly RecyclableMemoryStreamManager _memoryStreamManager = memoryStreamManager ?? throw new ArgumentNullException(nameof(memoryStreamManager));

    public byte[] SerializeAndCompress(ResourceJsonNode node)
    {
        using RecyclableMemoryStream outputStream = _memoryStreamManager.GetStream("gzip-compress");
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            node.SerializeToStream(gzipStream);
        }
        return outputStream.ToArray();
    }

    /// <summary>
    /// Compresses plain UTF-8 text with the same Gzip settings <see cref="SerializeAndCompress"/> uses for
    /// resource JSON. Used for semantic-search source passages (<c>dbo.VectorSearchParam.SourceTextCompressed</c>),
    /// which are not FHIR resources and so have no <see cref="ResourceJsonNode"/> to serialize.
    /// </summary>
    public byte[] CompressText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        using RecyclableMemoryStream outputStream = _memoryStreamManager.GetStream("gzip-compress-text");
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return outputStream.ToArray();
    }

    public ReadOnlyMemory<byte> DecompressBytes(ReadOnlyMemory<byte> compressedData)
    {
        if (compressedData.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        // Read the caller's array in place: copying the compressed payload into a pooled stream first
        // doubled the compressed bytes held per in-flight resource on every search and read.
        using Stream inputStream = MemoryMarshal.TryGetArray(compressedData, out ArraySegment<byte> segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : CopyToPooledStream(compressedData);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using RecyclableMemoryStream outputStream = _memoryStreamManager.GetStream("gzip-decompress-output");
        gzipStream.CopyTo(outputStream);

        return outputStream.ToArray();
    }

    private RecyclableMemoryStream CopyToPooledStream(ReadOnlyMemory<byte> data)
    {
        RecyclableMemoryStream stream = _memoryStreamManager.GetStream("gzip-decompress-input");
        stream.Write(data.Span);
        stream.Position = 0;
        return stream;
    }
}
