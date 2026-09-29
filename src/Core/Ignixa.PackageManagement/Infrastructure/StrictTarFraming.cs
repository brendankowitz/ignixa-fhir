using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Ignixa.PackageManagement.Models;

namespace Ignixa.PackageManagement.Infrastructure;

internal static class StrictTarFraming
{
    private const int BlockSize = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static (int Entries, long PayloadBytes, List<StrictTarFrame> Frames) Validate(
        byte[] tar, PackageExtractionLimits limits, CancellationToken cancellationToken)
    {
        int offset = 0;
        int entries = 0;
        long payloadBytes = 0;
        bool pendingMetadata = false;
        bool pendingGnuMetadata = false;
        string? pendingPath = null;
        var frames = new List<StrictTarFrame>();
        while (offset <= tar.Length - BlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<byte> header = tar.AsSpan(offset, BlockSize);
            if (IsZero(header))
            {
                if (pendingMetadata || tar.Length - offset < 2 * BlockSize || tar.Length % BlockSize != 0)
                {
                    throw Failure(PackageExtractionError.InvalidArchive, entries + 1);
                }
                VerifyZero(tar, offset, tar.Length - offset, entries + 1, cancellationToken);
                return (entries, payloadBytes, frames);
            }

            entries++;
            if (entries > limits.MaxArchiveEntries)
            {
                throw Failure(PackageExtractionError.EntryCountLimit, entries, limits.MaxArchiveEntries);
            }
            ValidateChecksum(header, entries);
            long size = ReadSize(header.Slice(124, 12), entries);
            if (size > limits.MaxEntryBytes)
            {
                throw Failure(PackageExtractionError.EntrySizeLimit, entries, limits.MaxEntryBytes);
            }

            byte type = header[156];
            if (type is not (0 or (byte)'0' or (byte)'5' or (byte)'x' or (byte)'L'))
            {
                throw Failure(PackageExtractionError.UnsupportedEntryType, entries);
            }
            ValidateNumericAttributes(header, entries, pendingGnuMetadata);
            bool metadata = type is (byte)'x' or (byte)'L';
            if ((metadata && pendingMetadata) || (type == (byte)'5' && size != 0))
            {
                throw Failure(PackageExtractionError.InvalidArchive, entries);
            }
            pendingMetadata = metadata;
            pendingGnuMetadata = type == (byte)'L';
            long paddedSize = (size + BlockSize - 1) / BlockSize * BlockSize;
            if (offset + BlockSize + paddedSize > tar.Length)
            {
                throw Failure(PackageExtractionError.InvalidArchive, entries);
            }
            int payloadOffset = offset + BlockSize;
            if (type == (byte)'x')
            {
                pendingPath = ValidatePax(tar.AsSpan(payloadOffset, (int)size), entries, cancellationToken);
            }
            else if (type == (byte)'L')
            {
                pendingPath = ReadPath(tar.AsSpan(payloadOffset, (int)size), entries);
            }
            else
            {
                string path = pendingPath ?? ReadHeaderPath(header, entries);
                frames.Add(new(entries, payloadOffset, size, type == (byte)'5', path));
                pendingPath = null;
            }
            VerifyZero(tar, payloadOffset + (int)size, (int)(paddedSize - size), entries, cancellationToken);
            payloadBytes += size;
            offset = (int)(payloadOffset + paddedSize);
        }
        throw Failure(PackageExtractionError.InvalidArchive, entries + 1);
    }

    private static void ValidateChecksum(ReadOnlySpan<byte> header, int entry)
    {
        long expected = ReadSize(header.Slice(148, 8), entry, allowBinary: false);
        int sum = 0;
        for (int index = 0; index < BlockSize; index++)
        {
            sum += index is >= 148 and < 156 ? ' ' : header[index];
        }
        if (expected != sum)
        {
            throw Failure(PackageExtractionError.InvalidArchive, entry);
        }
    }

    private static long ReadSize(ReadOnlySpan<byte> field, int entry, bool allowBinary = true)
    {
        // Positive GNU base-256 and POSIX octal sizes only. Negative sizes cannot frame payloads.
        long value = 0;
        if (allowBinary && field[0] == 0x80)
        {
            foreach (byte digit in field[1..])
            {
                if (value > (long.MaxValue - digit) / 256)
                {
                    throw Failure(PackageExtractionError.InvalidArchive, entry);
                }
                value = value * 256 + digit;
            }
            return value;
        }
        // A NUL terminates octal digits; trimming it from the front disagrees with net10.
        // Only leading spaces and a suffix of NUL/space padding are unambiguous.
        field = field.TrimStart((byte)' ');
        bool terminated = false;
        foreach (byte digit in field)
        {
            if (digit is 0 or (byte)' ')
            {
                terminated = true;
                continue;
            }
            if (terminated || digit is < (byte)'0' or > (byte)'7' || value > (long.MaxValue - 7) / 8)
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            value = value * 8 + digit - '0';
        }
        return value;
    }

