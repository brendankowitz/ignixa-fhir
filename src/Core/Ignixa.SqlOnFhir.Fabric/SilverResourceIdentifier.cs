// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Builds the source value of the resource's top-level <c>identifier</c> column: the source identifiers plus one that
/// records the source resource id, unless an identifier already carries a fhirId coding.
/// </summary>
/// <remarks>
/// When the id identifier is added, the existing identifiers are first deduplicated, keeping the first of equal ones
/// (the source pipeline uses a set union here; its ordering is unverified). Equality compares identifiers as they
/// would be stored with a shallow assigner. Every assigner is normalized afterwards, when the column is read.
/// </remarks>
internal static class SilverResourceIdentifier
{
    private static readonly SilverType ShallowReference = BuildShallowReference();

    public static JsonArray Build(
        JsonNode? source,
        SilverType elementType,
        string sourceId,
        FabricSilverIngestion ingestion,
        SilverValueReader reader)
    {
        const string Path = "identifier";
        if (source is not (null or JsonArray))
        {
            throw SilverValueReader.Reject(Path, "must be an array");
        }

        var identifiers = new List<JsonObject>();
        var items = source?.AsArray() ?? [];
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonObject item)
            {
                throw SilverValueReader.Reject($"{Path}[{index}]", "must be an object");
            }

            var identifier = item.DeepClone().AsObject();
            if (identifier["type"] is JsonObject type && type["coding"] is null)
            {
                type["coding"] = new JsonArray();
            }

            identifiers.Add(identifier);
        }

        if (!identifiers.Exists(identifier => SilverReferenceNormalizer.HasFhirIdCoding(identifier["type"])))
        {
            identifiers = Deduplicate(identifiers, elementType, reader);
            identifiers.Add(new JsonObject
            {
                ["value"] = sourceId,
                ["system"] = ingestion.SourceNamespace,
                ["type"] = SilverReferenceNormalizer.CreateFhirIdType(),
                ["period"] = new JsonObject(),
                ["assigner"] = new JsonObject(),
            });
        }

        return new JsonArray(identifiers.ToArray<JsonNode?>());
    }

    private static List<JsonObject> Deduplicate(List<JsonObject> identifiers, SilverType elementType, SilverValueReader reader)
    {
        // Duplicates are judged on the source assigner, before reference normalization; a no-op when the element
        // type has no assigner field.
        var keyType = elementType.WithFieldType("assigner", ShallowReference);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<JsonObject>();
        for (var index = 0; index < identifiers.Count; index++)
        {
            var keySource = identifiers[index].DeepClone().AsObject();
            if (keySource["assigner"] is JsonObject assigner && assigner["identifier"] is null)
            {
                assigner["identifier"] = new JsonObject();
            }

            // The reader writes every field in schema order with canonical leaf text, so equal keys serialize equally.
            var key = reader.Read(keySource, keyType, $"identifier[{index}]", SilverValueReader.ReadMode.Source)!;
            if (keys.Add(key.ToJsonString()))
            {
                kept.Add(identifiers[index]);
            }
        }

        return kept;
    }

    private static SilverType BuildShallowReference()
    {
        static SilverField Text(string name) => new(name, SilverType.String, nullable: true, originalOf: null);

        var identifier = SilverType.StructOf(
            Text("extension"), Text("use"), Text("type"), Text("value"), Text("system"), Text("period"), Text("assigner"));
        return SilverType.StructOf(
            Text("extension"),
            Text("id"),
            Text("reference"),
            Text("type"),
            new SilverField("identifier", identifier, nullable: true, originalOf: null),
            Text("display"));
    }
}
