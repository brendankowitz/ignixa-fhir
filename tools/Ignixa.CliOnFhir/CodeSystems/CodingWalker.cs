// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.CodeSystems;

/// <summary>
/// Finds the codings in one resource's JSON. A coding is an object with a string <c>code</c> that has a string
/// <c>system</c> (a Coding, or a Quantity with a unit system) or sits in a <c>coding</c> array (a Coding without a system).
/// Extensions and contained resources are walked too. A string value of a FHIR <c>code</c> element with a FHIR-defined
/// binding (by the FHIR version's schema, for example <c>Patient.gender</c>; see <see cref="ImplicitCodeElements"/>) is an
/// implicit code instead: it has no system in the data, and carries the system its binding implies only where one is known. Element paths are built from property names, which are folder data, so a
/// name that is not shaped like a FHIR element name is replaced by its digest.
/// </summary>
internal static partial class CodingWalker
{
    public static void Walk(JsonElement resource, string resourceType, string resourceName, ImplicitCodeElements implicitElements, InventoryBuilder builder) =>
        Object(resource, resourceType, inCodingArray: false, resourceName, implicitElements, builder);

    private static void Object(JsonElement element, string path, bool inCodingArray, string resource, ImplicitCodeElements implicitElements, InventoryBuilder builder)
    {
        string? code = StringMember(element, "code");
        string? system = StringMember(element, "system");
        if (code is { Length: > 0 } && (system is not null || inCodingArray))
        {
            builder.Coding(resource, Label(path), system is { Length: > 0 } ? system : null, code);
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name == "resourceType")
            {
                continue;
            }

            string child = $"{path}.{Segment(property.Name)}";
            JsonElement value = property.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    Object(value, child, inCodingArray: false, resource, implicitElements, builder);
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in value.EnumerateArray())
                    {
                        Item(property.Name, item, child, resource, implicitElements, builder);
                    }

                    break;
                case JsonValueKind.String when implicitElements.Contains(child):
                    builder.Implicit(child, implicitElements.SystemOf(child), value.GetString()!);
                    break;
            }
        }
    }

    private static void Item(string name, JsonElement item, string path, string resource, ImplicitCodeElements implicitElements, InventoryBuilder builder)
    {
        if (item.ValueKind == JsonValueKind.String && implicitElements.Contains(path))
        {
            builder.Implicit(path, implicitElements.SystemOf(path), item.GetString()!);
        }
        else if (item.ValueKind == JsonValueKind.Object)
        {
            // A contained resource's elements are named from its own type.
            string itemPath = name == "contained" && StringMember(item, "resourceType") is { } containedType ? Segment(containedType) : path;
            Object(item, itemPath, inCodingArray: name == "coding", resource, implicitElements, builder);
        }
    }

    private static string? StringMember(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // A CodeableConcept's codings are reported at the CodeableConcept: Observation.code rather than Observation.code.coding.
    private static string Label(string path) => path.EndsWith(".coding", StringComparison.Ordinal) ? path[..^".coding".Length] : path;

    private static string Segment(string name) => ElementName().IsMatch(name) ? name : FhirIds.Digest(name);

    // An element name, or a primitive's extension holder: '_' then an element name (Patient._gender).
    [GeneratedRegex("^_?[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex ElementName();
}
