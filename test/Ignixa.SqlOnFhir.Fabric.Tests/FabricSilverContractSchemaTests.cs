// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

public class FabricSilverContractSchemaTests
{
    private const string Secret = "invented-secret-marker";

    [Theory]
    [InlineData("decimal(1,0)")]
    [InlineData("decimal(38,38)")]
    [InlineData("decimal(10,2)")]
    [InlineData("string")]
    [InlineData("long")]
    [InlineData("integer")]
    [InlineData("short")]
    [InlineData("byte")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("boolean")]
    [InlineData("binary")]
    [InlineData("date")]
    [InlineData("timestamp")]
    [InlineData("timestamp_ntz")]
    public async Task GivenSupportedPrimitiveType_WhenLoading_ThenSchemaIsAccepted(string type)
    {
        using var root = new SyntheticSilverRoot();
        var schema = SchemaOf(Field("value", type));
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(schema));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.SchemaString.ShouldBe(schema.ToJsonString());
    }

    [Theory]
    [InlineData("varchar(10)")]
    [InlineData("decimal")]
    [InlineData("decimal(0,0)")]
    [InlineData("decimal(39,0)")]
    [InlineData("decimal(10,11)")]
    [InlineData("decimal(10,-1)")]
    [InlineData("decimal(10, 2)")]
    [InlineData("decimal(a,b)")]
    [InlineData("String")]
    [InlineData("")]
    public async Task GivenUnsupportedPrimitiveType_WhenLoading_ThenContractIsRejected(string type)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(Field("value", type))));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenMalformedSchemaString_WhenLoading_ThenContractIsRejectedWithoutEchoingIt()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: "{" + Secret));

        var exception = await ShouldBeRejected(root);

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenMissingSchemaString_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLineWith(metaData => metaData.Remove("schemaString")));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenSchemaStringThatIsNotAString_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            ProtocolLine(),
            MetaDataLineWith(metaData => metaData["schemaString"] = SchemaOf(Field("a", "string"))));

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("""{"type":"array","elementType":"string","containsNull":true}""")]
    [InlineData("""{"type":"struct"}""")]
    [InlineData("""{"type":"struct","fields":{}}""")]
    [InlineData("""{"fields":[]}""")]
    [InlineData("[]")]
    [InlineData("\"string\"")]
    public async Task GivenRootThatIsNotAStruct_WhenLoading_ThenContractIsRejected(string schemaString)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: schemaString));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenEmptyStruct_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf()));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldWithNonStringName_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field["name"] = 5;
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenInvalidSecondField_WhenLoading_ThenMessageLocatesItByIndexNotByName()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            ProtocolLine(),
            SchemaLine(SchemaOf(Field("a", "string"), Field(Secret, "nope"))));

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("fields[1]");
        exception.Message.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenFieldWithoutName_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field.Remove("name");
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldWithEmptyName_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(Field(string.Empty, "string"))));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldWithoutType_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field.Remove("type");
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldWithoutNullableFlag_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field.Remove("nullable");
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenNullableFlagThatIsNotBoolean_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field["nullable"] = "true";
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldMetadataThatIsNotAnObject_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field["metadata"] = "x";
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldElementThatIsNotAnObject_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(JsonValue.Create("a")!)));

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a", "A")]
    public async Task GivenDuplicateTopLevelFieldNames_WhenLoading_ThenContractIsRejectedWithoutEchoingNames(
        string first,
        string second)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            ProtocolLine(),
            SchemaLine(SchemaOf(Field(Secret + first, "string"), Field(Secret + second, "string"))));

        var exception = await ShouldBeRejected(root);

        exception.ToString().ShouldNotContain(Secret);
    }

    [Fact]
    public async Task GivenDuplicateNamesInsideNestedStruct_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var nested = SchemaOf(Field("x", "string"), Field("x", "long"));
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(Field("outer", nested))));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenSameNameInDifferentStructs_WhenLoading_ThenSchemaIsAccepted()
    {
        using var root = new SyntheticSilverRoot();
        var nested = SchemaOf(Field("x", "string"));
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(Field("x", nested))));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.Fields.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("""{"type":"array","containsNull":true}""")]
    [InlineData("""{"type":5}""")]
    [InlineData("""{"type":null,"elementType":"string","containsNull":true}""")]
    [InlineData("""{"type":"array","elementType":"string"}""")]
    [InlineData("""{"type":"array","elementType":"string","containsNull":"yes"}""")]
    [InlineData("""{"type":"array","elementType":"varchar","containsNull":true}""")]
    [InlineData("""{"type":"map","keyType":"string","valueType":"long"}""")]
    [InlineData("""{"type":"map","valueType":"long","valueContainsNull":true}""")]
    [InlineData("""{"type":"map","keyType":"string","valueContainsNull":true}""")]
    [InlineData("""{"type":"map","keyType":"string","valueType":"decimal(40,2)","valueContainsNull":true}""")]
    [InlineData("""{"type":"set","elementType":"string"}""")]
    [InlineData("""{"elementType":"string"}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":"nope","nullable":true,"metadata":{}}]}""")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task GivenMalformedNestedType_WhenLoading_ThenContractIsRejected(string typeJson)
    {
        using var root = new SyntheticSilverRoot();
        var schema = SchemaOf(Field("value", JsonNode.Parse(typeJson)!));
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(schema));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenSchemaNestedBeyondReaderLimit_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        const int Depth = 200;
        var schemaString =
            string.Concat(Enumerable.Repeat("{\"type\":\"array\",\"containsNull\":true,\"elementType\":", Depth))
            + "\"string\""
            + new string('}', Depth);
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: schemaString));

        await ShouldBeRejected(root);
    }

    private static async Task<InvalidDataException> ShouldBeRejected(SyntheticSilverRoot root) =>
        await Should.ThrowAsync<InvalidDataException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));
}
