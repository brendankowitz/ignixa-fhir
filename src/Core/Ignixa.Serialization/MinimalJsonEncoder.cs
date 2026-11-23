// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace Ignixa.Serialization;

/// <summary>
/// A <see cref="JavaScriptEncoder"/> that matches Newtonsoft.Json's default string escaping.
/// </summary>
/// <remarks>
/// Escapes <c>"</c>, <c>\</c>, U+0000 to U+001F, U+0085, U+2028 and U+2029, using the short forms
/// <c>\" \\ \b \f \n \r \t</c> and otherwise <c>\u</c> with four lowercase hex digits. Every other
/// character, including <c>+</c>, <c>&lt;</c>, <c>&amp;</c>, non-ASCII and supplementary characters, is
/// written literally; ill-formed UTF-16 or UTF-8 is replaced with U+FFFD. Unlike
/// <see cref="JavaScriptEncoder.Default"/>, the output is not safe to embed in HTML.
/// </remarks>
public sealed class MinimalJsonEncoder : JavaScriptEncoder
{
    // Escaped characters, plus the lead bytes of U+0085 (C2 85) and U+2028/U+2029 (E2 80 A8/A9) in UTF-8.
    // Scanning for these, not for safe text, lets long ASCII and non-ASCII runs pass in one vectorized search.
    private static readonly SearchValues<char> _escapedChars =
        SearchValues.Create("\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000b\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\"\\\u0085\u2028\u2029");

    private static readonly SearchValues<byte> _escapedOrLeadBytes =
        SearchValues.Create([.. Enumerable.Range(0, 0x20).Select(b => (byte)b), (byte)'"', (byte)'\\', 0xC2, 0xE2]);

    // Printable ASCII that is never escaped. Most FHIR strings are entirely this, so one pass settles them.
    private const string SafeAscii =
        " !#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private static readonly SearchValues<char> _safeAsciiChars = SearchValues.Create(SafeAscii);

    private static readonly SearchValues<byte> _safeAsciiBytes = SearchValues.Create(Encoding.ASCII.GetBytes(SafeAscii));

    private MinimalJsonEncoder()
    {
    }

