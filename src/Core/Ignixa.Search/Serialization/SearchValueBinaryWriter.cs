// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Writes search values, and the strings around them, in a compact binary form that <see cref="SearchValueBinaryReader"/>
/// reads back losslessly: the same fields as the compact JSON format (<see cref="CompactSearchValueWriter"/>), with dates as
/// ticks instead of text and every string written once per stream and then named by its position.
/// </summary>
/// <remarks>
/// An index repeats the same systems, codes, reference targets and dates on most of its resources, so naming a string already
/// written costs a byte or two, and the reader hands back one instance per distinct string. Not thread-safe.
/// </remarks>
public sealed class SearchValueBinaryWriter : ISearchValueVisitor
{
    private readonly BinaryWriter _writer;
    private readonly Dictionary<string, int> _written = new(StringComparer.Ordinal);

    public SearchValueBinaryWriter(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
    }

    /// <summary>Writes <paramref name="value"/>, which may be null: its text the first time, its position in the stream's strings after.</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            _writer.Write7BitEncodedInt(SearchValueBinaryFormat.NullString);
            return;
        }

        if (_written.TryGetValue(value, out int position))
        {
            _writer.Write7BitEncodedInt(SearchValueBinaryFormat.FirstStringReference + position);
            return;
        }

        _written.Add(value, _written.Count);
        _writer.Write7BitEncodedInt(SearchValueBinaryFormat.NewString);
        _writer.Write(value);
    }

    /// <summary>Writes <paramref name="value"/> with its kind.</summary>
    /// <exception cref="NotSupportedException">The value is a query-only kind (<see cref="OfTypeTokenSearchValue"/>).</exception>
    public void WriteValue(ISearchValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        value.AcceptVisitor(this);
    }

    /// <inheritdoc />
    public void Visit(CompositeIndexSearchValue composite)
    {
        _writer.Write(SearchValueBinaryFormat.Composite);
        _writer.Write7BitEncodedInt(composite.Components.Count);
        foreach (IReadOnlyList<ISearchValue> component in composite.Components)
        {
            _writer.Write7BitEncodedInt(component.Count);
            foreach (ISearchValue member in component)
            {
                WriteValue(member);
            }
        }
    }

    /// <inheritdoc />
    public void Visit(DateTimeSearchValue dateTime)
    {
        _writer.Write(SearchValueBinaryFormat.DateTime);
        WriteInstant(dateTime.Start);
        WriteInstant(dateTime.End);

        // As in the JSON format: the original partial date only when ToString is not the range, so ToString survives a reload.
        string text = dateTime.ToString();
        WriteString(string.Equals(text, CompactSearchValueReader.DefaultDateText(dateTime.Start, dateTime.End), StringComparison.Ordinal) ? null : text);
        WriteFlags(dateTime.IsMin, dateTime.IsMax);
    }

    /// <inheritdoc />
    public void Visit(NumberSearchValue number)
    {
        _writer.Write(SearchValueBinaryFormat.Number);
        WriteDecimal(number.Low);
        WriteDecimal(number.High);
    }

    /// <inheritdoc />
    public void Visit(QuantitySearchValue quantity)
    {
        _writer.Write(SearchValueBinaryFormat.Quantity);
        WriteString(quantity.System);
        WriteString(quantity.Code);
        WriteDecimal(quantity.Low);
        WriteDecimal(quantity.High);
    }

    /// <inheritdoc />
    public void Visit(ReferenceSearchValue reference)
    {
        _writer.Write(SearchValueBinaryFormat.Reference);
        _writer.Write((byte)reference.Kind);
        WriteString(reference.BaseUri?.OriginalString);
        WriteString(reference.ResourceType);
        WriteString(reference.ResourceId);
    }

    /// <inheritdoc />
    public void Visit(StringSearchValue s)
    {
        _writer.Write(SearchValueBinaryFormat.String);
        WriteString(s.String);
        WriteFlags(s.IsMin, s.IsMax);
    }

    /// <inheritdoc />
    public void Visit(TokenSearchValue token)
    {
        _writer.Write(SearchValueBinaryFormat.Token);
        WriteString(token.System);
        WriteString(token.Code);
        WriteString(token.Text);
        WriteString(token.IdentifierTypeSystem);
        WriteString(token.IdentifierTypeCode);
    }

    /// <inheritdoc />
    public void Visit(OfTypeTokenSearchValue ofTypeToken) =>
        throw new NotSupportedException("OfTypeTokenSearchValue is for search queries only and should not be serialized.");

    /// <inheritdoc />
    public void Visit(UriSearchValue uri)
    {
        _writer.Write(SearchValueBinaryFormat.Uri);
        WriteString(uri.Uri);
        WriteString(uri.Version);
        WriteString(uri.Fragment);
    }

    private void WriteInstant(DateTimeOffset instant)
    {
        _writer.Write(instant.Ticks);
        _writer.Write((short)instant.Offset.TotalMinutes);
    }

    private void WriteDecimal(decimal? value)
    {
        _writer.Write(value.HasValue);
        if (value is { } number)
        {
            _writer.Write(number);
        }
    }

    private void WriteFlags(bool isMin, bool isMax) =>
        _writer.Write((byte)((isMin ? SearchValueBinaryFormat.IsMinFlag : 0) | (isMax ? SearchValueBinaryFormat.IsMaxFlag : 0)));
}
