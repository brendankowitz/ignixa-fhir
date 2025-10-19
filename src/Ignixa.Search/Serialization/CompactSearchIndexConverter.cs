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
/// System.Text.Json converter for compact search index serialization.
/// Implements parameter-first structure optimization from issue #2686.
///
/// Format:
/// {
///   "parameterName": [
///     { "n_s": "VALUE1" },
///     { "n_s": "VALUE2" }
///   ],
///   "otherParameter": [...]
/// }
///
/// Benefits:
/// - 25-35% storage reduction via abbreviated property names
/// - Eliminates repeated parameter names (issue #2686)
/// - Groups values by parameter for faster queries
/// - Optimized for Cosmos DB range indexing
/// </summary>
public class CompactSearchIndexConverter : JsonConverter<List<SearchIndexEntry>>
{
    /// <summary>
    /// Writes search indices in compact, parameter-first format.
    /// </summary>
    public override void Write(Utf8JsonWriter writer, List<SearchIndexEntry> entries, JsonSerializerOptions options)
    {
        if (entries == null || entries.Count == 0)
        {
            writer.WriteNullValue();
            return;
        }

        // Group by parameter name (issue #2686 optimization)
        // This eliminates repeated "p": "parameterName" strings
        var grouped = entries.GroupBy(e => e.SearchParameter.Code);

        writer.WriteStartObject();

        foreach (var group in grouped)
        {
            // Parameter name as property key
            writer.WritePropertyName(group.Key);
            writer.WriteStartArray();

            foreach (var entry in group)
            {
                // Write compact search value object
                writer.WriteStartObject();

                var valueWriter = new CompactSearchValueWriter(writer);
                entry.Value.AcceptVisitor(valueWriter);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Reads search indices from compact, parameter-first format.
    /// Creates basic SearchParameterInfo objects (without full metadata).
    /// For full metadata, use ISupportedSearchParameterDefinitionManager lookup.
    /// </summary>
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

        using var doc = JsonDocument.ParseValue(ref reader);

        // Iterate through parameter-first structure
        foreach (var parameterProperty in doc.RootElement.EnumerateObject())
        {
            string parameterCode = parameterProperty.Name;

            // Iterate through values array for this parameter
            if (parameterProperty.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var valueElement in parameterProperty.Value.EnumerateArray())
                {
                    if (valueElement.ValueKind == JsonValueKind.Object)
                    {
                        // Read search value using CompactSearchValueReader
                        var searchValue = CompactSearchValueReader.ReadSearchValue(valueElement);

                        // Determine the SearchParamType from the search value's concrete type
                        var searchParamType = GetSearchParamType(searchValue);

                        // Create SearchParameterInfo with correct type
                        // Full metadata would require ISupportedSearchParameterDefinitionManager lookup
                        var searchParameter = new SearchParameterInfo(
                            name: parameterCode,
                            code: parameterCode,
                            searchParamType: searchParamType);

                        entries.Add(new SearchIndexEntry(searchParameter, searchValue));
                    }
                }
            }
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
            CompositeSearchValue => SearchParamType.Composite,
            _ => throw new NotSupportedException($"Unknown search value type: {searchValue.GetType().Name}")
        };
    }
}
