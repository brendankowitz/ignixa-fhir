// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

/// <summary>
/// The loader's strictness at its trust boundary: exact text encoding, ambiguous or undecodable JSON, features that
/// protocol 1/4 cannot carry, the version 0 scope and the layout fallback rules.
/// </summary>
public class FabricSilverContractStrictnessTests
{
    private const string InventedMarker = "invented-marker";

    [Fact]
    public async Task GivenNonAsciiNames_WhenLoadingStrictUtf8_ThenTheyAreAcceptedExactly()
    {
        using var root = new SyntheticSilverRoot();
        var schema = SchemaOf(Field("na\u00EFve-\u65E5\u672C-\U0001F600", "string")).ToJsonString(RawText);
        root.WriteSilverLogBytes(
            "Patient",
            new UTF8Encoding(false).GetBytes(string.Join('\n', ProtocolLine(), MetaDataLine(schemaString: schema))));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.Fields[0].GetProperty("name").GetString().ShouldBe("na\u00EFve-\u65E5\u672C-\U0001F600");
        Encoding.UTF8.GetBytes(contract.SchemaString).Length.ShouldBeGreaterThan(contract.SchemaString.Length);
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf32le-bom")]
    [InlineData("utf16le-no-bom")]
    public async Task GivenLogInAnotherEncodingOrWithBom_WhenLoading_ThenItIsRejectedNotDetected(string kind)
    {
        using var root = new SyntheticSilverRoot();
        var text = string.Join('\n', ProtocolLine(), MetaDataLine());
        byte[] bytes = kind switch
        {
            "utf8-bom" => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)],
            "utf16le-bom" => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)],
            "utf16be-bom" => [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)],
            "utf32le-bom" => [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes(text)],
            _ => Encoding.Unicode.GetBytes(text),
        };
        root.WriteSilverLogBytes("Patient", bytes);

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF })]
    [InlineData(new byte[] { 0xC3, 0x28 })]
    [InlineData(new byte[] { 0xE2, 0x82 })]
    [InlineData(new byte[] { 0xC0, 0xAF })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    public async Task GivenMalformedUtf8InAnUnrelatedAction_WhenLoading_ThenItIsRejectedWithoutTheBytes(byte[] bad)
    {
        using var root = new SyntheticSilverRoot();
        byte[] Line(string text) => [.. Encoding.UTF8.GetBytes(text), (byte)'\n'];
        root.WriteSilverLogBytes(
            "Patient",
            [
                .. Line(ProtocolLine()),
                .. Line(MetaDataLine()),
                .. Encoding.UTF8.GetBytes("""{"commitInfo":{"operation":"x"""),
                .. bad,
                .. Encoding.UTF8.GetBytes("\"}}\n"),
            ]);

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("UTF-8");
        exception.Message.ToUpperInvariant().ShouldNotContain("0X");
        exception.Message.ShouldNotContain(((int)bad[0]).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("""{"commitInfo":{"operation":"a\ud800"}}""")]
    [InlineData("""{"commitInfo":{"a\udc00":"x"}}""")]
    [InlineData("""{"protocol":{"minReaderVersion":1,"minWriterVersion":4,"\ud800":1}}""")]
    public async Task GivenLoneSurrogateEscapeInTheLog_WhenLoading_ThenItIsRejectedAsInvalidText(string badLine)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(), badLine);

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("Unicode");
    }

    [Theory]
    [InlineData("""{"type":"struct","fields":[{"name":"a\ud800","type":"string","nullable":true,"metadata":{}}]}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":"string","nullable":true,"metadata":{"k\udfff":"v"}}]}""")]
    public async Task GivenLoneSurrogateEscapeInTheSchema_WhenLoading_ThenItIsRejectedAsInvalidText(string schemaString)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: schemaString));

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("Unicode");
    }

    [Theory]
    [InlineData("""{"protocol":{"minReaderVersion":1,"minReaderVersion":2,"minWriterVersion":4}}""")]
    [InlineData("""{"protocol":{"minReaderVersion":1,"minWriterVersion":4,"min\u0057riterVersion":4}}""")]
    public async Task GivenRepeatedPropertyInAProtocolLine_WhenLoading_ThenItIsRejectedAsAmbiguous(string protocolLine)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", protocolLine, MetaDataLine());

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("repeats a property name");
    }

    [Theory]
    [InlineData("""{"type":"struct","fields":[{"name":"a","name":"b","type":"string","nullable":true,"metadata":{}}]}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":"string","type":"long","nullable":true,"metadata":{}}]}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":"string","nullable":true,"nullable":false,"metadata":{}}]}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":{"type":"array","elementType":"string","elementType":"long","containsNull":true},"nullable":true,"metadata":{}}]}""")]
    [InlineData("""{"type":"struct","fields":[{"name":"a","type":"string","nullable":true,"metadata":{"k":"1","k":"2"}}]}""")]
    [InlineData("""{"type":"struct","fields":[],"fields":[{"name":"a","type":"string","nullable":true,"metadata":{}}]}""")]
    public async Task GivenRepeatedPropertyInTheSchema_WhenLoading_ThenItIsRejectedAsAmbiguous(string schemaString)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: schemaString));

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldContain("repeats a property name");
    }

    [Theory]
    [InlineData("readerFeatures")]
    [InlineData("writerFeatures")]
    [InlineData("anythingElse")]
    public async Task GivenProtocolOneFourWithFeatureLists_WhenLoading_ThenItIsRejected(string extraProperty)
    {
        using var root = new SyntheticSilverRoot();
        var protocol = $$$"""{"protocol":{"minReaderVersion":1,"minWriterVersion":4,"{{{extraProperty}}}":["timestampNtz"]}}""";
        root.WriteSilverLog("Patient", protocol, MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenWrongProtocolVersions_WhenLoading_ThenMessageDoesNotEchoThem()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(reader: 987, writer: 654), MetaDataLine());

        var exception = await ShouldBeRejected(root);

        exception.Message.ShouldNotContain("987");
        exception.Message.ShouldNotContain("654");
    }

    [Theory]
    [InlineData("delta.feature.timestampNtz", "supported")]
    [InlineData("delta.feature.deletionVectors", "enabled")]
    [InlineData("Delta.Feature.Anything", "supported")]
    [InlineData("delta.columnMapping.mode", "name")]
    [InlineData("delta.columnMapping.mode", "none")]
    [InlineData("delta.enableDeletionVectors", "true")]
    [InlineData("delta.enableRowTracking", "true")]
    [InlineData("delta.checkpointPolicy", "v2")]
    [InlineData("delta.enableTypeWidening", "true")]
    [InlineData("delta.enableIcebergCompatV2", "true")]
    [InlineData("delta.universalFormat.enabledFormats", "iceberg")]
    public async Task GivenConfigurationForAFeatureBeyondProtocolOneFour_WhenLoading_ThenItIsRejected(string key, string value)
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration[key] = value;
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenConfigurationKeysDifferingOnlyByCase_WhenLoading_ThenItIsRejectedAsAmbiguous()
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration["DELTA.PARQUET.VORDER.ENABLED"] = "false";
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("delta.columnMapping.id", "1")]
    [InlineData("delta.columnMapping.physicalName", "col-1")]
    [InlineData("delta.identity.start", "1")]
    public async Task GivenFieldMetadataForAFeatureBeyondProtocolOneFour_WhenLoading_ThenItIsRejected(string key, string value)
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field["metadata"] = new JsonObject { [key] = value };
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFieldMetadataFromProtocolOneFour_WhenLoading_ThenItIsCarriedThroughUninterpreted()
    {
        using var root = new SyntheticSilverRoot();
        var field = Field("a", "string");
        field["metadata"] = new JsonObject { ["delta.generationExpression"] = "1 + 1", ["invented"] = "x" };
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(field)));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        var metadata = contract.Fields[0].GetProperty("metadata");
        metadata.GetProperty("delta.generationExpression").GetString().ShouldBe("1 + 1");
        metadata.GetProperty("invented").GetString().ShouldBe("x");
    }

    [Fact]
    public async Task GivenTimestampNtzNestedInComplexTypes_WhenLoading_ThenItIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var nested = new JsonObject
        {
            ["type"] = "array",
            ["elementType"] = new JsonObject
            {
                ["type"] = "map",
                ["keyType"] = "string",
                ["valueType"] = "timestamp_ntz",
                ["valueContainsNull"] = true,
            },
            ["containsNull"] = true,
        };
        root.WriteSilverLog("Patient", ProtocolLine(), SchemaLine(SchemaOf(Field("a", nested))));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenLaterCommitsAndCheckpoint_WhenLoading_ThenOnlyTheVersionZeroTemplateIsRead()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());
        var laterSchema = SchemaOf(Field("changedLater", "string")).ToJsonString();
        var laterConfiguration = ValidConfiguration();
        laterConfiguration["delta.enableChangeDataFeed"] = "false";
        root.WriteSilverLogFile(
            "Patient",
            "00000000000000000001.json",
            ProtocolLine(reader: 3, writer: 7),
            MetaDataLine(schemaString: laterSchema, configuration: laterConfiguration));
        root.WriteSilverLogFile("Patient", "00000000000000000010.checkpoint.parquet", "not a real checkpoint");
        root.WriteSilverLogFile("Patient", "_last_checkpoint", """{"version":10}""");

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.SchemaString.ShouldBe(SchemaString());
        contract.MinReaderVersion.ShouldBe(1);
        contract.MinWriterVersion.ShouldBe(4);
        contract.Configuration["delta.enableChangeDataFeed"].ShouldBe("True");
    }

    [Fact]
    public async Task GivenOnlyLaterCommits_WhenLoading_ThenNoCreationLogIsFound()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLogFile("Patient", "00000000000000000001.json", ProtocolLine(), MetaDataLine());

        var exception = await Should.ThrowAsync<FileNotFoundException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));

        exception.Message.ShouldContain("Silver root layout");
        exception.Message.ShouldContain("package layout");
    }

    [Fact]
    public async Task GivenSilverTableDirectoryWithoutLog_WhenLoading_ThenPackageLayoutIsUsed()
    {
        using var root = new SyntheticSilverRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, "Patient", "_delta_log"));
        root.WritePackageLog("Patient", ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.SchemaString.ShouldBe(SchemaString());
    }

    [Fact]
    public async Task GivenUnreadableSilverRootLog_WhenPackageLayoutExists_ThenTheFailureSurfacesWithoutFallback()
    {
        using var root = new SyntheticSilverRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, "Patient", "_delta_log", LogFileName));
        root.WritePackageLog("Patient", ProtocolLine(), MetaDataLine());

        var exception = await Should.ThrowAsync<Exception>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));

        (exception is UnauthorizedAccessException or IOException).ShouldBeTrue(exception.GetType().Name);
    }

    [Fact]
    public async Task GivenInvalidSilverRootLog_WhenPackageLayoutIsValid_ThenTheInvalidLogIsNotSkipped()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(reader: 2, writer: 5), MetaDataLine());
        root.WritePackageLog("Patient", ProtocolLine(), MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenRejectedContract_WhenInspectingTheFailure_ThenNoInputTextIsQuoted()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            ProtocolLine(),
            MetaDataLine(schemaString: SchemaOf(Field(InventedMarker, "bad-" + InventedMarker)).ToJsonString()));

        var exception = await ShouldBeRejected(root);

        exception.ToString().ShouldNotContain(InventedMarker);
    }

    private static async Task<InvalidDataException> ShouldBeRejected(SyntheticSilverRoot root) =>
        await Should.ThrowAsync<InvalidDataException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));
}
