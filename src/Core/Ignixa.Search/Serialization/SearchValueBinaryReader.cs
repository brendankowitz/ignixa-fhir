// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Reads what <see cref="SearchValueBinaryWriter"/> wrote, in the same order. Every value is rebuilt through its public
/// constructor and checked against what was stored, so input that was not written by the writer (or is truncated) fails with
/// <see cref="InvalidDataException"/> or <see cref="EndOfStreamException"/> instead of yielding a different value. Not thread-safe.
/// </summary>
public sealed class SearchValueBinaryReader
{
    private static readonly char[] EscapedCharacters = ['\\', ',', '$', '|'];

    private readonly BinaryReader _reader;
    private readonly List<string> _strings = [];
    private readonly Dictionary<string, PartialDateTime> _partialDates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Uri> _baseUris = new(StringComparer.Ordinal);

    public SearchValueBinaryReader(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <summary>Reads a string written by <see cref="SearchValueBinaryWriter.WriteString"/>; equal strings come back as one instance.</summary>
    /// <exception cref="InvalidDataException">The input names a string it has not written yet.</exception>
    public string? ReadString()
    {
        int tag = _reader.Read7BitEncodedInt();
        switch (tag)
        {
            case SearchValueBinaryFormat.NullString:
                return null;
            case SearchValueBinaryFormat.NewString:
                string value = _reader.ReadString();
                _strings.Add(value);
                return value;
            default:
                int position = tag - SearchValueBinaryFormat.FirstStringReference;
                return position >= 0 && position < _strings.Count
                    ? _strings[position]
                    : throw new InvalidDataException($"String {position} is named before it was written.");
        }
    }

    /// <summary>Reads a string that must be present.</summary>
    /// <exception cref="InvalidDataException">The string is null.</exception>
    public string ReadRequiredString(string what) => ReadString() ?? throw new InvalidDataException($"{what} is missing.");

    /// <summary>Reads one value written by <see cref="SearchValueBinaryWriter.WriteValue"/>.</summary>
    /// <exception cref="InvalidDataException">The input is not a value the writer wrote.</exception>
    public ISearchValue ReadValue() => ReadValue(inComposite: false);

    /// <summary>
    /// A count read from the input, which is at most the bytes left in it (every counted item takes at least one byte), so a
    /// damaged count fails as invalid data instead of allocating gigabytes.
    /// </summary>
    /// <exception cref="InvalidDataException">The count is negative or larger than what is left.</exception>
    public static int ReadCount(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int count = reader.Read7BitEncodedInt();
        long left = reader.BaseStream.CanSeek ? reader.BaseStream.Length - reader.BaseStream.Position : long.MaxValue;
        return count >= 0 && count <= left ? count : throw new InvalidDataException($"A count of {count} with {left} bytes left.");
    }

    private ISearchValue ReadValue(bool inComposite)
    {
        byte kind = _reader.ReadByte();
        if (inComposite && kind == SearchValueBinaryFormat.Composite)
        {
            // The indexer never nests composites; refusing them also bounds the recursion on damaged input.
            throw new InvalidDataException("A composite holds a composite.");
        }

        try
        {
            return kind switch
            {
                SearchValueBinaryFormat.Token => new TokenSearchValue(ReadString(), ReadString(), ReadString(), ReadString(), ReadString()),
                SearchValueBinaryFormat.Quantity => new QuantitySearchValue(ReadString(), ReadString(), ReadDecimal(), ReadDecimal()),
                SearchValueBinaryFormat.Number => new NumberSearchValue(ReadDecimal(), ReadDecimal()),
                SearchValueBinaryFormat.String => ReadStringValue(),
                SearchValueBinaryFormat.Reference => ReadReference(),
                SearchValueBinaryFormat.DateTime => ReadDate(),
                SearchValueBinaryFormat.Uri => ReadUri(),
                SearchValueBinaryFormat.Composite => ReadComposite(),
                _ => throw new InvalidDataException($"Unknown search value kind {kind}."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidDataException($"Invalid search value: {ex.Message}", ex);
        }
    }

    private StringSearchValue ReadStringValue()
    {
        string text = ReadRequiredString("A string value");
        (bool isMin, bool isMax) = ReadFlags();

        // StringSearchValue unescapes what it is given; text without escape characters is its own escaped form.
        string escaped = text.IndexOfAny(EscapedCharacters) < 0 ? text : text.EscapeSearchParameterValue();
        var value = new StringSearchValue(escaped) { IsMin = isMin, IsMax = isMax };
        return string.Equals(value.String, text, StringComparison.Ordinal)
            ? value
            : throw new InvalidDataException($"The string '{text}' cannot be reconstructed exactly.");
    }

    private ReferenceSearchValue ReadReference()
    {
        var kind = (ReferenceKind)_reader.ReadByte();
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException($"Unknown reference kind {(int)kind}.");
        }

        string? baseUri = ReadString();
        string? resourceType = ReadString();
        string resourceId = ReadRequiredString("A reference's id");
        return new ReferenceSearchValue(kind, baseUri is null ? null! : BaseUri(baseUri), resourceType!, resourceId);
    }

    private Uri BaseUri(string text)
    {
        if (!_baseUris.TryGetValue(text, out Uri? uri))
        {
            uri = new Uri(text, UriKind.RelativeOrAbsolute);
            _baseUris.Add(text, uri);
        }

        return uri;
    }

    private DateTimeSearchValue ReadDate()
    {
        DateTimeOffset start = ReadInstant();
        DateTimeOffset end = ReadInstant();
        string? text = ReadString();
        (bool isMin, bool isMax) = ReadFlags();

        DateTimeSearchValue date = text is null
            ? new DateTimeSearchValue(new PartialDateTime(start), new PartialDateTime(end))
            : new DateTimeSearchValue(PartialDate(text));

        // A rebuilt date must land on the stored range; anything else means the input is not what was written.
        if (date.Start != start || date.End != end)
        {
            throw new InvalidDataException($"The date '{text}' does not reproduce the stored range {start:o} to {end:o}.");
        }

        date.IsMin = isMin;
        date.IsMax = isMax;
        return date;
    }

    // PartialDateTime is immutable once built, so one parse serves every value written with the same text.
    private PartialDateTime PartialDate(string text)
    {
        if (!_partialDates.TryGetValue(text, out PartialDateTime? partial))
        {
            partial = PartialDateTime.Parse(text);
            if (!string.Equals(new DateTimeSearchValue(partial).ToString(), text, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The date '{text}' does not print as it was stored.");
            }

            _partialDates.Add(text, partial);
        }

        return partial;
    }

    private UriSearchValue ReadUri()
    {
        string uri = ReadRequiredString("A uri value");
        string? version = ReadString();
        string? fragment = ReadString();
        return new UriSearchValue(uri, separateCanonicalComponents: false) { Version = version!, Fragment = fragment! };
    }

    private CompositeIndexSearchValue ReadComposite()
    {
        int componentCount = ReadCount();
        List<IReadOnlyList<ISearchValue>> components = new(componentCount);
        for (int component = 0; component < componentCount; component++)
        {
            int memberCount = ReadCount();
            List<ISearchValue> members = new(memberCount);
            for (int member = 0; member < memberCount; member++)
            {
                members.Add(ReadValue(inComposite: true));
            }

            components.Add(members);
        }

        return new CompositeIndexSearchValue(components);
    }

    private int ReadCount() => ReadCount(_reader);

    private DateTimeOffset ReadInstant()
    {
        long ticks = _reader.ReadInt64();
        short offsetMinutes = _reader.ReadInt16();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private decimal? ReadDecimal() => _reader.ReadBoolean() ? _reader.ReadDecimal() : null;

    private (bool IsMin, bool IsMax) ReadFlags()
    {
        byte flags = _reader.ReadByte();
        if ((flags & ~(SearchValueBinaryFormat.IsMinFlag | SearchValueBinaryFormat.IsMaxFlag)) != 0)
        {
            throw new InvalidDataException($"Unknown flags {flags}.");
        }

        return ((flags & SearchValueBinaryFormat.IsMinFlag) != 0, (flags & SearchValueBinaryFormat.IsMaxFlag) != 0);
    }
}
