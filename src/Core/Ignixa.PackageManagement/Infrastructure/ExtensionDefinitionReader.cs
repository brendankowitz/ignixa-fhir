// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Validation.Abstractions;

namespace Ignixa.PackageManagement.Infrastructure;

/// <summary>
/// Reads what an extension StructureDefinition says about its use: its contexts (R4 and later <c>context[]</c> of
/// <c>{type, expression}</c>; STU3 <c>contextType</c> with <c>context[]</c> strings) and whether its root element is a modifier.
/// </summary>
internal static class ExtensionDefinitionReader
{
    /// <summary>The definition's use, or null when <paramref name="json"/> is not an extension definition.</summary>
    public static ExtensionDefinitionInfo? Read(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject definition || (definition["type"] as JsonValue)?.ToString() != "Extension")
        {
            return null;
        }

        return new ExtensionDefinitionInfo(Contexts(definition), IsModifier(definition));
    }

    private static List<ExtensionContext> Contexts(JsonObject definition)
    {
        List<ExtensionContext> contexts = [];
        if (definition["context"] is not JsonArray entries)
        {
            return contexts;
        }

        // STU3 states one contextType for all its context strings.
        var stu3Type = (definition["contextType"] as JsonValue)?.ToString();
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case JsonObject context when (context["expression"] as JsonValue)?.ToString() is { Length: > 0 } expression:
                    contexts.Add(new ExtensionContext(TypeOf((context["type"] as JsonValue)?.ToString()), expression));
                    break;
                case JsonValue value when value.ToString() is { Length: > 0 } expression && stu3Type is not null:
                    contexts.Add(new ExtensionContext(TypeOf(stu3Type), expression));
                    break;
            }
        }

        return contexts;
    }

    // An unrecognised context type is read as FHIRPath, which validation reports as not checked rather than guess.
    private static ExtensionContextType TypeOf(string? type) => type switch
    {
        "element" or "resource" or "datatype" => ExtensionContextType.Element,
        "extension" => ExtensionContextType.Extension,
        _ => ExtensionContextType.FhirPath,
    };

    private static bool IsModifier(JsonObject definition)
    {
        foreach (var part in new[] { "snapshot", "differential" })
        {
            if (definition[part] is JsonObject { } container && container["element"] is JsonArray elements
                && elements.OfType<JsonObject>().FirstOrDefault(element => (element["path"] as JsonValue)?.ToString() == "Extension") is { } root
                && root["isModifier"] is JsonValue flag && flag.TryGetValue(out bool isModifier))
            {
                return isModifier;
            }
        }

        return false;
    }
}
