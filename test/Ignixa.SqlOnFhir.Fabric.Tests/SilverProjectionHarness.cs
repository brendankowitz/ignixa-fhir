// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// Builds invented Silver contracts in the Silver layout (lineage columns, Reference structs carrying
/// <c>msftSourceReference</c>, <c>_string</c> companions) and projects resources through the public API.
/// </summary>
internal static class SilverProjectionHarness
{
    public const string Namespace = "https://example.org/fhir";
    public const string StrippedNamespace = "example.org/fhir";
    public const string FilePath = "Files/landing/batch-001.ndjson";
    public const string FhirIdSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";

    /// <summary>Has non-zero digits below the microsecond, which Silver drops.</summary>
    public static readonly DateTimeOffset IngestedAt = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1_234_567);

    public static readonly ISchema R4 = new R4CoreSchemaProvider();

    public static async Task<FabricSilverContract> ContractAsync(string resourceType, params JsonNode[] columns)
    {
        using var root = new SyntheticSilverRoot();
        JsonNode[] fields =
        [
            Field("msftModifiedDatetime", "timestamp"),
            Field("id", "string"),
            Field("resourceType", "string"),
            Field("msftSourceSystem", "string"),
            Field("msftFilePath", "string"),
            .. columns,
            Field("msftCreatedDatetime", "timestamp"),
        ];
        root.WriteSilverLog(resourceType, ProtocolLine(), SchemaLine(SchemaOf(fields)));
        return await FabricSilverContract.LoadAsync(root.Path, resourceType, CancellationToken.None);
    }

    public static async Task<FabricSilverProjection> CreateAsync(string resourceType, params JsonNode[] columns) =>
        FabricSilverProjection.Create(await ContractAsync(resourceType, columns), R4);

    public static FabricSilverIngestion Ingestion(string sourceNamespace = Namespace) =>
        new(sourceNamespace, FilePath, IngestedAt);

    public static FabricSilverRow Project(this FabricSilverProjection projection, string json, string sourceNamespace = Namespace) =>
        projection.Project(ResourceJsonNode.Parse(json), Ingestion(sourceNamespace));

    public static FabricSilverProjectionException Rejection(this FabricSilverProjection projection, string json) =>
        Should.Throw<FabricSilverProjectionException>(() => projection.Project(json));

    /// <summary>The Silver id rule, written independently of the library: SHA-256 hex of namespace/type/id.</summary>
    public static string Hash(string strippedNamespace, string resourceType, string id) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{strippedNamespace}/{resourceType}/{id}")));

    /// <summary>JSON text of a string or null, without the escaping of non-ASCII and HTML characters.</summary>
    public static string Text(string? value) => value is null ? "null" : JsonSerializer.Serialize(value, RawText);

    public static void ShouldBeJson(this JsonElement actual, string expected)
    {
        var equal = JsonNode.DeepEquals(JsonNode.Parse(actual.GetRawText()), JsonNode.Parse(expected));
        equal.ShouldBeTrue($"expected {expected} but was {actual.GetRawText()}");
    }

    public static JsonObject Struct(params JsonNode[] fields) => new()
    {
        ["type"] = "struct",
        ["fields"] = new JsonArray(fields),
    };

    public static JsonObject ArrayOf(JsonNode element, bool containsNull = true) => new()
    {
        ["type"] = "array",
        ["elementType"] = element,
        ["containsNull"] = containsNull,
    };

    public static JsonObject Strings(params string[] names) => Struct(names.Select(name => (JsonNode)Field(name, "string")).ToArray());

    public static JsonObject Coding() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("system", "string"),
        Field("version", "string"),
        Field("code", "string"),
        Field("display", "string"),
        Field("userSelected", "boolean"));

    public static JsonObject CodeableConcept() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("coding", ArrayOf(Coding())),
        Field("text", "string"));

    public static JsonObject Period() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("start", "timestamp"),
        Field("end", "timestamp"));

    /// <summary>The shallow Reference used where a Reference nests inside a Reference: every value is text.</summary>
    public static JsonObject ShallowReference() => Struct(
        Field("extension", "string"),
        Field("id", "string"),
        Field("reference", "string"),
        Field("type", "string"),
        Field("identifier", Strings("extension", "use", "type", "value", "system", "period", "assigner")),
        Field("display", "string"));

    public static JsonObject Reference() => Struct(
        Field("reference", "string"),
        Field("type", "string"),
        Field("identifier", Struct(
            Field("extension", "string"),
            Field("use", "string"),
            Field("value", "string"),
            Field("system", "string"),
            Field("type", CodeableConcept()),
            Field("period", Period()),
            Field("assigner", ShallowReference()))),
        Field("display", "string"),
        Field("extension", "string"),
        Field("id", "string"),
        Field("idOrig", "string"),
        Field("msftSourceReference", "string"));

    public static JsonObject Identifier(JsonObject assigner) => Struct(
        Field("extension", "string"),
        Field("use", "string"),
        Field("value", "string"),
        Field("system", "string"),
        Field("type", CodeableConcept()),
        Field("period", Period()),
        Field("assigner", assigner));

    // Expected cells in the layout of the types above, written out with every null explicit.
    public const string NullPeriod = """{"extension":null,"id":null,"start":null,"end":null}""";
    public const string NullConcept = """{"extension":null,"id":null,"coding":null,"text":null}""";
    public const string NullShallowReference = """{"extension":null,"id":null,"reference":null,"type":null,"identifier":null,"display":null}""";
    public const string AllNullIdentifier = """{"extension":null,"use":null,"value":null,"system":null,"type":null,"period":null,"assigner":null}""";

    public static string Coding(string? system, string? code, string? display = null, string? userSelected = "null") =>
        $$"""{"extension":null,"id":null,"system":{{Text(system)}},"version":null,"code":{{Text(code)}},"display":{{Text(display)}},"userSelected":{{userSelected}}}""";

    public static string FhirIdCoding => Coding(FhirIdSystem, "fhirId", "FHIR Id");

    public static string Concept(string? text, params string[] codings) =>
        $$"""{"extension":null,"id":null,"coding":[{{string.Join(',', codings)}}],"text":{{Text(text)}}}""";

    public static string FhirIdConcept => Concept("FHIR Id", FhirIdCoding);

    public static string ExpectedIdentifier(
        string? value = null,
        string? system = null,
        string type = NullConcept,
        string? use = null,
        string assigner = NullShallowReference,
        string period = NullPeriod) =>
        $$"""{"extension":null,"use":{{Text(use)}},"value":{{Text(value)}},"system":{{Text(system)}},"type":{{type}},"period":{{period}},"assigner":{{assigner}}}""";

    public static string ExpectedReference(
        string identifier,
        string? source = null,
        string? type = null,
        string? id = null,
        string? idOrig = null,
        string? display = null,
        string? reference = null,
        string? extension = null) =>
        $$"""{"reference":{{Text(reference)}},"type":{{Text(type)}},"identifier":{{identifier}},"display":{{Text(display)}},"extension":{{Text(extension)}},"id":{{Text(id)}},"idOrig":{{Text(idOrig)}},"msftSourceReference":{{Text(source)}}}""";
}
