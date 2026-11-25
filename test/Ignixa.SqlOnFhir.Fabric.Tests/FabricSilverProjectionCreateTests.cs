// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Specification.Generated;
using static Ignixa.SqlOnFhir.Fabric.Tests.SilverProjectionHarness;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>Compiling a contract: every column must be one the projection knows how to fill.</summary>
public class FabricSilverProjectionCreateTests
{
    private const string Secret = "invented-secret-marker";

    [Fact]
    public async Task GivenANonR4Schema_WhenCreated_ThenRejected()
    {
        var contract = await ContractAsync("Patient");

        Should.Throw<ArgumentException>(() => FabricSilverProjection.Create(contract, new R5CoreSchemaProvider()))
            .ParamName.ShouldBe("schema");
    }

    [Fact]
    public async Task GivenNullArguments_WhenCreated_ThenRejected()
    {
        var contract = await ContractAsync("Patient");

        Should.Throw<ArgumentNullException>(() => FabricSilverProjection.Create(null!, R4));
        Should.Throw<ArgumentNullException>(() => FabricSilverProjection.Create(contract, null!));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("msftModifiedDatetime")]
    [InlineData("msftCreatedDatetime")]
    public async Task GivenAContractWithoutARequiredColumn_WhenCreated_ThenRejectedNamingIt(string missing)
    {
        JsonNode[] fields =
        [
            Field("msftModifiedDatetime", "timestamp"),
            Field("id", "string"),
            Field("msftCreatedDatetime", "timestamp"),
        ];
        var contract = await RawContractAsync(fields.Where(field => (string)field["name"]! != missing).ToArray());

        var error = Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));

        error.Message.ShouldContain(missing);
    }

    [Fact]
    public async Task GivenOnlyTheRequiredColumns_WhenCreated_ThenRowsHaveOnlyThem()
    {
        var contract = await RawContractAsync(
            Field("id", "string"),
            Field("msftCreatedDatetime", "timestamp"),
            Field("msftModifiedDatetime", "timestamp"));

        var row = FabricSilverProjection.Create(contract, R4).Project("""{"resourceType":"Patient","id":"p1"}""");

        row.ColumnNames.ShouldBe(["id", "msftCreatedDatetime", "msftModifiedDatetime"]);
    }

    [Theory]
    [InlineData("msftModifiedDatetime", "string")]
    [InlineData("msftCreatedDatetime", "date")]
    [InlineData("msftSourceSystem", "integer")]
    [InlineData("msftFilePath", "boolean")]
    [InlineData("id", "long")]
    [InlineData("identifier", "string")]
    public async Task GivenAFixedColumnOfTheWrongType_WhenCreated_ThenRejectedByPosition(string column, string type)
    {
        var contract = await RawContractAsync(
            Field("msftModifiedDatetime", column == "msftModifiedDatetime" ? type : "timestamp"),
            Field("id", column == "id" ? type : "string"),
            Field("msftCreatedDatetime", column == "msftCreatedDatetime" ? type : "timestamp"),
            Field(column is "msftSourceSystem" or "msftFilePath" or "identifier" ? column : "other", type));

        var error = Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));

        error.Message.ShouldContain("schema.fields[");
    }

    [Fact]
    public async Task GivenAnIdentifierArrayOfStrings_WhenCreated_ThenRejected()
    {
        var contract = await ContractAsync("Patient", Field("identifier", ArrayOf("string")));

        Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("float")]
    [InlineData("double")]
    public async Task GivenAnUnsupportedPrimitiveColumn_WhenCreated_ThenRejectedWithoutNamingIt(string type)
    {
        var contract = await ContractAsync("Patient", Field(Secret, type));

        var error = Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));

        error.Message.ShouldContain("schema.fields[5].type");
        error.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenAnUnsupportedTypeNestedInAStruct_WhenCreated_ThenRejectedByPosition()
    {
        var contract = await ContractAsync("Patient", Field("x", ArrayOf(Struct(Field("ok", "string"), Field(Secret, "double")))));

        var error = Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));

        error.Message.ShouldContain("schema.fields[5].type.elementType.fields[1].type");
        error.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenAMapColumn_WhenCreated_ThenRejected()
    {
        var map = new JsonObject
        {
            ["type"] = "map",
            ["keyType"] = "string",
            ["valueType"] = "string",
            ["valueContainsNull"] = true,
        };
        var contract = await ContractAsync("Patient", Field(Secret, map));

        var error = Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4));

        error.Message.ShouldNotContain(Secret);
    }

    [Theory]
    [InlineData("integer")]
    [InlineData("timestamp")]
    public async Task GivenACompanionThatIsNotAString_WhenCreated_ThenRejected(string type)
    {
        var contract = await ContractAsync("Patient", Field("name", ArrayOf(Strings("text"))), Field("name_string", type));

        Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4)).Message.ShouldContain("schema.fields[6]");
    }

    [Fact]
    public async Task GivenACompanionOfAPrimitiveColumn_WhenCreated_ThenRejected()
    {
        var contract = await ContractAsync("Patient", Field("gender", "string"), Field("gender_string", "string"));

        Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4)).Message.ShouldContain("schema.fields[6]");
    }

    [Theory]
    [InlineData("string")]
    [InlineData("date")]
    public async Task GivenAFlattenedColumnOfAnotherType_WhenCreated_ThenRejected(string type)
    {
        var contract = await ContractAsync(
            "Patient",
            Field("meta", Struct(Field("lastUpdated", "timestamp"))),
            Field("meta_lastUpdated", type));

        Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4)).Message.ShouldContain("schema.fields[6]");
    }

    [Fact]
    public async Task GivenAFlattenedStructColumn_WhenTypesMatchStructurally_ThenItIsCopied()
    {
        var concept = CodeableConcept();
        var contract = await ContractAsync(
            "Patient",
            Field("x", Struct(Field("code", concept))),
            Field("x_code", concept.DeepClone()));

        var row = FabricSilverProjection.Create(contract, R4).Project("""{"resourceType":"Patient","id":"p1","x":{"code":{"text":"T"}}}""");

        row["x_code"].ShouldBeJson("""{"extension":null,"id":null,"coding":null,"text":"T"}""");
    }

    public static TheoryData<JsonNode, JsonNode> FlattenedTypeMismatches => new()
    {
        { ArrayOf("string"), ArrayOf("string", containsNull: false) },
        { Strings("text"), Struct(Field("text", "string", nullable: false)) },
        { Strings("text"), Strings("other") },
        { Strings("text"), Strings("text", "more") },
        { ArrayOf(Strings("text")), ArrayOf(Strings("other")) },
        { "decimal(10,3)", "decimal(10,2)" },
        { "decimal(10,3)", "decimal(11,3)" },
    };

    [Theory]
    [MemberData(nameof(FlattenedTypeMismatches))]
    public async Task GivenAFlattenedColumnThatDiffersInDetail_WhenCreated_ThenRejected(JsonNode fieldType, JsonNode flattenedType)
    {
        var contract = await ContractAsync(
            "Patient",
            Field("x", Struct(Field("y", fieldType.DeepClone()))),
            Field("x_y", flattenedType.DeepClone()));

        Should.Throw<InvalidDataException>(() => FabricSilverProjection.Create(contract, R4)).Message.ShouldContain("schema.fields[6]");
    }

    private static async Task<FabricSilverContract> RawContractAsync(params JsonNode[] fields)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(fields)));
        return await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);
    }
}
