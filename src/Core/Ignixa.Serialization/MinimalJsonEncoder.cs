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

    public override bool WillEncode(int unicodeScalar) =>
        unicodeScalar is < 0x20 or '"' or '\\' or 0x85 or 0x2028 or 0x2029;

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        ReadOnlySpan<char> span = new(text, textLength);
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

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        Span<char> destination = new(buffer, bufferLength);
        if (!WillEncode(unicodeScalar))
        {
            return new Rune(unicodeScalar).TryEncodeToUtf16(destination, out numberOfCharactersWritten);
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
}
