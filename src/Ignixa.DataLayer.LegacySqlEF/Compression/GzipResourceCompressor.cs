// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO.Compression;
using System.Text;

namespace Ignixa.DataLayer.LegacySqlEF.Compression;

/// <summary>
/// Compresses and decompresses FHIR resource JSON using Gzip.
/// Provides ~70% storage reduction for typical FHIR resources.
/// </summary>
public class GzipResourceCompressor
{
    /// <summary>
    /// Compresses JSON string to byte array using Gzip.
    /// </summary>
    /// <param name="json">The JSON string to compress.</param>
    /// <returns>Compressed byte array.</returns>
    public byte[] Compress(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);

        var bytes = Encoding.UTF8.GetBytes(json);
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    /// Decompresses byte array to JSON string using Gzip.
    /// </summary>
    /// <param name="compressedData">The compressed byte array.</param>
    /// <returns>Decompressed JSON string.</returns>
    public string Decompress(byte[] compressedData)
    {
        ArgumentNullException.ThrowIfNull(compressedData);

        if (compressedData.Length == 0)
        {
            return string.Empty;
        }

        using var inputStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    public ReadOnlyMemory<byte> DecompressBytes(byte[] compressedData)
    {
        ArgumentNullException.ThrowIfNull(compressedData);

        if (compressedData.Length == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        using var inputStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);

        return outputStream.ToArray();
    }
}