    private static void ValidateNumericAttributes(ReadOnlySpan<byte> header, int entry, bool afterGnuMetadata)
    {
        ReadNumeric(header.Slice(100, 8), entry, int.MinValue, int.MaxValue);
        ReadNumeric(header.Slice(108, 8), entry, int.MinValue, int.MaxValue);
        ReadNumeric(header.Slice(116, 8), entry, int.MinValue, int.MaxValue);
        ValidateTimestamp(header.Slice(136, 12), entry);
        // Devices are rejected before TarReader; their device numbers are never consumed.
        // Of the GNU auxiliary fields, TarReader consumes only access/change timestamps.
        ReadOnlySpan<byte> magic = header.Slice(257, 6);
        if (magic.SequenceEqual("ustar "u8) ||
            ((afterGnuMetadata || header[156] == (byte)'L') && !IsZero(magic)))
        {
            ValidateTimestamp(header.Slice(345, 12), entry);
            ValidateTimestamp(header.Slice(357, 12), entry);
        }
    }

    private static void ValidateTimestamp(ReadOnlySpan<byte> field, int entry) =>
        ReadNumeric(field, entry, DateTimeOffset.MinValue.ToUnixTimeSeconds(), DateTimeOffset.MaxValue.ToUnixTimeSeconds());

    private static void ReadNumeric(ReadOnlySpan<byte> field, int entry, long minimum, long maximum)
    {
        long value;
        if (field[0] == 0xff)
        {
            value = BinaryPrimitives.ReadInt64BigEndian(field[^8..]);
            if (field[..^8].IndexOfAnyExcept((byte)0xff) >= 0 || value >= 0)
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
        }
        else
        {
            value = ReadSize(field, entry);
        }
        if (value < minimum || value > maximum)
        {
            throw Failure(PackageExtractionError.InvalidArchive, entry);
        }
    }

    private static string? ValidatePax(ReadOnlySpan<byte> payload, int entry, CancellationToken cancellationToken)
    {
        // This is a framing gate, not a tar reader. Reject structural overrides that would let
        // TarReader consume a different byte layout than the physical headers validated above.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? path = null;
        while (!payload.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int separator = payload.IndexOf((byte)' ');
            if (separator <= 0 || separator > 10 ||
                !int.TryParse(Encoding.ASCII.GetString(payload[..separator]), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int length) ||
                length > payload.Length || length <= separator + 3 || payload[length - 1] != '\n')
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            ReadOnlySpan<byte> attribute = payload.Slice(separator + 1, length - separator - 2);
            int equals = attribute.IndexOf((byte)'=');
            // net9 splits records at newlines, rather than respecting the byte length.
            if (equals <= 0 || attribute.IndexOfAny((byte)'\n', (byte)'\r', (byte)0) >= 0)
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            string key;
            try
            {
                key = StrictUtf8.GetString(attribute[..equals]);
                if (key == "path")
                {
                    path = StrictUtf8.GetString(attribute[(equals + 1)..]);
                }
                else
                {
                    _ = StrictUtf8.GetCharCount(attribute[(equals + 1)..]);
                }
            }
            catch (DecoderFallbackException)
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            if (key.Any(char.IsWhiteSpace) || !keys.Add(key))
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            if (key is "size" or "linkpath" or "hdrcharset" ||
                key.StartsWith("GNU.sparse", StringComparison.Ordinal) ||
                key.StartsWith("SCHILY.", StringComparison.Ordinal))
            {
                throw Failure(PackageExtractionError.UnsupportedEntryType, entry);
            }
            payload = payload[length..];
        }
        return path;
    }

    private static string ReadHeaderPath(ReadOnlySpan<byte> header, int entry)
    {
        string name = ReadPath(header[..100], entry);
        if (header.Slice(257, 6).SequenceEqual("ustar\0"u8))
        {
            string prefix = ReadPath(header.Slice(345, 155), entry);
            if (prefix.Length > 0)
            {
                return prefix + "/" + name;
            }
        }
        return name;
    }

    private static string ReadPath(ReadOnlySpan<byte> field, int entry)
    {
        int terminator = field.IndexOf((byte)0);
        if (terminator >= 0)
        {
            if (!IsZero(field[terminator..]))
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            field = field[..terminator];
        }
        try
        {
            return StrictUtf8.GetString(field);
        }
        catch (DecoderFallbackException)
        {
            throw Failure(PackageExtractionError.UnsafePath, entry);
        }
    }

    private static bool IsZero(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyExcept((byte)0) < 0;

    private static void VerifyZero(byte[] tar, int offset, int length, int entry, CancellationToken cancellationToken)
    {
        while (length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(length, 16 * 1024);
            if (!IsZero(tar.AsSpan(offset, count)))
            {
                throw Failure(PackageExtractionError.InvalidArchive, entry);
            }
            offset += count;
            length -= count;
        }
    }

    private static PackageExtractionException Failure(PackageExtractionError code, int entry, long? limit = null) =>
        new(new(code, entry, limit));
}
