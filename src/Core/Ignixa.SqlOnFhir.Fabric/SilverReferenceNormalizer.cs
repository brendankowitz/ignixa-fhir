// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric;

/// <summary>
/// Rewrites one FHIR Reference into the Silver Reference layout: <c>reference</c>, <c>type</c>, <c>identifier</c>,
/// <c>display</c>, <c>extension</c>, <c>id</c>, <c>idOrig</c> and <c>msftSourceReference</c>, where <c>id</c> is the
/// hashed Silver id of a resolvable target and <c>identifier</c> is completed from the reference text.
/// </summary>
/// <remarks>
/// <para>
/// The output is an unfinished value in Silver layout; <see cref="SilverValueReader"/> reads it into the column's
/// declared type. A missing Reference still produces a struct (all fields null except an all-null identifier),
/// because the source pipeline's normalization always returns a value.
/// </para>
/// <para>
/// Inside the identifier, <c>type</c> is a CodeableConcept, <c>period</c> is kept only as an empty marker (its
/// contents never reach Silver), and <c>assigner</c> is a shallow Reference whose fields are strings. Values in those
/// slots that the source pipeline would turn into language-specific text instead of JSON (arrays or objects where it
/// expects a string) are rejected (deterministic policy).
/// </para>
/// </remarks>
internal sealed class SilverReferenceNormalizer
{
    public const string FhirIdCode = "fhirId";

    private const string FhirIdSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";
    private const string FhirIdDisplay = "FHIR Id";

    private static readonly string[] ShallowReferenceFields = ["extension", "id", "reference", "type", "display"];
    private static readonly string[] ShallowIdentifierFields = ["extension", "use", "type", "value", "system", "period", "assigner"];
    private static readonly string[] CodingStringFields = ["extension", "id", "system", "version", "code", "display"];

    private readonly SilverReferencePattern _pattern;
    private readonly FabricSilverIngestion _ingestion;

    public SilverReferenceNormalizer(SilverReferencePattern pattern, FabricSilverIngestion ingestion)
    {
        _pattern = pattern;
        _ingestion = ingestion;
    }

    /// <summary>The coding Silver uses to mark an identifier that carries a source resource id.</summary>
    public static JsonObject CreateFhirIdCoding() => new()
    {
        ["system"] = FhirIdSystem,
        ["code"] = FhirIdCode,
        ["display"] = FhirIdDisplay,
    };

    /// <summary>A CodeableConcept holding only the fhirId coding.</summary>
    public static JsonObject CreateFhirIdType() => new()
    {
        ["coding"] = new JsonArray(CreateFhirIdCoding()),
        ["text"] = FhirIdDisplay,
    };

    /// <summary>Whether a CodeableConcept (in source or Silver layout) has a coding whose code is fhirId.</summary>
    public static bool HasFhirIdCoding(JsonNode? type) =>
        type is JsonObject concept &&
        concept["coding"] is JsonArray codings &&
        codings.Any(coding => coding is JsonObject members &&
                              members["code"] is JsonValue code &&
                              code.GetValueKind() == JsonValueKind.String &&
                              code.GetValue<string>() == FhirIdCode);

    public JsonObject Normalize(JsonNode? source, string path)
    {
        if (source is null)
        {
            return new JsonObject { ["identifier"] = new JsonObject() };
        }

        if (source is not JsonObject members)
        {
            throw SilverValueReader.Reject(path, "must be an object");
        }

        var reference = StringLeaf(members["reference"]);
        var declaredType = StringLeaf(members["type"]);
        var identifier = ConformIdentifier(members["identifier"], SilverValueReader.Child(path, "identifier"), out var typePresent);
        var result = new JsonObject
        {
            ["reference"] = reference,
            ["type"] = declaredType,
            ["identifier"] = identifier,
            ["display"] = StringLeaf(members["display"]),
            ["extension"] = StringLeaf(members["extension"]),
            ["msftSourceReference"] = reference,
        };

        if (string.IsNullOrEmpty(reference))
        {
            return result;
        }

        var match = _pattern.Match(reference);
        var type = string.IsNullOrEmpty(declaredType) ? match?.Type : declaredType;
        result["type"] = type;
        result["reference"] = null;
        if (match is null)
        {
            return result;
        }

        var sameBase = match.Base is null || SilverIdHash.Strip(match.Base) == _ingestion.StrippedNamespace;
        if (!match.IsLogical)
        {
            if (sameBase)
            {
                result["id"] = SilverIdHash.Compute(_ingestion.StrippedNamespace, type!, match.Id!);
                result["idOrig"] = match.Id;
                AddSourceId(identifier, typePresent, match.Id!);
            }

            return result;
        }

        if (match.Base is null && string.IsNullOrEmpty(match.IdentifierSystem))
        {
            identifier["value"] = match.IdentifierValue;
            identifier["system"] = _ingestion.SourceNamespace;
        }
        else if (sameBase)
        {
            identifier["value"] = match.IdentifierValue;
            identifier["system"] = match.IdentifierSystem;
        }

        return result;
    }

