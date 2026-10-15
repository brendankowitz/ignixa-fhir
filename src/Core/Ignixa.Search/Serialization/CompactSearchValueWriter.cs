// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using System.Text.Json;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Writes a search value's properties in the compact format (<see cref="SearchValueConstants"/>) into the JSON object the
/// caller has opened. The format is lossless: <see cref="CompactSearchValueReader"/> rebuilds a value that every search,
/// sort and include reads exactly as it was indexed, including token text case, identifier types, reference kinds,
/// min/max sort flags, canonical version and fragment, the empty-versus-absent distinction of systems and codes, and
/// composites component by component.
/// </summary>
public class CompactSearchValueWriter : ISearchValueVisitor
{
    private readonly Utf8JsonWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompactSearchValueWriter"/> class.
    /// </summary>
    /// <param name="writer">The JSON writer, positioned inside the object that will hold the value.</param>
    public CompactSearchValueWriter(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        _writer = writer;
    }

    /// <summary>Writes <paramref name="value"/> as one complete JSON object.</summary>
    public static void WriteObject(Utf8JsonWriter writer, ISearchValue value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        value.AcceptVisitor(new CompactSearchValueWriter(writer));
        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public void Visit(CompositeIndexSearchValue composite)
    {
        ArgumentNullException.ThrowIfNull(composite);

        // Components are written as they are, not as their Cartesian product: a component with several values (a
        // CodeableConcept with two codings) keeps them together, which is what the in-memory matcher reads.
        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.CompositeKind);
        _writer.WriteStartArray(SearchValueConstants.ComponentsName);
        foreach (IReadOnlyList<ISearchValue> component in composite.Components)
        {
            _writer.WriteStartArray();
            foreach (ISearchValue member in component)
            {
                WriteObject(_writer, member);
            }

            _writer.WriteEndArray();
        }

        _writer.WriteEndArray();
    }

    /// <inheritdoc />
    public void Visit(DateTimeSearchValue dateTime)
    {
        ArgumentNullException.ThrowIfNull(dateTime);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.DateTimeKind);

        // "o" (ISO 8601 round-trip) keeps every fractional digit, so the reloaded bounds compare equal.
        _writer.WriteString(SearchValueConstants.DateTimeStartName, dateTime.Start.ToString("o", CultureInfo.InvariantCulture));
        _writer.WriteString(SearchValueConstants.DateTimeEndName, dateTime.End.ToString("o", CultureInfo.InvariantCulture));

        // ToString is the original partial date when the value was built from one (2020-01), and start-end otherwise;
        // persisting it only when it differs keeps ToString identical after a reload.
        string text = dateTime.ToString();
        if (!string.Equals(text, CompactSearchValueReader.DefaultDateText(dateTime.Start, dateTime.End), StringComparison.Ordinal))
        {
            _writer.WriteString(SearchValueConstants.DateTimeTextName, text);
        }

        WriteFlags(dateTime.IsMin, dateTime.IsMax);
    }

    /// <inheritdoc />
    public void Visit(NumberSearchValue number)
    {
        ArgumentNullException.ThrowIfNull(number);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.NumberKind);
        WriteOptional(SearchValueConstants.LowNumberName, number.Low);
        WriteOptional(SearchValueConstants.HighNumberName, number.High);
    }

    /// <inheritdoc />
    public void Visit(QuantitySearchValue quantity)
    {
        ArgumentNullException.ThrowIfNull(quantity);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.QuantityKind);
        WriteOptional(SearchValueConstants.SystemName, quantity.System);
        WriteOptional(SearchValueConstants.CodeName, quantity.Code);
        WriteOptional(SearchValueConstants.LowQuantityName, quantity.Low);
        WriteOptional(SearchValueConstants.HighQuantityName, quantity.High);
    }

    /// <inheritdoc />
    public void Visit(ReferenceSearchValue reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.ReferenceKind);
        _writer.WriteString(SearchValueConstants.ReferenceKindName, reference.Kind.ToString());
        WriteOptional(SearchValueConstants.ReferenceBaseUriName, reference.BaseUri?.OriginalString);
        WriteOptional(SearchValueConstants.ReferenceResourceTypeName, reference.ResourceType);
        _writer.WriteString(SearchValueConstants.ReferenceResourceIdName, reference.ResourceId);
    }

    /// <inheritdoc />
    public void Visit(StringSearchValue s)
    {
        ArgumentNullException.ThrowIfNull(s);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.StringKind);
        _writer.WriteString(SearchValueConstants.StringName, s.String);
        WriteFlags(s.IsMin, s.IsMax);
    }

    /// <inheritdoc />
    public void Visit(TokenSearchValue token)
    {
        ArgumentNullException.ThrowIfNull(token);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.TokenKind);
        WriteOptional(SearchValueConstants.SystemName, token.System);
        WriteOptional(SearchValueConstants.CodeName, token.Code);
        WriteOptional(SearchValueConstants.TextName, token.Text);
        WriteOptional(SearchValueConstants.IdentifierTypeSystemName, token.IdentifierTypeSystem);
        WriteOptional(SearchValueConstants.IdentifierTypeCodeName, token.IdentifierTypeCode);
    }

    /// <inheritdoc />
    public void Visit(OfTypeTokenSearchValue ofTypeToken)
    {
        throw new NotSupportedException("OfTypeTokenSearchValue is for search queries only and should not be serialized.");
    }

    /// <inheritdoc />
    public void Visit(UriSearchValue uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        _writer.WriteString(SearchValueConstants.KindName, SearchValueConstants.UriKind);
        _writer.WriteString(SearchValueConstants.UriName, uri.Uri);
        WriteOptional(SearchValueConstants.UriVersionName, uri.Version);
        WriteOptional(SearchValueConstants.UriFragmentName, uri.Fragment);
    }

    private void WriteFlags(bool isMin, bool isMax)
    {
        if (isMin)
        {
            _writer.WriteBoolean(SearchValueConstants.IsMinName, true);
        }

        if (isMax)
        {
            _writer.WriteBoolean(SearchValueConstants.IsMaxName, true);
        }
    }

    private void WriteOptional(string name, string? value)
    {
        if (value is not null)
        {
            _writer.WriteString(name, value);
        }
    }

    private void WriteOptional(string name, decimal? value)
    {
        if (value is { } number)
        {
            _writer.WriteNumber(name, number);
        }
    }
}
