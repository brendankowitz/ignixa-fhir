// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using static Ignixa.SqlOnFhir.Fabric.Tests.SyntheticSilverRoot;

namespace Ignixa.SqlOnFhir.Fabric.Tests;

public class FabricSilverContractLoadTests
{
    [Theory]
    [InlineData("Patient")]
    [InlineData("Observation")]
    public async Task GivenValidSilverRoot_WhenLoading_ThenSchemaStringIsExactlyTheLoggedString(string resourceType)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(resourceType, CommitInfoLine(), ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, resourceType, CancellationToken.None);

        contract.ResourceType.ShouldBe(resourceType);
        contract.SchemaString.ShouldBe(SchemaString());
    }

    [Fact]
    public async Task GivenNestedSchema_WhenLoading_ThenFieldsKeepOrderNestingAndDecimalPrecision()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.Fields.Select(field => field.GetProperty("name").GetString())
            .ShouldBe(["rowKey", "tags", "detail", "lookup", "seenOn"]);
        contract.Fields[0].GetProperty("nullable").GetBoolean().ShouldBeFalse();

        var tagElement = contract.Fields[1].GetProperty("type").GetProperty("elementType");
        var weights = tagElement.GetProperty("fields")[1].GetProperty("type");
        weights.GetProperty("elementType").GetString().ShouldBe("decimal(18,6)");

        var inner = contract.Fields[2].GetProperty("type").GetProperty("fields")[1].GetProperty("type");
        inner.GetProperty("fields")[1].GetProperty("type").GetString().ShouldBe("timestamp");
        contract.Fields[2].GetProperty("type").GetProperty("fields")[0].GetProperty("type").GetString()
            .ShouldBe("decimal(38,10)");
    }

    [Fact]
    public async Task GivenValidLog_WhenLoading_ThenProtocolAndConfigurationAreExposed()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.MinReaderVersion.ShouldBe(1);
        contract.MinWriterVersion.ShouldBe(4);
        contract.Configuration.ShouldBe(
            new Dictionary<string, string>
            {
                ["delta.enableChangeDataFeed"] = "True",
                ["delta.parquet.vorder.enabled"] = "true",
            },
            ignoreOrder: true);
    }

    [Fact]
    public async Task GivenLoadedContract_WhenMutatingExposedCollections_ThenTheyAreReadOnly()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        ((IList<JsonElement>)contract.Fields).IsReadOnly.ShouldBeTrue();
        ((IDictionary<string, string>)contract.Configuration).IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenActionsInAnyOrderWithBlankAndUnrelatedLines_WhenLoading_ThenActionsAreFoundByName()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog(
            "Patient",
            string.Empty,
            MetaDataLine(),
            CommitInfoLine(),
            "   ",
            """{"txn":{"appId":"x","version":1}}""",
            ProtocolLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.SchemaString.ShouldBe(SchemaString());
        contract.MinWriterVersion.ShouldBe(4);
    }

    [Fact]
    public async Task GivenPackageRoot_WhenLoading_ThenSilverTableUnderPackageLayoutIsRead()
    {
        using var root = new SyntheticSilverRoot();
        root.WritePackageLog("Observation", ProtocolLine(), MetaDataLine());

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Observation", CancellationToken.None);

        contract.SchemaString.ShouldBe(SchemaString());
    }

    [Fact]
    public async Task GivenBothLayouts_WhenLoading_ThenSilverRootWins()
    {
        using var root = new SyntheticSilverRoot();
        var direct = SchemaOf(Field("direct", "string")).ToJsonString();
        var packaged = SchemaOf(Field("packaged", "string")).ToJsonString();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine(schemaString: direct));
        root.WritePackageLog("Patient", ProtocolLine(), MetaDataLine(schemaString: packaged));

        var contract = await FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None);

        contract.SchemaString.ShouldBe(direct);
    }

    [Fact]
    public async Task GivenOtherResourceTypeOnlyPresent_WhenLoading_ThenLogIsNotFound()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Observation", ProtocolLine(), MetaDataLine());

        await Should.ThrowAsync<FileNotFoundException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));
    }

    [Theory]
    [InlineData("Encounter")]
    [InlineData("patient")]
    [InlineData("Patient ")]
    [InlineData("")]
    [InlineData("../Patient")]
    [InlineData("..\\Patient")]
    [InlineData("Patient/../Observation")]
    [InlineData("Patient/")]
    public async Task GivenResourceTypeOutsideAllowlist_WhenLoading_ThenArgumentIsRejectedBeforeAnyRead(string resourceType)
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());

        await Should.ThrowAsync<ArgumentException>(
            FabricSilverContract.LoadAsync(root.Path, resourceType, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GivenBlankRoot_WhenLoading_ThenArgumentIsRejected(string rootPath)
    {
        await Should.ThrowAsync<ArgumentException>(
            FabricSilverContract.LoadAsync(rootPath, "Patient", CancellationToken.None));
    }

    [Fact]
    public async Task GivenCanceledToken_WhenLoading_ThenCancellationPropagates()
    {
        using var root = new SyntheticSilverRoot();
        root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", cts.Token));
    }

    [Fact]
    public async Task GivenCanceledToken_WhenLoadingMissingTable_ThenCancellationWinsOverMissingLog()
    {
        using var root = new SyntheticSilverRoot();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", cts.Token));
    }

    [Fact]
    public async Task GivenLogLockedByAnotherWriter_WhenLoading_ThenIoFailurePropagates()
    {
        using var root = new SyntheticSilverRoot();
        var logPath = root.WriteSilverLog("Patient", ProtocolLine(), MetaDataLine());
        await using var locked = new FileStream(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Should.ThrowAsync<IOException>(
            FabricSilverContract.LoadAsync(root.Path, "Patient", CancellationToken.None));
    }
}
