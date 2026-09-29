using System.Buffers.Binary;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

internal static class StrictGzipArchive
{
    private const int BufferSize = 16 * 1024;

    internal static async Task<byte[]> ReadInputAsync(
        Stream input, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - output.Length + 1)),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0)
            {
                return output.ToArray();
            }
            if (output.Length + count > maximum)
            {
                throw new PackageExtractionException(new(PackageExtractionError.CompressedSizeLimit, Limit: maximum));
            }
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    internal static byte[] Expand(byte[] gzip, int maximum, CancellationToken cancellationToken)
    {
        // GZipStream does not expose the consumed member boundary. The existing SharpZipLib
        // inflater does, so a missing footer or extra member cannot masquerade as complete input.
        int headerLength = ReadHeader(gzip, cancellationToken);
        var inflater = new Inflater(noHeader: true);
        inflater.SetInput(gzip, headerLength, gzip.Length - headerLength);
        var crc = new Crc32();
        using var output = new MemoryStream();
        var buffer = new byte[BufferSize];
        try
        {
            while (!inflater.IsFinished)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = inflater.Inflate(buffer, 0, (int)Math.Min(buffer.Length, maximum - output.Length + 1));
                if (output.Length + count > maximum)
                {
                    throw new PackageExtractionException(new(PackageExtractionError.ExpandedSizeLimit, Limit: maximum));
                }
                if (count == 0 && !inflater.IsFinished)
                {
                    throw InvalidArchive();
                }
                crc.Update(new ArraySegment<byte>(buffer, 0, count));
                output.Write(buffer, 0, count);
            }
        }
        catch (SharpZipBaseException)
        {
            throw InvalidArchive();
        }

        cancellationToken.ThrowIfCancellationRequested();
        int footer = gzip.Length - inflater.RemainingInput;
        if (inflater.RemainingInput != 8 ||
            BinaryPrimitives.ReadUInt32LittleEndian(gzip.AsSpan(footer, 4)) != (uint)crc.Value ||
            BinaryPrimitives.ReadUInt32LittleEndian(gzip.AsSpan(footer + 4, 4)) != (uint)output.Length)
        {
            throw InvalidArchive();
        }
        return output.ToArray();
    }

    private static int ReadHeader(byte[] gzip, CancellationToken cancellationToken)
    {
        if (gzip.Length < 18 || gzip[0] != 0x1f || gzip[1] != 0x8b || gzip[2] != 8 || (gzip[3] & 0xe0) != 0)
        {
            throw InvalidArchive();
        }

        byte flags = gzip[3];
        int offset = 10;
        if ((flags & 4) != 0)
        {
            if (offset + 2 > gzip.Length - 8)
            {
                throw InvalidArchive();
            }
            int length = BinaryPrimitives.ReadUInt16LittleEndian(gzip.AsSpan(offset, 2));
            offset += 2 + length;
        }
        if ((flags & 8) != 0)
        {
            offset = SkipTerminatedField(gzip, offset, cancellationToken);
        }
        if ((flags & 16) != 0)
        {
            offset = SkipTerminatedField(gzip, offset, cancellationToken);
        }
        if (offset > gzip.Length - 8)
        {
            throw InvalidArchive();
        }
        if ((flags & 2) != 0)
        {
            if (offset + 2 > gzip.Length - 8)
            {
                throw InvalidArchive();
            }
            var crc = new Crc32();
            crc.Update(new ArraySegment<byte>(gzip, 0, offset));
            if (BinaryPrimitives.ReadUInt16LittleEndian(gzip.AsSpan(offset, 2)) != (ushort)crc.Value)
            {
                throw InvalidArchive();
            }
            offset += 2;
        }
        return offset;
    }

    private static int SkipTerminatedField(byte[] gzip, int offset, CancellationToken cancellationToken)
    {
        while (offset < gzip.Length - 8)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (gzip[offset++] == 0)
            {
                return offset;
            }
        }
        throw InvalidArchive();
    }

    private static PackageExtractionException InvalidArchive() => new(new(PackageExtractionError.InvalidArchive));
}
