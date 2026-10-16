// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.CliOnFhir.Terminology.DrugClasses;

/// <summary>
/// Reads RxClass's JSON shapes, shape-checking every value: an item without its id is skipped, and an empty object (what
/// RxClass answers when it knows nothing) is an empty list.
/// </summary>
internal static class RxClassJson
{
    public const int MaxNameLength = 300;

    /// <summary><c>allClasses</c>: rxclassMinConceptList.rxclassMinConcept[].</summary>
    public static IEnumerable<DrugClass> Classes(JsonObject body, string source) =>
        Items(body["rxclassMinConceptList"]?["rxclassMinConcept"])
            .Where(item => ClassId(item) is not null)
            .Select(item => new DrugClass(source, ClassId(item)!, Name(item, "className"), Name(item, "classType"), Via: null));

    /// <summary><c>class/byRxcui</c>: rxclassDrugInfoList.rxclassDrugInfo[], each a class and the concept it is related to.</summary>
    public static IEnumerable<DrugClass> DrugClasses(JsonObject body, string source) =>
        Items(body["rxclassDrugInfoList"]?["rxclassDrugInfo"])
            .Select(item => (Class: item["rxclassMinConceptItem"] as JsonObject, Via: Concept(item["minConcept"] as JsonObject)))
            .Where(item => ClassId(item.Class) is not null)
            .Select(item => new DrugClass(source, ClassId(item.Class)!, Name(item.Class, "className"), Name(item.Class, "classType"), item.Via));

    /// <summary><c>classMembers</c>: drugMemberGroup.drugMember[].minConcept.</summary>
    public static IEnumerable<DrugConcept> Members(JsonObject body) =>
        Items(body["drugMemberGroup"]?["drugMember"])
            .Select(item => Concept(item["minConcept"] as JsonObject))
            .OfType<DrugConcept>();

    /// <summary>Whether <paramref name="value"/> is shaped as an RxCUI.</summary>
    public static bool IsRxcui(string value) => value.Length is > 0 and <= 12 && value.All(char.IsAsciiDigit);

    /// <summary>Whether <paramref name="value"/> is shaped as an RxClass class id (C07AB, CV100, N0000175556).</summary>
    public static bool IsClassId(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiLetterOrDigit);

    private static DrugConcept? Concept(JsonObject? concept) =>
        String(concept, "rxcui") is { } rxcui && IsRxcui(rxcui) ? new DrugConcept(rxcui, Name(concept, "name"), Name(concept, "tty")) : null;

    private static string? ClassId(JsonObject? item) => String(item, "classId") is { } id && IsClassId(id) ? id : null;

    private static string? Name(JsonObject? item, string name) => String(item, name) is { } text ? text[..Math.Min(text.Length, MaxNameLength)] : null;

    private static IEnumerable<JsonObject> Items(JsonNode? list) => (list as JsonArray ?? []).OfType<JsonObject>();

    private static string? String(JsonNode? node, string name) =>
        node?[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
