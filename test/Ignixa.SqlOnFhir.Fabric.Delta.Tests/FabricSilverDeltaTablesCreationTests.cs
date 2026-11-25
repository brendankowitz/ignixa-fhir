// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using DeltaLake.Table;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

public sealed class FabricSilverDeltaTablesCreationTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task GivenNoTableDirectory_WhenEnsuringTable_ThenVersionZeroHoldsTheContractWithLowercaseChangeFeed()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("silver", "Patient");
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        outcome.ShouldBe(FabricSilverTableOutcome.Created);
        var snapshot = DeltaLogReader.Read(tablePath);
        snapshot.Version.ShouldBe(0);
        snapshot.Protocol.GetRawText().ShouldBe("""{"minReaderVersion":1,"minWriterVersion":4}""");
        snapshot.MetaDataKeys.ShouldBe(["id", "format", "schemaString", "partitionColumns", "configuration", "createdTime"]);
        Guid.TryParseExact(snapshot.MetaData.GetProperty("id").GetString(), "D", out _).ShouldBeTrue();
        snapshot.MetaData.GetProperty("format").GetRawText().ShouldBe("""{"provider":"parquet","options":{}}""");
        snapshot.SchemaString.ShouldBe(contract.SchemaString);
        snapshot.MetaData.GetProperty("partitionColumns").GetRawText().ShouldBe("[]");
        snapshot.Configuration.ShouldBe(
        [
            KeyValuePair.Create(SyntheticSilverTable.ChangeDataFeed, "true"),
            KeyValuePair.Create(SyntheticSilverTable.VOrder, "true"),
            KeyValuePair.Create(SyntheticSilverTable.LogRetention, "interval 30 days"),
        ]);
        snapshot.MetaData.GetProperty("createdTime").GetInt64().ShouldBeInRange(before, after);
        snapshot.LiveFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenNoTable_WhenEnsuringTable_ThenTheCommitIsCommitInfoProtocolMetaDataOnLfTerminatedLinesWithoutBom()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");

        await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(tablePath, "_delta_log", "00000000000000000000.json"));
        bytes.Take(3).ShouldNotBe(new byte[] { 0xEF, 0xBB, 0xBF });
        bytes.ShouldNotContain((byte)'\r');
        bytes[^1].ShouldBe((byte)'\n');
        var lines = DeltaLogReader.ReadCommitLines(tablePath, 0);
        lines.Select(line => JsonDocument.Parse(line).RootElement.EnumerateObject().Single().Name)
            .ShouldBe(["commitInfo", "protocol", "metaData"]);
        using var commitInfo = JsonDocument.Parse(lines[0]);
        var info = commitInfo.RootElement.GetProperty("commitInfo");
        info.EnumerateObject().Select(property => property.Name).ShouldBe(
            ["timestamp", "operation", "operationParameters", "isolationLevel", "isBlindAppend", "engineInfo", "txnId"]);
        info.GetProperty("operation").GetString().ShouldBe("CREATE TABLE");
        info.GetProperty("operationParameters").GetRawText().ShouldBe("""{"mode":"ErrorIfExists","partitionBy":"[]"}""");
        info.GetProperty("isolationLevel").GetString().ShouldBe("Serializable");
        info.GetProperty("isBlindAppend").GetBoolean().ShouldBeTrue();
        info.GetProperty("engineInfo").GetString().ShouldBe("Ignixa.SqlOnFhir.Fabric.Delta");
        Guid.TryParseExact(info.GetProperty("txnId").GetString(), "D", out _).ShouldBeTrue();
        info.GetProperty("timestamp").GetInt64()
            .ShouldBe(DeltaLogReader.Read(tablePath).MetaData.GetProperty("createdTime").GetInt64());
    }

    [Fact]
    public async Task GivenCreatedTable_WhenLoadedByTheDeltaEngine_ThenItIsVersionZeroWithTheContractSchemaAndChangeFeed()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");
        await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        using var engine = new DeltaEngine(EngineOptions.Default);
        using var table = await engine.LoadTableAsync(new TableOptions { TableLocation = tablePath }, CancellationToken.None);

        table.Version().ShouldBe(0UL);
        var protocol = table.ProtocolVersions();
        (protocol.MinimumReaderVersion, protocol.MinimumWriterVersion).ShouldBe((1, 4));
        var metadata = table.Metadata();
        JsonNode.DeepEquals(JsonNode.Parse(metadata.SchemaString), JsonNode.Parse(contract.SchemaString)).ShouldBeTrue();
        metadata.PartitionColumns.ShouldBeEmpty();
        metadata.Configuration[SyntheticSilverTable.ChangeDataFeed].ShouldBe("true");
        table.Schema().FieldsList.Select(field => field.Name).ShouldBe(["id", "meta", "scores", "born"]);
    }

    [Fact]
    public async Task GivenCreatedTable_WhenEnsuredAgain_ThenItIsValidatedAndUnchanged()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");
        await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);
        var created = await File.ReadAllBytesAsync(Path.Combine(tablePath, "_delta_log", "00000000000000000000.json"));

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        outcome.ShouldBe(FabricSilverTableOutcome.Validated);
        (await File.ReadAllBytesAsync(Path.Combine(tablePath, "_delta_log", "00000000000000000000.json"))).ShouldBe(created);
        Directory.GetFiles(Path.Combine(tablePath, "_delta_log")).Length.ShouldBe(1);
    }

    [Theory]
    [InlineData("empty table directory")]
    [InlineData("empty log directory")]
    [InlineData("log directory with only temp files")]
    public async Task GivenDirectoryWithoutALog_WhenEnsuringTable_ThenTheTableIsCreated(string state)
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");
        var logPath = Path.Combine(tablePath, "_delta_log");
        Directory.CreateDirectory(state == "empty table directory" ? tablePath : logPath);
        var litter = Path.Combine(logPath, ".00000000000000000000.json.0123456789abcdef.tmp");
        if (state == "log directory with only temp files")
        {
            await File.WriteAllTextAsync(litter, "partial");
        }

        var outcome = await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);

        outcome.ShouldBe(FabricSilverTableOutcome.Created);
        DeltaLogReader.Read(tablePath).Version.ShouldBe(0);
        File.Exists(litter).ShouldBe(state == "log directory with only temp files");
    }

    [Theory]
    [InlineData("part-00000.parquet", false)]
    [InlineData("_change_data", true)]
    public async Task GivenFilesButNoLog_WhenEnsuringTable_ThenUnrecognizedContentIsRejectedAndNothingIsCreated(
        string entry,
        bool isDirectory)
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");
        Directory.CreateDirectory(tablePath);
        if (isDirectory)
        {
            Directory.CreateDirectory(Path.Combine(tablePath, entry));
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(tablePath, entry), "data");
        }

        var exception = await Should.ThrowAsync<FabricSilverTableMismatchException>(
            () => FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None));

        exception.Aspect.ShouldBe(FabricSilverTableAspect.UnrecognizedContent);
        exception.PropertyName.ShouldBeNull();
        Directory.Exists(Path.Combine(tablePath, "_delta_log")).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenCanceledToken_WhenEnsuringTable_ThenItThrowsAndCreatesNothing()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");

        await Should.ThrowAsync<OperationCanceledException>(
            () => FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, new CancellationToken(canceled: true)));

        Directory.Exists(tablePath).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenConcurrentCreators_WhenEnsuringTheSameTable_ThenExactlyOneCreatesAndTheOthersValidate()
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();
        var tablePath = _directory.Combine("Patient");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creators = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);
        })).ToList();

        start.SetResult();
        var outcomes = await Task.WhenAll(creators);

        outcomes.Count(outcome => outcome == FabricSilverTableOutcome.Created).ShouldBe(1);
        outcomes.Count(outcome => outcome == FabricSilverTableOutcome.Validated).ShouldBe(7);
        Directory.GetFileSystemEntries(Path.Combine(tablePath, "_delta_log"))
            .Select(Path.GetFileName)
            .ShouldBe(["00000000000000000000.json"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenBlankTablePath_WhenEnsuringTable_ThenArgumentExceptionIsThrown(string tablePath)
    {
        var contract = await SyntheticSilverTable.LoadContractAsync();

        var exception = await Should.ThrowAsync<ArgumentException>(
            () => FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None));

        exception.ParamName.ShouldBe("tablePath");
    }

    [Fact]
    public async Task GivenNullContract_WhenEnsuringTable_ThenArgumentNullExceptionIsThrown()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => FabricSilverDeltaTables.EnsureTableAsync(_directory.Combine("Patient"), null!, CancellationToken.None));
    }
}
