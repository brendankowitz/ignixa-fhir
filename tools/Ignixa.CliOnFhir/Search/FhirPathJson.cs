// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.CliOnFhir.Search;

/// <summary>FHIRPath results as JSON, for table cells and expression values.</summary>
internal static class FhirPathJson
{
    /// <summary>
    /// A table cell: null for no value, the value itself for one, a JSON array for several (as a ViewDefinition column with
    /// <c>collection: true</c> gives them, but as JSON rather than text).
    /// </summary>
    public static JsonNode? Cell(IReadOnlyList<IElement> values) => values.Count switch
    {
        0 => null,
        1 => Value(values[0]),
        _ => new JsonArray([.. values.Select(Value)]),
    };

    /// <summary>Every value, in order, as a JSON array.</summary>
    public static JsonArray Values(IReadOnlyList<IElement> values) => new([.. values.Select(Value)]);

    /// <summary>
    /// One value: a primitive as its JSON value (a date or time as its FHIR literal, as written), a complex element (a
    /// CodeableConcept, a Quantity, a whole resource) as its JSON. A complex value the engine built rather than read (a
    /// quantity from arithmetic) is its FHIRPath string form.
    /// </summary>
    public static JsonNode? Value(IElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        switch (element.Value)
        {
            case string text:
                return JsonValue.Create(text);
            case bool flag:
                return JsonValue.Create(flag);
            case int number:
                return JsonValue.Create(number);
            case long number:
                return JsonValue.Create(number);
            case decimal number:
                return JsonValue.Create(number);
            case double number:
                return JsonValue.Create(number);
            case FhirTemporal temporal:
                return JsonValue.Create(temporal.Literal);
        }

        if (element.Meta<JsonNode>() is JsonNode source and (JsonObject or JsonArray))
        {
            return source.DeepClone();
        }

        if (Rebuilt(element) is { } rebuilt)
        {
            return rebuilt;
        }

        return element.Value is { } other ? JsonValue.Create(Convert.ToString(other, CultureInfo.InvariantCulture)) : null;
    }

    /// <summary>A complex element with no JSON behind it, rebuilt from its children by name; null when it has none.</summary>
    private static JsonObject? Rebuilt(IElement element)
    {
        var json = new JsonObject();
        foreach (IGrouping<string, IElement> group in element.Children().Where(child => !string.IsNullOrEmpty(child.Name)).GroupBy(child => child.Name))
        {
            List<JsonNode?> values = [.. group.Select(Value)];
            json[group.Key] = values.Count == 1 ? values[0] : new JsonArray([.. values]);
        }

        return json.Count > 0 ? json : null;
    }
}
