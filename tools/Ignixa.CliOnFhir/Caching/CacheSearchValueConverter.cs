// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>
/// Lossless JSON form of a search value, owned by the cache format. Ignixa's compact converter
/// (<c>CompactSearchValueWriter</c>) cannot be used: it drops token identifier types, reference kinds,
/// date and string min/max flags, canonical URI parts and the empty-versus-null distinction, uppercases
/// token text, flattens composites, and cannot read text-only tokens or high-only ranges back.
/// </summary>
/// <remarks>
/// Every value is an object with a kind discriminator <c>k</c> and only its non-default fields. Any change to
/// the shape must bump <see cref="CacheManifest.CurrentFormatVersion"/>. Writing a value type this format does
/// not know is a programmer error and throws; reading an unknown kind or malformed value throws so the
/// caller treats the file as corrupt.
/// </remarks>
internal sealed class CacheSearchValueConverter : JsonConverter<ISearchValue>
{
    private const string KindProperty = "k";

    public override void Write(Utf8JsonWriter writer, ISearchValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        // Exact types only: a subclass may carry state this format does not persist.
        switch (value)
        {
            case TokenSearchValue token when value.GetType() == typeof(TokenSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "token");
                WriteOptional(writer, "sys", token.System);
                WriteOptional(writer, "code", token.Code);
                WriteOptional(writer, "text", token.Text);
                WriteOptional(writer, "its", token.IdentifierTypeSystem);
                WriteOptional(writer, "itc", token.IdentifierTypeCode);
                writer.WriteEndObject();
                break;
            case QuantitySearchValue quantity when value.GetType() == typeof(QuantitySearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "quantity");
                WriteOptional(writer, "sys", quantity.System);
                WriteOptional(writer, "code", quantity.Code);
                WriteOptional(writer, "low", quantity.Low);
                WriteOptional(writer, "high", quantity.High);
                writer.WriteEndObject();
                break;
            case NumberSearchValue number when value.GetType() == typeof(NumberSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "number");
                WriteOptional(writer, "low", number.Low);
                WriteOptional(writer, "high", number.High);
                writer.WriteEndObject();
                break;
            case StringSearchValue text when value.GetType() == typeof(StringSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "string");
                writer.WriteString("str", text.String);
                WriteFlags(writer, text.IsMin, text.IsMax);
                writer.WriteEndObject();
                break;
            case ReferenceSearchValue reference when value.GetType() == typeof(ReferenceSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "reference");
                writer.WriteString("kind", reference.Kind.ToString());
                WriteOptional(writer, "base", reference.BaseUri?.OriginalString);
                WriteOptional(writer, "rt", reference.ResourceType);
                writer.WriteString("id", reference.ResourceId);
                writer.WriteEndObject();
                break;
            case DateTimeSearchValue date when value.GetType() == typeof(DateTimeSearchValue):
                WriteDate(writer, date);
                break;
            case UriSearchValue uri when value.GetType() == typeof(UriSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "uri");
                writer.WriteString("uri", uri.Uri);
                WriteOptional(writer, "ver", uri.Version);
                WriteOptional(writer, "frag", uri.Fragment);
                writer.WriteEndObject();
                break;
            case CompositeIndexSearchValue composite when value.GetType() == typeof(CompositeIndexSearchValue):
                writer.WriteStartObject();
                writer.WriteString(KindProperty, "composite");
                writer.WriteStartArray("components");
                foreach (IReadOnlyList<ISearchValue> component in composite.Components)
                {
                    writer.WriteStartArray();
                    foreach (ISearchValue member in component)
                    {
                        Write(writer, member, options);
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            default:
                throw new NotSupportedException($"The index cache cannot persist search values of type {value.GetType().FullName}.");
        }
    }

    public override ISearchValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected a search value object but found {reader.TokenType}.");
        }

        // Wrong token types and constructor invariants (a token with nothing in it, an empty composite) surface as
        // ArgumentException, InvalidOperationException or FormatException; they all mean the file is not what was written.
        try
        {
            var fields = new Fields();
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                string name = reader.GetString()!;
                reader.Read();
                fields.Set(name, ref reader, options, this);
            }

            if (reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException("The search value object is not terminated.");
            }

            return fields.Build();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new JsonException($"Invalid search value: {ex.Message}", ex);
        }
    }

    private static void WriteDate(Utf8JsonWriter writer, DateTimeSearchValue date)
    {
        writer.WriteStartObject();
        writer.WriteString(KindProperty, "date");
        writer.WriteString("start", date.Start.ToString("o", CultureInfo.InvariantCulture));
        writer.WriteString("end", date.End.ToString("o", CultureInfo.InvariantCulture));
        WriteFlags(writer, date.IsMin, date.IsMax);

        // ToString is the value's original partial date when it was built from one, and start-end otherwise;
        // persisting it only when it differs keeps ToString identical after a reload.
        string text = date.ToString();
        if (!string.Equals(text, DefaultDateText(date), StringComparison.Ordinal))
        {
            writer.WriteString("text", text);
        }

        writer.WriteEndObject();
    }

    private static string DefaultDateText(DateTimeSearchValue date) => $"{date.Start:o}-{date.End:o}";

    private static void WriteFlags(Utf8JsonWriter writer, bool isMin, bool isMax)
    {
        if (isMin)
        {
            writer.WriteBoolean("min", true);
        }

        if (isMax)
        {
            writer.WriteBoolean("max", true);
        }
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is { } number)
        {
            writer.WriteNumber(name, number);
        }
    }

    /// <summary>The union of every kind's fields, filled while reading and turned into the right value afterwards.</summary>
    private sealed class Fields
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
        private string? _uri;
        private string? _version;
        private string? _fragment;
        private decimal? _low;
        private decimal? _high;
        private bool _isMin;
        private bool _isMax;
        private List<IReadOnlyList<ISearchValue>>? _components;

        public void Set(string name, ref Utf8JsonReader reader, JsonSerializerOptions options, CacheSearchValueConverter converter)
        {
            switch (name)
            {
                case KindProperty: _kind = reader.GetString(); break;
                case "sys": _system = reader.GetString(); break;
                case "code": _code = reader.GetString(); break;
                case "text": _text = reader.GetString(); break;
                case "its": _identifierTypeSystem = reader.GetString(); break;
                case "itc": _identifierTypeCode = reader.GetString(); break;
                case "str": _string = reader.GetString(); break;
                case "kind": _referenceKind = reader.GetString(); break;
                case "base": _baseUri = reader.GetString(); break;
                case "rt": _resourceType = reader.GetString(); break;
                case "id": _resourceId = reader.GetString(); break;
                case "start": _start = reader.GetString(); break;
                case "end": _end = reader.GetString(); break;
                case "uri": _uri = reader.GetString(); break;
                case "ver": _version = reader.GetString(); break;
                case "frag": _fragment = reader.GetString(); break;
                case "low": _low = reader.GetDecimal(); break;
                case "high": _high = reader.GetDecimal(); break;
                case "min": _isMin = reader.GetBoolean(); break;
                case "max": _isMax = reader.GetBoolean(); break;
                case "components": _components = ReadComponents(ref reader, options, converter); break;
                default: throw new JsonException($"Unknown search value property '{name}'.");
            }
        }

        public ISearchValue Build() => _kind switch
        {
            "token" => new TokenSearchValue(_system, _code, _text, _identifierTypeSystem, _identifierTypeCode),
            "quantity" => new QuantitySearchValue(_system, _code, _low, _high),
            "number" => new NumberSearchValue(_low, _high),
            "string" => BuildString(),
            "reference" => BuildReference(),
            "date" => BuildDate(),
            "uri" => new UriSearchValue(Required(_uri, "uri"), separateCanonicalComponents: false) { Version = _version, Fragment = _fragment },
            "composite" => new CompositeIndexSearchValue(Required(_components, "components")),
            null => throw new JsonException($"A search value has no '{KindProperty}'."),
            _ => throw new JsonException($"Unknown search value kind '{_kind}'."),
        };

        private static List<IReadOnlyList<ISearchValue>> ReadComponents(ref Utf8JsonReader reader, JsonSerializerOptions options, CacheSearchValueConverter converter)
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
                    members.Add(converter.Read(ref reader, typeof(ISearchValue), options));
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

        // StringSearchValue unescapes what it is given and the writer stored the already-unescaped text, so the text
        // is escaped again first; the check proves the escape and unescape rules invert each other for this value.
        private StringSearchValue BuildString()
        {
            string text = Required(_string, "str");
            var value = new StringSearchValue(Escape(text)) { IsMin = _isMin, IsMax = _isMax };
            return string.Equals(value.String, text, StringComparison.Ordinal)
                ? value
                : throw new JsonException($"The string '{text}' cannot be reconstructed exactly.");
        }

        // Mirrors Ignixa.Search.Indexing.StringExtensions.EscapeSearchParameterValue (internal): the escaping
        // character first, then the token, composite and or separators.
        private static string Escape(string text) => text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal);

        private ReferenceSearchValue BuildReference()
        {
            if (!Enum.TryParse(Required(_referenceKind, "kind"), ignoreCase: false, out ReferenceKind kind) || !Enum.IsDefined(kind))
            {
                throw new JsonException($"Unknown reference kind '{_referenceKind}'.");
            }

            return new ReferenceSearchValue(kind, _baseUri is null ? null! : new Uri(_baseUri, UriKind.RelativeOrAbsolute), _resourceType!, Required(_resourceId, "id"));
        }

        private DateTimeSearchValue BuildDate()
        {
            string start = Required(_start, "start");
            string end = Required(_end, "end");
            DateTimeSearchValue date = _text is null ? DateTimeSearchValue.Parse(start, end) : DateTimeSearchValue.Parse(NormalizeOffset(_text));

            // A reparsed original must land on the persisted range and print as it did; anything else means the
            // file is not what was written.
            if (date.Start != DateTimeOffset.Parse(start, CultureInfo.InvariantCulture)
                || date.End != DateTimeOffset.Parse(end, CultureInfo.InvariantCulture)
                || (_text is not null && !string.Equals(date.ToString(), _text, StringComparison.Ordinal)))
            {
                throw new JsonException($"The date '{_text}' does not reproduce the persisted range {start} to {end}.");
            }

            date.IsMin = _isMin;
            date.IsMax = _isMax;
            return date;
        }

        // PartialDateTime.ToString prints a negative offset with minutes as "-05:-30", which it cannot parse back.
        // Restoring the sign to the hour part recovers the original; the reloaded value then prints the same text again.
        private static string NormalizeOffset(string text)
        {
            int colon = text.LastIndexOf(":-", StringComparison.Ordinal);
            return colon >= 3 && text.Length - colon == 4 && text[colon - 3] is ('+' or '-')
                ? $"{text[..(colon - 3)]}-{text.AsSpan(colon - 2, 2)}:{text[(colon + 2)..]}"
                : text;
        }

        private static T Required<T>(T? value, string name)
            where T : class => value ?? throw new JsonException($"A search value is missing '{name}'.");
    }
}
