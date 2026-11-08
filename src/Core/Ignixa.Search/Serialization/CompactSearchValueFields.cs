// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using System.Text.Json;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// The union of every kind's fields, filled while <see cref="CompactSearchValueReader"/> reads one object and turned into
/// the value it describes afterwards.
/// </summary>
internal sealed class CompactSearchValueFields
{
    private string? _kind;
    private string? _system;
    private string? _code;
    private string? _text;
    private string? _identifierTypeSystem;
    private string? _identifierTypeCode;
    private string? _string;
    private string? _referenceKind;
    private string? _baseUri;
    private string? _resourceType;
    private string? _resourceId;
    private string? _start;
    private string? _end;
    private string? _dateText;
    private string? _uri;
    private string? _version;
    private string? _fragment;
    private decimal? _lowNumber;
    private decimal? _highNumber;
    private decimal? _lowQuantity;
    private decimal? _highQuantity;
    private bool _isMin;
    private bool _isMax;
    private List<IReadOnlyList<ISearchValue>>? _components;

    public void Set(string name, ref Utf8JsonReader reader)
    {
        switch (name)
        {
            case SearchValueConstants.KindName: _kind = reader.GetString(); break;
            case SearchValueConstants.SystemName: _system = reader.GetString(); break;
            case SearchValueConstants.CodeName: _code = reader.GetString(); break;
            case SearchValueConstants.TextName: _text = reader.GetString(); break;
            case SearchValueConstants.IdentifierTypeSystemName: _identifierTypeSystem = reader.GetString(); break;
            case SearchValueConstants.IdentifierTypeCodeName: _identifierTypeCode = reader.GetString(); break;
            case SearchValueConstants.StringName: _string = reader.GetString(); break;
            case SearchValueConstants.ReferenceKindName: _referenceKind = reader.GetString(); break;
            case SearchValueConstants.ReferenceBaseUriName: _baseUri = reader.GetString(); break;
            case SearchValueConstants.ReferenceResourceTypeName: _resourceType = reader.GetString(); break;
            case SearchValueConstants.ReferenceResourceIdName: _resourceId = reader.GetString(); break;
            case SearchValueConstants.DateTimeStartName: _start = reader.GetString(); break;
            case SearchValueConstants.DateTimeEndName: _end = reader.GetString(); break;
            case SearchValueConstants.DateTimeTextName: _dateText = reader.GetString(); break;
            case SearchValueConstants.UriName: _uri = reader.GetString(); break;
            case SearchValueConstants.UriVersionName: _version = reader.GetString(); break;
            case SearchValueConstants.UriFragmentName: _fragment = reader.GetString(); break;
            case SearchValueConstants.LowNumberName: _lowNumber = reader.GetDecimal(); break;
            case SearchValueConstants.HighNumberName: _highNumber = reader.GetDecimal(); break;
            case SearchValueConstants.LowQuantityName: _lowQuantity = reader.GetDecimal(); break;
            case SearchValueConstants.HighQuantityName: _highQuantity = reader.GetDecimal(); break;
            case SearchValueConstants.IsMinName: _isMin = reader.GetBoolean(); break;
            case SearchValueConstants.IsMaxName: _isMax = reader.GetBoolean(); break;
            case SearchValueConstants.ComponentsName: _components = ReadComponents(ref reader); break;
            default: throw new JsonException($"Unknown search value property '{name}'.");
        }
    }

    public ISearchValue Build() => _kind switch
    {
        SearchValueConstants.TokenKind => new TokenSearchValue(_system, _code, _text, _identifierTypeSystem, _identifierTypeCode),
        SearchValueConstants.QuantityKind => new QuantitySearchValue(_system, _code, _lowQuantity, _highQuantity),
        SearchValueConstants.NumberKind => new NumberSearchValue(_lowNumber, _highNumber),
        SearchValueConstants.StringKind => BuildString(),
        SearchValueConstants.ReferenceKind => BuildReference(),
        SearchValueConstants.DateTimeKind => BuildDate(),
        SearchValueConstants.UriKind => new UriSearchValue(Required(_uri, SearchValueConstants.UriName), separateCanonicalComponents: false) { Version = _version, Fragment = _fragment },
        SearchValueConstants.CompositeKind => new CompositeIndexSearchValue(Required(_components, SearchValueConstants.ComponentsName)),
        null => throw new JsonException($"A search value has no '{SearchValueConstants.KindName}'."),
        _ => throw new JsonException($"Unknown search value kind '{_kind}'."),
    };

    private static List<IReadOnlyList<ISearchValue>> ReadComponents(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Composite components must be an array.");
        }

        List<IReadOnlyList<ISearchValue>> components = [];
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            List<ISearchValue> members = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                members.Add(CompactSearchValueReader.Read(ref reader));
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A composite component is not terminated.");
            }

            components.Add(members);
        }

        if (reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("The composite components are not terminated.");
        }

        return components;
    }

    // StringSearchValue unescapes what it is given, so the stored (unescaped) text is escaped again first; the check
    // proves the escape and unescape rules invert each other for this value.
    private StringSearchValue BuildString()
    {
        string text = Required(_string, SearchValueConstants.StringName);
        var value = new StringSearchValue(CompactSearchValueReader.EscapeString(text)) { IsMin = _isMin, IsMax = _isMax };
        return string.Equals(value.String, text, StringComparison.Ordinal)
            ? value
            : throw new JsonException("A stored string value cannot be reconstructed exactly.");
    }

    private ReferenceSearchValue BuildReference()
    {
        if (!Enum.TryParse(Required(_referenceKind, SearchValueConstants.ReferenceKindName), ignoreCase: false, out ReferenceKind kind) || !Enum.IsDefined(kind))
        {
            throw new JsonException($"Unknown reference kind '{_referenceKind}'.");
        }

        return new ReferenceSearchValue(
            kind,
            _baseUri is null ? null! : new Uri(_baseUri, UriKind.RelativeOrAbsolute),
            _resourceType!,
            Required(_resourceId, SearchValueConstants.ReferenceResourceIdName));
    }

    private DateTimeSearchValue BuildDate()
    {
        string start = Required(_start, SearchValueConstants.DateTimeStartName);
        string end = Required(_end, SearchValueConstants.DateTimeEndName);
        DateTimeSearchValue date = _dateText is null ? DateTimeSearchValue.Parse(start, end) : DateTimeSearchValue.Parse(_dateText);

        // A reparsed original must land on the persisted range and print as it did; anything else means the input is
        // not what was written.
        if (date.Start != DateTimeOffset.Parse(start, CultureInfo.InvariantCulture)
            || date.End != DateTimeOffset.Parse(end, CultureInfo.InvariantCulture)
            || (_dateText is not null && !string.Equals(date.ToString(), _dateText, StringComparison.Ordinal)))
        {
            throw new JsonException("A stored date value does not reproduce its persisted range.");
        }

        date.IsMin = _isMin;
        date.IsMax = _isMax;
        return date;
    }

    private static T Required<T>(T? value, string name)
        where T : class => value ?? throw new JsonException($"A search value is missing '{name}'.");
}
