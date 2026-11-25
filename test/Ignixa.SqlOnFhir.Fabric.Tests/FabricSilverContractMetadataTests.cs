// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

public class FabricSilverContractMetadataTests
{
    [Theory]
    [InlineData(2, 4)]
    [InlineData(1, 3)]
    [InlineData(1, 5)]
    [InlineData(3, 7)]
    public async Task GivenUnexpectedProtocolVersions_WhenLoading_ThenContractIsRejected(int reader, int writer)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(reader, writer), MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("""{"protocol":{"minWriterVersion":4}}""")]
    [InlineData("""{"protocol":{"minReaderVersion":1}}""")]
    [InlineData("""{"protocol":{"minReaderVersion":"1","minWriterVersion":4}}""")]
    [InlineData("""{"protocol":{"minReaderVersion":1.5,"minWriterVersion":4}}""")]
    [InlineData("""{"protocol":[1,4]}""")]
    public async Task GivenMalformedProtocolAction_WhenLoading_ThenContractIsRejected(string protocolLine)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", protocolLine, MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenMissingProtocolAction_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", CommitInfoLine(), MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenMissingMetaDataAction_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", CommitInfoLine(), ProtocolLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenDuplicateProtocolAction_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), ProtocolLine(), MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenDuplicateMetaDataAction_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(), MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenLineCarryingTwoActions_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(), """{"commitInfo":{},"txn":{}}""");

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenUnreadableLineAfterOneGoodLine_WhenLoading_ThenMessageNamesTheLineNumber()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), "{not json", MetaDataLine());

        var exception = await Should.ThrowAsync<InvalidDataException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));

        exception.Message.ShouldContain("line 2");
    }

    [Fact]
    public async Task GivenMetaDataThatIsNotAnObject_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), """{"metaData":[]}""");

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFormatThatIsNotAnObject_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(format: JsonValue.Create("parquet")));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenFormatWithoutProvider_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(format: new JsonObject()));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenPartitionColumnsThatAreNotAnArray_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(partitionColumns: JsonValue.Create("none")));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenRepeatedConfigurationProperty_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        const string Property = "\"delta.parquet.vorder.enabled\":\"true\"";
        var line = MetaDataLine().Replace(Property, Property + "," + Property, StringComparison.Ordinal);
        root.WriteSilverLog("Patient", ProtocolLine(), line);

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    public async Task GivenUnreadableActionLine_WhenLoading_ThenContractIsRejected(string badLine)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), badLine, MetaDataLine());

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenNonParquetFormat_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            ProtocolLine(),
            MetaDataLine(format: new JsonObject { ["provider"] = "csv", ["options"] = new JsonObject() }));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenMissingFormat_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLineWith(metaData => metaData.Remove("format")));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenPartitionedTable_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(partitionColumns: new JsonArray("rowKey")));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenMissingPartitionColumns_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLineWith(metaData => metaData.Remove("partitionColumns")));

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("delta.enableChangeDataFeed")]
    [InlineData("delta.parquet.vorder.enabled")]
    public async Task GivenRequiredPropertyMissing_WhenLoading_ThenContractIsRejected(string missingKey)
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration.Remove(missingKey);
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        await ShouldBeRejected(root);
    }

    [Theory]
    [InlineData("delta.enableChangeDataFeed", "false")]
    [InlineData("delta.parquet.vorder.enabled", "false")]
    [InlineData("delta.parquet.vorder.enabled", "")]
    public async Task GivenRequiredPropertyDisabled_WhenLoading_ThenContractIsRejected(string key, string value)
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration[key] = value;
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenNonStringConfigurationValue_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration["delta.appendOnly"] = true;
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenConfigurationThatIsNotAnObject_WhenLoading_ThenContractIsRejected()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: new JsonArray()));

        await ShouldBeRejected(root);
    }

    [Fact]
    public async Task GivenExtraConfiguration_WhenLoading_ThenAllPropertiesAreCarriedThrough()
    {
        using var root = new SyntheticSilverRoot();
        var configuration = ValidConfiguration();
        configuration["delta.invented.setting"] = "7";
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(configuration: configuration));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.Configuration["delta.invented.setting"].ShouldBe("7");
        contract.Configuration.Count.ShouldBe(3);
    }

    private static async Task ShouldBeRejected(SyntheticSilverRoot root) =>
        await Should.ThrowAsync<InvalidDataException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));
}
