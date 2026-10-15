// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Terminology.Remote;

/// <summary>Reads a FHIR <c>Parameters</c> resource from a terminology server: values by name, and parts.</summary>
internal static class TxParameters
{
    public static IEnumerable<JsonObject> All(JsonObject parameters, string name) =>
        (parameters["parameter"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(parameter => String(parameter, "name") == name);

    public static IEnumerable<JsonObject> Parts(JsonObject parameter, string name) =>
        (parameter["part"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(part => String(part, "name") == name);

    /// <summary>The first parameter (or part) <paramref name="name"/>'s primitive value as text, from any value[x]; null when absent.</summary>
    public static string? Value(JsonObject parametersOrParameter, string name, bool parts = false)
    {
        JsonObject? found = (parts ? Parts(parametersOrParameter, name) : All(parametersOrParameter, name)).FirstOrDefault();
        return found is null ? null : PrimitiveValue(found);
    }

    public static string? PrimitiveValue(JsonObject parameter)
    {
        foreach ((string key, JsonNode? value) in parameter)
        {
            if (key.StartsWith("value", StringComparison.Ordinal) && value is JsonValue primitive)
            {
                return primitive.GetValueKind() switch
                {
                    JsonValueKind.String => primitive.GetValue<string>(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => primitive.ToJsonString(),
                    _ => null,
                };
            }
        }

        return null;
    }

    /// <summary>A part's <c>valueCoding</c>, if any.</summary>
    public static JsonObject? Coding(JsonObject parameter, string name) => Parts(parameter, name).FirstOrDefault()?["valueCoding"] as JsonObject;

    public static string? String(JsonNode? node, string name) =>
        node?[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