    private static string? StringLeaf(JsonNode? node) => node is null ? null : SilverValueReader.ReadString(node);

    private static string? StrictString(JsonNode? node, string path)
    {
        if (node is null)
        {
            return null;
        }

        return node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : throw SilverValueReader.Reject(path, "must be a string inside a normalized reference identifier");
    }

    private static JsonObject ConformIdentifier(JsonNode? node, string path, out bool typePresent)
    {
        typePresent = false;
        if (node is null)
        {
            return new JsonObject { ["type"] = new JsonObject(), ["period"] = new JsonObject(), ["assigner"] = new JsonObject() };
        }

        if (node is not JsonObject members)
        {
            throw SilverValueReader.Reject(path, "must be an object");
        }

        var typePath = SilverValueReader.Child(path, "type");
        JsonObject type;
        switch (members["type"])
        {
            case null:
                type = new JsonObject();
                break;
            case JsonObject concept:
                type = ConformCodeableConcept(concept, typePath);
                typePresent = true;
                break;
            default:
                throw SilverValueReader.Reject(typePath, "must be an object");
        }

        if (members["period"] is not (null or JsonObject))
        {
            throw SilverValueReader.Reject(SilverValueReader.Child(path, "period"), "must be an object");
        }

        return new JsonObject
        {
            ["extension"] = StringLeaf(members["extension"]),
            ["use"] = StringLeaf(members["use"]),
            ["value"] = StringLeaf(members["value"]),
            ["system"] = StringLeaf(members["system"]),
            ["type"] = type,
            ["period"] = new JsonObject(),
            ["assigner"] = ConformShallowReference(members["assigner"], SilverValueReader.Child(path, "assigner")),
        };
    }

    private static JsonObject ConformCodeableConcept(JsonObject concept, string path)
    {
        var codingPath = SilverValueReader.Child(path, "coding");
        var codings = new JsonArray();
        switch (concept["coding"])
        {
            case null:
                break;
            case JsonArray items:
                for (var index = 0; index < items.Count; index++)
                {
                    codings.Add(ConformCoding(items[index], $"{codingPath}[{index}]"));
                }

                break;
            default:
                throw SilverValueReader.Reject(codingPath, "must be an array");
        }

        return new JsonObject
        {
            ["extension"] = StrictString(concept["extension"], SilverValueReader.Child(path, "extension")),
            ["id"] = StrictString(concept["id"], SilverValueReader.Child(path, "id")),
            ["coding"] = codings,
            ["text"] = StrictString(concept["text"], SilverValueReader.Child(path, "text")),
        };
    }

    private static JsonObject ConformCoding(JsonNode? node, string path)
    {
        if (node is not JsonObject members)
        {
            throw SilverValueReader.Reject(path, "must be an object");
        }

        var coding = new JsonObject();
        foreach (var name in CodingStringFields)
        {
            coding[name] = StrictString(members[name], SilverValueReader.Child(path, name));
        }

        var userSelected = members["userSelected"];
        if (userSelected is not null && userSelected.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw SilverValueReader.Reject(SilverValueReader.Child(path, "userSelected"), "must be a boolean");
        }

        coding["userSelected"] = userSelected?.DeepClone();
        return coding;
    }

    private static JsonObject ConformShallowReference(JsonNode? node, string path)
    {
        if (node is null or JsonObject { Count: 0 })
        {
            return new JsonObject();
        }

        if (node is not JsonObject members)
        {
            throw SilverValueReader.Reject(path, "must be an object");
        }

        var result = new JsonObject();
        foreach (var name in ShallowReferenceFields)
        {
            result[name] = StrictString(members[name], SilverValueReader.Child(path, name));
        }

        var identifierPath = SilverValueReader.Child(path, "identifier");
        switch (members["identifier"])
        {
            case null:
                result["identifier"] = new JsonObject();
                break;
            case JsonObject identifier:
                var conformed = new JsonObject();
                foreach (var name in ShallowIdentifierFields)
                {
                    conformed[name] = StrictString(identifier[name], SilverValueReader.Child(identifierPath, name));
                }

                result["identifier"] = conformed;
                break;
            default:
                throw SilverValueReader.Reject(identifierPath, "must be an object");
        }

        return result;
    }

    /// <summary>
    /// Records the resolved source id in the identifier unless it already identifies the target (a fhirId coding, or
    /// any value or system).
    /// </summary>
    private void AddSourceId(JsonObject identifier, bool typePresent, string sourceId)
    {
        var type = (JsonObject)identifier["type"]!;
        if (HasFhirIdCoding(type) ||
            !string.IsNullOrEmpty(identifier["value"]?.GetValue<string>()) ||
            !string.IsNullOrEmpty(identifier["system"]?.GetValue<string>()))
        {
            return;
        }

        identifier["value"] = sourceId;
        identifier["system"] = _ingestion.SourceNamespace;
        if (typePresent)
        {
            type["coding"]!.AsArray().Add(CreateFhirIdCoding());
        }
        else
        {
            identifier["type"] = CreateFhirIdType();
        }
    }
}
