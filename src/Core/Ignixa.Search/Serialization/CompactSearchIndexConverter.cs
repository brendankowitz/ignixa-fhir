// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;

namespace Ignixa.Search.Serialization;

/// <summary>
/// System.Text.Json converter for compact search index serialization: values grouped under their parameter's code, each
/// in the lossless compact value format (<see cref="CompactSearchValueWriter"/>).
///
/// Format:
/// {
///   "parameterCode": [
///     { "k": "token", "s": "http://loinc.org", "c": "8867-4" },
///     { "k": "quantity", "s": "http://unitsofmeasure.org", "c": "/min", "lq": 72, "hq": 72 }
///   ],
///   "otherParameter": [...]
/// }
///
/// Grouping by parameter avoids repeating the code on every value (issue #2686). Reading is strict: anything this
/// converter did not write throws <see cref="JsonException"/>.
/// </summary>
public class CompactSearchIndexConverter : JsonConverter<List<SearchIndexEntry>>
{
    /// <summary>
    /// Writes search indices in compact, parameter-first format.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, List<SearchIndexEntry> entries, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (entries == null || entries.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        foreach (var group in entries.GroupBy(e => e.SearchParameter.Code))
        {
            writer.WritePropertyName(group.Key);
            writer.WriteStartArray();

            foreach (var entry in group)
            {
                CompactSearchValueWriter.WriteObject(writer, entry.Value);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads search indices from compact, parameter-first format.
    /// Creates basic SearchParameterInfo objects (code and type only, the code doubling as the name).
    /// For full metadata, use ISupportedSearchParameterDefinitionManager lookup.
    /// </summary>
    /// <exception cref="JsonException">The input is not an index this converter wrote.</exception>
    public override List<SearchIndexEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token for search indices");
        }

        var entries = new List<SearchIndexEntry>();

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string parameterCode = reader.GetString();
            if (string.IsNullOrWhiteSpace(parameterCode))
            {
                throw new JsonException("A search parameter code is empty.");
            }

            reader.Read();
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"The values of search parameter '{parameterCode}' must be an array.");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
            {
                ISearchValue searchValue = CompactSearchValueReader.Read(ref reader);
                var searchParameter = new SearchParameterInfo(
                    name: parameterCode,
                    code: parameterCode,
                    searchParamType: GetSearchParamType(searchValue));

                entries.Add(new SearchIndexEntry(searchParameter, searchValue));
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException($"The values of search parameter '{parameterCode}' must be search value objects.");
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("The search indices object is not terminated.");
        }

        return entries;
    }

    /// <summary>
    /// Determines the SearchParamType from an ISearchValue concrete type.
    /// Used during deserialization to reconstruct the correct parameter type.
    /// </summary>
    private static SearchParamType GetSearchParamType(ISearchValue searchValue)
    {
        return searchValue switch
        {
            DateTimeSearchValue => SearchParamType.Date,
            NumberSearchValue => SearchParamType.Number,
            QuantitySearchValue => SearchParamType.Quantity,
            ReferenceSearchValue => SearchParamType.Reference,
            StringSearchValue => SearchParamType.String,
            TokenSearchValue => SearchParamType.Token,
            UriSearchValue => SearchParamType.Uri,
            CompositeIndexSearchValue => SearchParamType.Composite,
            _ => throw new NotSupportedException($"Unknown search value type: {searchValue.GetType().Name}")
        };
    }
}
