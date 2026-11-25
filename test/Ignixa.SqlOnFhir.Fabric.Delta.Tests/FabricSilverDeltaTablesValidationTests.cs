// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using DeltaLake.Errors;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

public sealed class FabricSilverDeltaTablesValidationTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string TablePath => _directory.Combine("Patient");

    [Fact]
    public async Task GivenTableCreatedByAnotherWriterMatchingTheContract_WhenEnsuringTable_ThenItIsValidated()
    {
        SyntheticSilverTable.WriteExistingTable(TablePath);

        var outcome = await EnsureAsync();

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    public async Task GivenTableWithAnotherProtocol_WhenEnsuringTable_ThenProtocolMismatchIsRaised(int reader, int writer)
    {
        SyntheticSilverTable.WriteExistingTable(TablePath, protocolLine: SyntheticSilverTable.ProtocolLine(reader, writer));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Protocol);
        exception.PropertyName.ShouldBeNull();
    }

    [Fact]
    public async Task GivenTableWithAnotherSchema_WhenEnsuringTable_ThenSchemaMismatchIsRaisedWithoutSchemaText()
    {
        const string schema = """{"type":"struct","fields":[{"name":"secretColumnName","type":"string","nullable":true,"metadata":{}}]}""";
        SyntheticSilverTable.WriteExistingTable(TablePath, SyntheticSilverTable.MetaDataLine(schemaString: schema));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Schema);
        exception.Message.ShouldNotContain("secretColumnName");
        exception.Message.ShouldNotContain("struct");
    }

    [Fact]
    public async Task GivenTableWithTheSameSchemaReformattedAndMembersReordered_WhenEnsuringTable_ThenItIsValidated()
    {
        var reformatted = SyntheticSilverTable.SchemaString()
            .Replace("\n", " \n  ", StringComparison.Ordinal)
            .Replace("""{"name":"born",  "type":"date","nullable":true,"metadata":{}}""", """{ "metadata": {}, "nullable": true, "type": "date", "name": "born" }""", StringComparison.Ordinal);
        reformatted.ShouldContain("\"name\": \"born\"");
        SyntheticSilverTable.WriteExistingTable(TablePath, SyntheticSilverTable.MetaDataLine(schemaString: reformatted));

        var outcome = await EnsureAsync();

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
    }

    [Theory]
    [InlineData("field name", """{"name":"scores",""", """{"name":"scoresRenamed",""")]
    [InlineData("nullability", """{"name":"id","type":"string","nullable":false""", """{"name":"id","type":"string","nullable":true""")]
    [InlineData("field metadata", "Größe – identifier", "Grosse - identifier")]
    [InlineData("type", "decimal(18,6)", "decimal(18,4)")]
    public async Task GivenTableWithASemanticSchemaChange_WhenEnsuringTable_ThenSchemaMismatchIsRaised(
        string change,
        string original,
        string replacement)
    {
        var schema = SyntheticSilverTable.SchemaString();
        schema.ShouldContain(original, customMessage: change);
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(schemaString: schema.Replace(original, replacement, StringComparison.Ordinal)));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Schema);
    }

    [Fact]
    public async Task GivenTableWithReorderedFields_WhenEnsuringTable_ThenSchemaMismatchIsRaised()
    {
        // Lines 1 and 2 are the "id" and "meta" fields, both comma-terminated.
        var fields = SyntheticSilverTable.SchemaString().Split('\n');
        (fields[1], fields[2]) = (fields[2], fields[1]);
        SyntheticSilverTable.WriteExistingTable(TablePath, SyntheticSilverTable.MetaDataLine(schemaString: string.Join('\n', fields)));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Schema);
    }

    [Fact]
    public async Task GivenPartitionedTable_WhenEnsuringTable_ThenPartitionColumnsMismatchIsRaised()
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(partitionColumns: new JsonArray("born")));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.PartitionColumns);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("false")]
    [InlineData(null)]
    public async Task GivenTableWhoseChangeFeedIsNotLowercaseTrue_WhenEnsuringTable_ThenChangeDataFeedMismatchNamesTheProperty(
        string? value)
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(
                configuration: SyntheticSilverTable.TableConfigurationWith(SyntheticSilverTable.ChangeDataFeed, value)));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.ChangeDataFeed);
        exception.PropertyName.ShouldBe(SyntheticSilverTable.ChangeDataFeed);
        exception.Message.ShouldContain("change data feed setting is incompatible");
        exception.Message.ShouldContain("not corrected automatically");
    }

    [Theory]
    [InlineData(SyntheticSilverTable.VOrder, null)]
    [InlineData(SyntheticSilverTable.VOrder, "False")]
    [InlineData(SyntheticSilverTable.LogRetention, "interval 7 days")]
    public async Task GivenTableMissingOrChangingAContractProperty_WhenEnsuringTable_ThenConfigurationMismatchNamesOnlyTheProperty(
        string name,
        string? value)
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(configuration: SyntheticSilverTable.TableConfigurationWith(name, value)));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Configuration);
        exception.PropertyName.ShouldBe(name);
        exception.Message.ShouldContain(name);
        if (value is not null)
        {
            exception.Message.ShouldNotContain(value);
        }
    }

    [Theory]
    [InlineData("delta.appendOnly", "true")]
    [InlineData("delta.constraints.positiveScore", "scores IS NOT NULL")]
    [InlineData("delta.enableDeletionVectors", "false")]
    [InlineData("Delta.Unknown.Setting", "x")]
    public async Task GivenTableWithAnUndeclaredDeltaProperty_WhenEnsuringTable_ThenUndeclaredPropertyIsRaisedWithoutItsName(
        string name,
        string value)
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(configuration: SyntheticSilverTable.TableConfigurationWith(name, value)));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.UndeclaredProperty);
        exception.PropertyName.ShouldBeNull();
        exception.Message.ShouldNotContain(name, Case.Insensitive);
        exception.Message.ShouldNotContain(value);
    }

    [Theory]
    [InlineData("delta.checkpointInterval", "5")]
    [InlineData("delta.checkpoint.writeStatsAsJson", "false")]
    [InlineData("delta.checkpoint.writeStatsAsStruct", "true")]
    [InlineData("delta.dataSkippingNumIndexedCols", "8")]
    [InlineData("delta.dataSkippingStatsColumns", "id")]
    [InlineData("delta.deletedFileRetentionDuration", "interval 1 days")]
    [InlineData("delta.enableExpiredLogCleanup", "false")]
    [InlineData("delta.setTransactionRetentionDuration", "interval 7 days")]
    [InlineData("delta.targetFileSize", "134217728")]
    [InlineData("owner", "data platform team")]
    public async Task GivenTableWithAWriterNeutralOrNonDeltaProperty_WhenEnsuringTable_ThenItIsValidated(string name, string value)
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(configuration: SyntheticSilverTable.TableConfigurationWith(name, value)));

        var outcome = await EnsureAsync();

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
    }

    [Fact]
    public async Task GivenContractWithoutLogRetentionAndTableWithIt_WhenEnsuringTable_ThenItIsValidatedAsWriterNeutral()
    {
        var templateConfiguration = SyntheticSilverTable.TemplateConfiguration();
        templateConfiguration.Remove(SyntheticSilverTable.LogRetention);
        var contract = await SyntheticSilverTable.LoadContractAsync(templateConfiguration);
        SyntheticSilverTable.WriteExistingTable(TablePath);

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(TablePath, contract, CancellationToken.None);

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
    }

    [Fact]
    public async Task GivenTableWhoseLaterCommitChangesTheSchema_WhenEnsuringTable_ThenTheLatestStateIsRejected()
    {
        SyntheticSilverTable.WriteExistingTable(TablePath);
        const string evolved = """{"type":"struct","fields":[{"name":"id","type":"string","nullable":false,"metadata":{}}]}""";
        SyntheticSilverTable.WriteCommit(TablePath, 1, SyntheticSilverTable.MetaDataLine(schemaString: evolved));

        var exception = await EnsureFailsAsync();

        exception.Aspect.ShouldBe(FabricSilverTableAspect.Schema);
    }

    [Fact]
    public async Task GivenTableCreatedWithUppercaseChangeFeedAndLaterCorrected_WhenEnsuringTable_ThenTheLatestStateIsValidated()
    {
        SyntheticSilverTable.WriteExistingTable(
            TablePath,
            SyntheticSilverTable.MetaDataLine(configuration: SyntheticSilverTable.TemplateConfiguration()));
        SyntheticSilverTable.WriteCommit(TablePath, 1, SyntheticSilverTable.MetaDataLine());

        var outcome = await EnsureAsync();

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
    }

    [Fact]
    public async Task GivenTableWhoseLogStartsAtACheckpoint_WhenEnsuringTable_ThenTheCheckpointStateIsValidated()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        await FabricSilverDeltaTables.EnsureTableAsync(TablePath, contract, CancellationToken.None);
        using (var engine = new DeltaEngine(EngineOptions.Default))
        using (var table = await engine.LoadTableAsync(new TableOptions { TableLocation = TablePath }, CancellationToken.None))
        {
            await table.CheckpointAsync(CancellationToken.None);
        }

        File.Delete(Path.Combine(TablePath, "_delta_log", "00000000000000000000.json"));

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(TablePath, contract, CancellationToken.None);

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
        Directory.GetFiles(Path.Combine(TablePath, "_delta_log"), "*.json").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("notes.txt", "not a commit")]
    [InlineData("00000000000000000000.json", "not json")]
    public async Task GivenLogDirectoryTheEngineCannotLoad_WhenEnsuringTable_ThenInvalidDataWrapsTheEngineErrorAndNothingIsCreated(
        string fileName,
        string content)
    {
        var logDirectory = Path.Combine(TablePath, "_delta_log");
        Directory.CreateDirectory(logDirectory);
        await File.WriteAllTextAsync(Path.Combine(logDirectory, fileName), content);

        var exception = await Should.ThrowAsync<InvalidDataException>(EnsureAsync);

        exception.InnerException.ShouldBeAssignableTo<DeltaLakeException>();
        Directory.GetFiles(logDirectory).Select(Path.GetFileName).ShouldBe([fileName]);
    }

    private async Task<FabricSilverTableOutcome> EnsureAsync() =>
        await FabricSilverDeltaTables.EnsureTableAsync(
            TablePath,
            await SyntheticSilverTable.LoadContractAsync(),
            CancellationToken.None);

    private Task<FabricSilverTableMismatchException> EnsureFailsAsync() =>
        Should.ThrowAsync<FabricSilverTableMismatchException>(EnsureAsync);
}
