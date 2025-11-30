// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Compression;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.IO;

namespace Ignixa.DataLayer.SqlEntityFramework.Compression;

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
        using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);
        node.SerializeToStream(gzipStream);
        gzipStream.Flush();
        return outputStream.ToArray();
    }

    public ReadOnlyMemory<byte> DecompressBytes(ReadOnlyMemory<byte> compressedData)
    {
        if (compressedData.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        using RecyclableMemoryStream inputStream = _memoryStreamManager.GetStream("gzip-decompress-input");
        inputStream.Write(compressedData.Span);
        inputStream.Position = 0;
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using RecyclableMemoryStream outputStream = _memoryStreamManager.GetStream("gzip-decompress-output");
        gzipStream.CopyTo(outputStream);

        return outputStream.ToArray();
    }
}