    /// <summary>Gets the shared instance.</summary>
    public static MinimalJsonEncoder Instance { get; } = new();

    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override bool WillEncode(int unicodeScalar) => IsEscaped(unicodeScalar);

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength) =>
        IndexOfFirstCharToEncode(new ReadOnlySpan<char>(text, textLength));

    /// <summary>
    /// Copies each run that needs no escaping in one block. The base implementation encodes everything
    /// after the first escaped character one scalar at a time, which is slow for narrative text.
    /// </summary>
    public override OperationStatus Encode(ReadOnlySpan<char> source, Span<char> destination, out int charsConsumed, out int charsWritten, bool isFinalBlock = true)
    {
        charsConsumed = 0;
        charsWritten = 0;
        while (charsConsumed < source.Length)
        {
            ReadOnlySpan<char> rest = source[charsConsumed..];
            Span<char> output = destination[charsWritten..];
            int clean = IndexOfFirstCharToEncode(rest);
            if (clean < 0)
            {
                clean = rest.Length;
            }

            if (clean > output.Length)
            {
                // The clean run is well-formed, so only a surrogate pair can straddle the cut.
                int fits = output.Length;
                if (fits > 0 && char.IsHighSurrogate(rest[fits - 1]))
                {
                    fits--;
                }

                rest[..fits].CopyTo(output);
                charsConsumed += fits;
                charsWritten += fits;
                return OperationStatus.DestinationTooSmall;
            }

            rest[..clean].CopyTo(output);
            charsConsumed += clean;
            charsWritten += clean;
            if (clean == rest.Length)
            {
                break;
            }

            // An escaped character or an ill-formed surrogate; a failed decode yields U+FFFD.
            OperationStatus decoded = Rune.DecodeFromUtf16(rest[clean..], out Rune rune, out int runeLength);
            if (decoded == OperationStatus.NeedMoreData && !isFinalBlock)
            {
                return OperationStatus.NeedMoreData;
            }

            if (!TryWriteScalar(rune, output[clean..], out int written))
            {
                return OperationStatus.DestinationTooSmall;
            }

            charsConsumed += runeLength;
            charsWritten += written;
        }

        return OperationStatus.Done;
    }

    /// <inheritdoc cref="Encode(ReadOnlySpan{char}, Span{char}, out int, out int, bool)"/>
    public override OperationStatus EncodeUtf8(ReadOnlySpan<byte> utf8Source, Span<byte> utf8Destination, out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
    {
        bytesConsumed = 0;
        bytesWritten = 0;
        while (bytesConsumed < utf8Source.Length)
        {
            ReadOnlySpan<byte> rest = utf8Source[bytesConsumed..];
            Span<byte> output = utf8Destination[bytesWritten..];
            int run = rest.IndexOfAny(_escapedOrLeadBytes);
            if (run < 0)
            {
                run = rest.Length;
            }

            if (run > 0 && Utf8.IsValid(rest[..run]))
            {
                int fits = Math.Min(run, output.Length);
                while (fits < run && fits > 0 && (rest[fits] & 0xC0) == 0x80)
                {
                    fits--;
                }

                rest[..fits].CopyTo(output);
                bytesConsumed += fits;
                bytesWritten += fits;
                if (fits < run)
                {
                    return OperationStatus.DestinationTooSmall;
                }

                continue;
            }

            // The byte that ended the run, or an ill-formed run, goes one scalar at a time. The whole run
            // is finished here, so a long ill-formed run is not validated again for each scalar.
            int end = bytesConsumed + Math.Max(run, 1);
            while (bytesConsumed < end)
            {
                OperationStatus decoded = Rune.DecodeFromUtf8(utf8Source[bytesConsumed..], out Rune rune, out int runeLength);
                if (decoded == OperationStatus.NeedMoreData && !isFinalBlock)
                {
                    return OperationStatus.NeedMoreData;
                }

                if (!TryWriteScalarUtf8(rune, utf8Destination[bytesWritten..], out int written))
                {
                    return OperationStatus.DestinationTooSmall;
                }

                bytesConsumed += runeLength;
                bytesWritten += written;
            }
        }

        return OperationStatus.Done;
    }

    private static bool IsEscaped(int unicodeScalar) =>
        unicodeScalar is < 0x20 or '"' or '\\' or 0x85 or 0x2028 or 0x2029;

    // The index of the first escaped character or ill-formed surrogate, or -1.
    private static int IndexOfFirstCharToEncode(ReadOnlySpan<char> span)
    {
        int index = span.IndexOfAnyExcept(_safeAsciiChars);
        if (index < 0)
        {
            return -1;
        }

        int escaped = span[index..].IndexOfAny(_escapedChars);
        escaped = escaped < 0 ? -1 : index + escaped;
        int limit = escaped < 0 ? span.Length : escaped;

        // A lone surrogate before the first escaped character is ill-formed and must go through the encoder to be replaced.
        while (true)
        {
            int surrogate = span[index..limit].IndexOfAnyInRange('\uD800', '\uDFFF');
            if (surrogate < 0)
            {
                return escaped;
            }

            index += surrogate;
            if (!char.IsHighSurrogate(span[index]) || index + 1 >= span.Length || !char.IsLowSurrogate(span[index + 1]))
            {
                return index;
            }

            index += 2;
        }
    }

    public override int FindFirstCharacterToEncodeUtf8(ReadOnlySpan<byte> utf8Text)
    {
        int index = utf8Text.IndexOfAnyExcept(_safeAsciiBytes);
        if (index < 0)
        {
            return -1;
        }

        // Ill-formed input is rare; handing the rest to the encoder replaces bad sequences.
        if (!Utf8.IsValid(utf8Text[index..]))
        {
            return index;
        }

        while (true)
        {
            int next = utf8Text[index..].IndexOfAny(_escapedOrLeadBytes);
            if (next < 0)
            {
                return -1;
            }

            index += next;
            if (Rune.DecodeFromUtf8(utf8Text[index..], out Rune rune, out int consumed) != OperationStatus.Done || WillEncode(rune.Value))
            {
                return index;
            }

            index += consumed;
        }
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten) =>
        TryWriteScalar(new Rune(unicodeScalar), new Span<char>(buffer, bufferLength), out numberOfCharactersWritten);

    private static bool TryWriteScalar(Rune rune, Span<char> destination, out int numberOfCharactersWritten)
    {
        int unicodeScalar = rune.Value;
        if (!IsEscaped(unicodeScalar))
        {
            return rune.TryEncodeToUtf16(destination, out numberOfCharactersWritten);
        }

        string? shortForm = unicodeScalar switch
        {
            '"' => "\\\"",
            '\\' => "\\\\",
            '\b' => "\\b",
            '\f' => "\\f",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => null,
        };

        if (shortForm is not null)
        {
            bool written = shortForm.TryCopyTo(destination);
            numberOfCharactersWritten = written ? shortForm.Length : 0;
            return written;
        }

        return destination.TryWrite(CultureInfo.InvariantCulture, $"\\u{unicodeScalar:x4}", out numberOfCharactersWritten);
    }

    private static bool TryWriteScalarUtf8(Rune rune, Span<byte> destination, out int numberOfBytesWritten)
    {
        if (!IsEscaped(rune.Value))
        {
            return rune.TryEncodeToUtf8(destination, out numberOfBytesWritten);
        }

        // Escapes are ASCII, so each char narrows to one byte.
        Span<char> escape = stackalloc char[6];
        TryWriteScalar(rune, escape, out int length);
        if (length > destination.Length)
        {
            numberOfBytesWritten = 0;
            return false;
        }

        for (int i = 0; i < length; i++)
        {
            destination[i] = (byte)escape[i];
        }

        numberOfBytesWritten = length;
        return true;
    }
}
