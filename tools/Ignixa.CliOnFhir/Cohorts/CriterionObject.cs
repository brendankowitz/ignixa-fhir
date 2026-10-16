// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Cohorts;

/// <summary>
/// One JSON object of a cohort request, read strictly: a property the object does not define, or a value of the wrong
/// kind, fails the call naming where it is, since a misspelt criterion silently ignored would change the count.
/// </summary>
internal sealed class CriterionObject
{
    private readonly JsonElement _element;

    private CriterionObject(JsonElement element, string where)
    {
        _element = element;
        Where = where;
    }

    /// <summary>Where the request has the object, for example <c>observations[0]</c>.</summary>
    public string Where { get; }

    /// <summary>The objects of an argument: an array of them, or one object alone.</summary>
    /// <param name="argument">The argument as the client sent it; null or JSON null when it was not.</param>
    /// <param name="name">The argument's name.</param>
    /// <param name="properties">The properties each object may have.</param>
    /// <exception cref="ToolRequestException">Not an object or an array of objects, or an object with another property.</exception>
    public static IReadOnlyList<CriterionObject> List(JsonElement? argument, string name, IReadOnlyList<string> properties)
    {
        if (argument is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return [Of(value, $"{name}[0]", properties)];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{name}' is a list of criterion objects, for example [{{\"codes\":[\"http://snomed.info/sct|44054006\"]}}], not {Kind(value)}.");
        }

        return [.. value.EnumerateArray().Select((item, index) => Of(item, $"{name}[{index}]", properties))];
    }

    /// <summary>A nested object, such as <c>window</c>; null when absent.</summary>
    public CriterionObject? Object(string name, IReadOnlyList<string> properties) =>
        Property(name) is { } value ? Of(value, $"{Where}.{name}", properties) : null;

    /// <summary>A list of strings, or one string alone; null when absent.</summary>
    public IReadOnlyList<string>? Strings(string name)
    {
        if (Property(name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return [value.GetString()!];
        }

        if (value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String))
        {
            return [.. value.EnumerateArray().Select(item => item.GetString()!)];
        }

        throw Invalid($"{Where}.{name} is a list of strings, not {Kind(value)}.");
    }

    public string? String(string name) => Property(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.String } value => value.GetString(),
        { } value => throw Invalid($"{Where}.{name} is a string, not {Kind(value)}."),
    };

    /// <summary>A number; a numeric string is accepted too, since some clients send every value as a string.</summary>
    public decimal? Number(string name) => Property(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.Number } value when value.TryGetDecimal(out decimal number) => number,
        { ValueKind: JsonValueKind.String } value when decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out decimal number) => number,
        { } value => throw Invalid($"{Where}.{name} is a number, not {Kind(value)}."),
    };

    public bool? Boolean(string name) => Property(name) switch
    {
        null => null,
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { } value => throw Invalid($"{Where}.{name} is true or false, not {Kind(value)}."),
    };

    /// <summary>One of <paramref name="allowed"/>, or <paramref name="fallback"/> when absent.</summary>
    public string Choice(string name, IReadOnlyList<string> allowed, string fallback)
    {
        string? value = String(name);
        if (value is null)
        {
            return fallback;
        }

        return allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw Invalid($"{Where}.{name} is {string.Join(" or ", allowed.Select(choice => $"'{choice}'"))}, not '{Clip(value)}'.");
    }

    public static ToolRequestException Invalid(string problem) => ToolRequestException.Error("invalid-parameter", problem);

    private static CriterionObject Of(JsonElement element, string where, IReadOnlyList<string> properties)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw ToolRequestException.Error("invalid-parameter", $"{where} is an object with {Names(properties)}, not {Kind(element)}.");
        }

        List<string> unknown = [.. element.EnumerateObject().Select(property => property.Name).Where(name => !properties.Contains(name, StringComparer.Ordinal))];
        if (unknown.Count > 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"{where} has {string.Join(", ", unknown.Take(5).Select(name => $"'{Clip(name)}'"))}, which it does not take; its properties are {Names(properties)}.");
        }

        return new CriterionObject(element, where);
    }

    private JsonElement? Property(string name) =>
        _element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null ? value : null;

    private static string Names(IReadOnlyList<string> properties) => string.Join(", ", properties);

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true or false",
        _ => "null",
    };

    private static string Clip(string text) => text.Length > 64 ? text[..64] + "..." : text;
}
