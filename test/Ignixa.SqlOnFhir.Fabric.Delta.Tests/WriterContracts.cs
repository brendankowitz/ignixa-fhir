// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Invented Silver contracts the writer can merge: the lineage columns, <c>meta</c> with its flattened
/// <c>meta_lastUpdated</c>, and columns chosen to exercise every Arrow conversion. Nothing here is derived from a
/// real Healthcare data solutions package.
/// </summary>
internal static class WriterContracts
{
    public const string Namespace = "https://example.org/fhir";
    public const string OtherNamespace = "https://other.example.org/fhir";
    public const string FirstFile = "Files/landing/batch-001.ndjson";
    public const string SecondFile = "Files/landing/batch-002.ndjson";

    /// <summary>A column whose name the MERGE must quote.</summary>
    public const string HyphenatedColumn = "photo-note";

    public static readonly ISchema R4 = new R4CoreSchemaProvider();

    public static readonly DateTimeOffset FirstIngestion = new(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
    public static readonly DateTimeOffset SecondIngestion = new(2024, 6, 7, 8, 9, 10, TimeSpan.Zero);

    public static FabricSilverIngestion Ingestion(
        string sourceNamespace = Namespace,
        string filePath = FirstFile,
        DateTimeOffset? ingestedAt = null) =>
        new(sourceNamespace, filePath, ingestedAt ?? FirstIngestion);

    /// <summary>A Patient-shaped table: every Arrow conversion the writer performs, including deep nesting.</summary>
    public static JsonObject PatientSchema() => SchemaOf(
        Field("msftModifiedDatetime", "timestamp"),
        Field("id", "string", nullable: false),
        Field("resourceType", "string"),
        Field("msftSourceSystem", "string"),
        Field("msftFilePath", "string"),
        Field("meta", Struct(Field("versionId", "string"), Field("lastUpdated", "timestamp"))),
        Field("meta_lastUpdated", "timestamp"),
        Field("active", "boolean"),
        Field("gender", "string"),
        Field("birthDate", "date"),
        Field("multipleBirthInteger", "integer"),
        Field("visits", "long"),
        Field("balance", "decimal(38,10)"),
        Field("tiny", "decimal(38,38)"),
        Field("weights", ArrayOf("decimal(18,6)")),
        Field("seenAt", ArrayOf("timestamp")),
        Field("name", ArrayOf(Struct(Field("family", "string"), Field("given", ArrayOf("string"))))),
        Field("name_string", "string"),
        Field("contact", DeepStruct()),
        Field(HyphenatedColumn, "string"),
        Field("msftCreatedDatetime", "timestamp"));

    public static Task<FabricSilverContract> PatientAsync() => LoadAsync(PatientSchema(), "Patient");

    /// <summary><c>{relationship:{detail:{codes:[{code,rank}]}}, flag}</c>: a list of structs four levels down.</summary>
    public static JsonObject DeepStruct()
    {
        var code = Struct(Field("code", "string"), Field("rank", "short"));
        var detail = Struct(Field("codes", ArrayOf(code)));
        var relationship = Struct(Field("detail", detail));
        return Struct(Field("relationship", relationship), Field("flag", "byte"));
    }

    public static Task<FabricSilverContract> LoadAsync(JsonObject schema, string resourceType) =>
        SyntheticSilverTable.LoadContractAsync(schemaString: schema.ToJsonString(), resourceType: resourceType);

    public static FabricSilverTableBatch Batch(string tablePath, FabricSilverContract contract, params ResourceJsonNode[] resources) =>
        new(tablePath, contract, FabricSilverProjection.Create(contract, R4), resources);

    public static JsonObject SchemaOf(params JsonNode[] fields) => new()
    {
        ["type"] = "struct",
        ["fields"] = new JsonArray(fields),
    };

    public static JsonObject Field(string name, JsonNode type, bool nullable = true) => new()
    {
        ["name"] = name,
        ["type"] = type,
        ["nullable"] = nullable,
        ["metadata"] = new JsonObject(),
    };

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
}
