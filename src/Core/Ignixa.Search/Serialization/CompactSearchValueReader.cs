// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Reads search values from compact JSON format using abbreviated property names.
/// Reconstructs ISearchValue instances from serialized compact format.
/// </summary>
public static class CompactSearchValueReader
{
    /// <summary>
    /// Reads a search value object from a JsonElement.
    /// Determines the search value type based on properties present.
    /// </summary>
    public static ISearchValue ReadSearchValue(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected JSON object for search value");
        }

        // Read all properties into dictionary for type discrimination
        var properties = new Dictionary<string, JsonElement>();
        foreach (var property in element.EnumerateObject())
        {
            properties[property.Name] = property.Value;
        }

        // Determine search value type based on properties present
        if (HasProperty(properties, SearchValueConstants.DateTimeStartName) ||
            HasProperty(properties, SearchValueConstants.DateTimeEndName))
        {
            return ReadDateTimeSearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.NumberName) ||
            HasProperty(properties, SearchValueConstants.LowNumberName))
        {
            return ReadNumberSearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.QuantityName) ||
            HasProperty(properties, SearchValueConstants.LowQuantityName))
        {
            return ReadQuantitySearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.ReferenceResourceIdName))
        {
            return ReadReferenceSearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.NormalizedStringName))
        {
            return ReadStringSearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.UriName))
        {
            return ReadUriSearchValue(properties);
        }

        if (HasProperty(properties, SearchValueConstants.CodeName) ||
            HasProperty(properties, SearchValueConstants.SystemName))
        {
            return ReadTokenSearchValue(properties);
        }

        // Default to string if no other type matches
        throw new JsonException("Unable to determine search value type from properties");
    }

    private static bool HasProperty(Dictionary<string, JsonElement> properties, string propertyName)
    {
        return properties.ContainsKey(propertyName);
    }

    private static string GetStringProperty(Dictionary<string, JsonElement> properties, string propertyName)
    {
        return properties.TryGetValue(propertyName, out var element)
            ? element.GetString()
            : null;
    }

    private static decimal? GetDecimalProperty(Dictionary<string, JsonElement> properties, string propertyName)
    {
        return properties.TryGetValue(propertyName, out var element)
            ? element.GetDecimal()
            : null;
    }

    private static DateTimeSearchValue ReadDateTimeSearchValue(Dictionary<string, JsonElement> properties)
    {
        var startString = GetStringProperty(properties, SearchValueConstants.DateTimeStartName);
        var endString = GetStringProperty(properties, SearchValueConstants.DateTimeEndName);

        if (startString == null || endString == null)
        {
            throw new JsonException("DateTime search value missing start or end");
        }

        var start = DateTimeOffset.Parse(startString, CultureInfo.InvariantCulture);
        var end = DateTimeOffset.Parse(endString, CultureInfo.InvariantCulture);

        // Use Parse method to reconstruct from start/end strings
        return DateTimeSearchValue.Parse(startString, endString);
    }

    private static NumberSearchValue ReadNumberSearchValue(Dictionary<string, JsonElement> properties)
    {
        // Try exact value first
        var exactValue = GetDecimalProperty(properties, SearchValueConstants.NumberName);
        if (exactValue.HasValue)
        {
            return new NumberSearchValue(exactValue.Value);
        }

        // Otherwise use range values
        var low = GetDecimalProperty(properties, SearchValueConstants.LowNumberName);
        var high = GetDecimalProperty(properties, SearchValueConstants.HighNumberName);

        return new NumberSearchValue(low, high);
    }

    private static QuantitySearchValue ReadQuantitySearchValue(Dictionary<string, JsonElement> properties)
    {
        var system = GetStringProperty(properties, SearchValueConstants.SystemName);
        var code = GetStringProperty(properties, SearchValueConstants.CodeName);

        // Try exact value first
        var exactValue = GetDecimalProperty(properties, SearchValueConstants.QuantityName);
        if (exactValue.HasValue)
        {
            return new QuantitySearchValue(system, code, exactValue.Value);
        }

        // Otherwise use range values
        var low = GetDecimalProperty(properties, SearchValueConstants.LowQuantityName);
        var high = GetDecimalProperty(properties, SearchValueConstants.HighQuantityName);

        return new QuantitySearchValue(system, code, low, high);
    }

    private static ReferenceSearchValue ReadReferenceSearchValue(Dictionary<string, JsonElement> properties)
    {
        var baseUriString = GetStringProperty(properties, SearchValueConstants.ReferenceBaseUriName);
        var resourceType = GetStringProperty(properties, SearchValueConstants.ReferenceResourceTypeName);
        var resourceId = GetStringProperty(properties, SearchValueConstants.ReferenceResourceIdName);

        Uri baseUri = null;
        if (!string.IsNullOrEmpty(baseUriString))
        {
            baseUri = new Uri(baseUriString);
        }

        // Determine reference kind based on what's present
        ReferenceKind kind = ReferenceKind.Internal;
        if (baseUri != null)
        {
            kind = ReferenceKind.External;
        }

        return new ReferenceSearchValue(kind, baseUri, resourceType, resourceId);
    }

    private static StringSearchValue ReadStringSearchValue(Dictionary<string, JsonElement> properties)
    {
        // Normalized string is always present, original may not be (for composite components)
        var normalized = GetStringProperty(properties, SearchValueConstants.NormalizedStringName);
        var original = GetStringProperty(properties, SearchValueConstants.StringName);

        // If original is missing, reconstruct from normalized (uppercase was used for normalization)
        var value = original ?? normalized ?? string.Empty;

        return new StringSearchValue(value);
    }

    private static TokenSearchValue ReadTokenSearchValue(Dictionary<string, JsonElement> properties)
    {
        var system = GetStringProperty(properties, SearchValueConstants.SystemName);
        var code = GetStringProperty(properties, SearchValueConstants.CodeName);
        var normalizedText = GetStringProperty(properties, SearchValueConstants.NormalizedTextName);

        // Use normalized text directly (uppercase was used for normalization)
        var text = normalizedText;

        return new TokenSearchValue(system, code, text);
    }

    private static UriSearchValue ReadUriSearchValue(Dictionary<string, JsonElement> properties)
    {
        var uri = GetStringProperty(properties, SearchValueConstants.UriName);

        if (string.IsNullOrEmpty(uri))
        {
            throw new JsonException("URI search value missing uri property");
        }

        return new UriSearchValue(uri, false);
    }
}
