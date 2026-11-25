// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeltaLake.Table;
using Parquet.Serialization;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The live data file count that the <see cref="FabricSilverWriterOptions.MaxTargetFiles"/> ceiling reads, checked
/// against logs the writer and the engine produced and against hand-built logs for the checkpoint rules.
/// </summary>
public class LiveDataFilesTests
{
    [Fact]
    public async Task GivenATableWrittenSeveralTimes_WhenCounted_ThenEachVersionMatchesTheIndependentLogReader()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var afterSeed = DeltaLogReader.Read(table).LiveFiles.Count;

        await WriteAsync(table, contract, SyntheticPatients.Patient("b", "2024-01-01T00:00:00Z"));
        await WriteAsync(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z"));
        var latest = DeltaLogReader.Read(table);

        (await LiveDataFiles.CountAsync(table, 1, CancellationToken.None)).ShouldBe(afterSeed);
        (await LiveDataFiles.CountAsync(table, 3, CancellationToken.None)).ShouldBe(latest.LiveFiles.Count);
        latest.LiveFiles.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAnEngineCheckpointAndCleanedUpCommits_WhenCounted_ThenTheCheckpointSuppliesTheFiles()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await WriteAsync(table, contract, SyntheticPatients.Patient("b", "2024-01-01T00:00:00Z"));
        await WriteAsync(table, contract, SyntheticPatients.Patient("c", "2024-01-01T00:00:00Z"));
        var expected = DeltaLogReader.Read(table).LiveFiles.Count;
        expected.ShouldBe(3);

        using (var engine = new DeltaEngine(EngineOptions.Default))
        using (var loaded = await engine.LoadTableAsync(new TableOptions { TableLocation = table }, CancellationToken.None))
        {
            await loaded.CheckpointAsync(CancellationToken.None);
        }

        File.Exists(LogEntry(table, "00000000000000000003.checkpoint.parquet")).ShouldBeTrue();

        // Log cleanup may delete every commit a checkpoint covers; the count must not need them.
        for (var version = 0; version <= 3; version++)
        {
            File.Delete(LogEntry(table, CommitName(version)));
        }

        (await LiveDataFiles.CountAsync(table, 3, CancellationToken.None)).ShouldBe(expected);
    }

    [Fact]
    public async Task GivenAnEngineCheckpoint_WhenTheWriterMergesAgain_ThenTheCountFollowsTheLaterCommits()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await WriteAsync(table, contract, SyntheticPatients.Patient("b", "2024-01-01T00:00:00Z"));
        using (var engine = new DeltaEngine(EngineOptions.Default))
        using (var loaded = await engine.LoadTableAsync(new TableOptions { TableLocation = table }, CancellationToken.None))
        {
            await loaded.CheckpointAsync(CancellationToken.None);
        }

        await WriteAsync(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z"));
        await WriteAsync(table, contract, SyntheticPatients.Patient("c", "2024-01-01T00:00:00Z"));

        // Version 2 had a and b in one file each; version 3 rewrote a's file and version 4 added c's.
        (await LiveDataFiles.CountAsync(table, 4, CancellationToken.None)).ShouldBe(3);
        (await EngineTableReader.ReadIdsAsync(table)).Ids.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GivenSeveralCheckpoints_WhenCounted_ThenTheNewestCompleteOneAtOrBelowTheVersionIsReplayedFrom()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;

        // An older single-part checkpoint that a correct replay never reads.
        await WriteCheckpointAsync(Path.Combine(log, "00000000000000000001.checkpoint.parquet"), "stale-1.parquet");

        // The newest complete checkpoint, in two parts, with a row whose add action is null.
        await WriteCheckpointAsync(Path.Combine(log, "00000000000000000002.checkpoint.0000000001.0000000002.parquet"), "a.parquet", null);
        await WriteCheckpointAsync(Path.Combine(log, "00000000000000000002.checkpoint.0000000002.0000000002.parquet"), "b.parquet", "c.parquet");

        // A newer multi-part checkpoint still being written, and one above the counted version; neither is a valid parquet file.
        await File.WriteAllTextAsync(Path.Combine(log, "00000000000000000003.checkpoint.0000000001.0000000002.parquet"), "partial");
        await File.WriteAllTextAsync(Path.Combine(log, "00000000000000000005.checkpoint.parquet"), "future");
        await File.WriteAllTextAsync(Path.Combine(log, "00000000000000000003.checkpoint.unknown.parquet"), "other form");

        await WriteCommitAsync(log, 3, Add("d.parquet"), string.Empty, Remove("b.parquet"), """{"commitInfo":{}}""");
        await WriteCommitAsync(log, 4, Add("e.parquet"));

        (await LiveDataFiles.CountAsync(table, 3, CancellationToken.None)).ShouldBe(3);
        (await LiveDataFiles.CountAsync(table, 4, CancellationToken.None)).ShouldBe(4);
        (await LiveDataFiles.CountAsync(table, 2, CancellationToken.None)).ShouldBe(3);
    }

    [Fact]
    public async Task GivenNoCheckpoint_WhenCounted_ThenEveryCommitFromZeroIsReplayed()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await WriteCommitAsync(log, 0, """{"protocol":{"minReaderVersion":1,"minWriterVersion":4}}""", Add("a.parquet"));
        await WriteCommitAsync(log, 1, Add("b.parquet"), Remove("a.parquet"), Add("b.parquet"));

        (await LiveDataFiles.CountAsync(table, 0, CancellationToken.None)).ShouldBe(1);
        (await LiveDataFiles.CountAsync(table, 1, CancellationToken.None)).ShouldBe(1);
    }

    [Theory]
    [InlineData("[1]", typeof(InvalidDataException))]
    [InlineData("""{"add":{"size":1}}""", typeof(InvalidDataException))]
    [InlineData("""{"remove":{"path":""}}""", typeof(InvalidDataException))]
    [InlineData("""{"add":"a.parquet"}""", typeof(InvalidDataException))]
    [InlineData("""{"add":{"path":7}}""", typeof(InvalidDataException))]
    [InlineData("""{"add":{"path":"b.parquet"},"remove":{"path":"a.parquet"}}""", typeof(InvalidDataException))]
    [InlineData("{\"add\":", typeof(JsonException))]
    public async Task GivenAMalformedCommitLine_WhenCounted_ThenItFails(string line, Type expected)
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await WriteCommitAsync(log, 0, Add("a.parquet"), line);

        var exception = await Should.ThrowAsync<Exception>(() => LiveDataFiles.CountAsync(table, 0, CancellationToken.None));

        exception.ShouldBeAssignableTo(expected);
    }

    [Fact]
    public async Task GivenACommitMissingFromTheReplayedRange_WhenCounted_ThenItFails()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await WriteCommitAsync(log, 0, Add("a.parquet"));
        await WriteCommitAsync(log, 2, Add("c.parquet"));

        await Should.ThrowAsync<FileNotFoundException>(() => LiveDataFiles.CountAsync(table, 2, CancellationToken.None));
    }

    [Fact]
    public async Task GivenACheckpointWithoutAnAddColumn_WhenCounted_ThenItFails()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await using (var stream = File.Create(Path.Combine(log, "00000000000000000000.checkpoint.parquet")))
        {
            await ParquetSerializer.SerializeAsync(new[] { new NoAddRow { Txn = "x" } }, stream);
        }

        await Should.ThrowAsync<InvalidDataException>(() => LiveDataFiles.CountAsync(table, 0, CancellationToken.None));
    }

    [Fact]
    public async Task GivenACheckpointWhosePathIsNotAString_WhenCounted_ThenItFailsRatherThanCountingNoFiles()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await using (var stream = File.Create(Path.Combine(log, "00000000000000000000.checkpoint.parquet")))
        {
            await ParquetSerializer.SerializeAsync(new[] { new NumericPathRow { Add = new NumericPathAdd { Path = 7 } } }, stream);
        }

        await Should.ThrowAsync<InvalidDataException>(() => LiveDataFiles.CountAsync(table, 0, CancellationToken.None));
    }

    [Fact]
    public async Task GivenNoLoadedVersion_WhenTheFileCeilingCounts_ThenItFailsClosed()
    {
        using var root = new TemporaryDirectory();

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(
            () => TargetTableSize.CountLiveFilesAsync(root.Path, loadedVersion: null, limit: 100, CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.TargetFilesUnknown);
        exception.LimitValue.ShouldBe(100);
        exception.Actual.ShouldBeNull();
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task GivenALogThatCannotBeReplayed_WhenTheFileCeilingCounts_ThenItFailsClosedWithTheCause()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await WriteCommitAsync(log, 1, Add("a.parquet"));

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(
            () => TargetTableSize.CountLiveFilesAsync(table, loadedVersion: 1, limit: 7, CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.TargetFilesUnknown);
        exception.TablePath.ShouldBe(table);
        exception.LimitValue.ShouldBe(7);
        exception.InnerException.ShouldBeOfType<FileNotFoundException>();
    }

    [Fact]
    public async Task GivenACanceledToken_WhenTheFileCeilingCounts_ThenCancellationIsNotReportedAsAnUnknownCount()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Synthetic");
        var log = Directory.CreateDirectory(Path.Combine(table, "_delta_log")).FullName;
        await WriteCommitAsync(log, 0, Add("a.parquet"));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => TargetTableSize.CountLiveFilesAsync(table, loadedVersion: 0, limit: 7, canceled.Token));
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public void GivenALiveFileCount_WhenComparedWithTheLimit_ThenOnlyACountAboveItFails(int liveFiles, bool fails)
    {
        var check = () => TargetTableSize.RequireFiles(liveFiles, "Tables/Patient", 100);

        if (fails)
        {
            var exception = check.ShouldThrow<FabricSilverLimitExceededException>();
            exception.Limit.ShouldBe(FabricSilverLimit.TargetFiles);
            exception.Actual.ShouldBe(liveFiles);
            exception.Message.ShouldContain("Tables/Patient");
            exception.Message.ShouldContain("OPTIMIZE");
        }
        else
        {
            check.ShouldNotThrow();
        }
    }

    private static async Task WriteAsync(string table, FabricSilverContract contract, params Ignixa.Serialization.SourceNodes.ResourceJsonNode[] resources) =>
        await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, resources)], WriterContracts.Ingestion(), CancellationToken.None);

    private static string LogEntry(string table, string name) => Path.Combine(table, "_delta_log", name);

    private static string CommitName(long version) => version.ToString("D20", CultureInfo.InvariantCulture) + ".json";

    private static Task WriteCommitAsync(string log, long version, params string[] lines) =>
        File.WriteAllLinesAsync(Path.Combine(log, CommitName(version)), lines);

    private static string Add(string path) => $$$"""{"add":{"path":"{{{path}}}","size":1,"dataChange":true}}""";

    private static string Remove(string path) => $$$"""{"remove":{"path":"{{{path}}}","dataChange":true}}""";

    private static async Task WriteCheckpointAsync(string path, params string?[] addedPaths)
    {
        await using var stream = File.Create(path);
        await ParquetSerializer.SerializeAsync(
            addedPaths.Select(added => new CheckpointRow { Add = added is null ? null : new CheckpointAdd { Path = added } }),
            stream);
    }

    private sealed class CheckpointRow
    {
        [JsonPropertyName("add")]
        public CheckpointAdd? Add { get; set; }
    }

    private sealed class CheckpointAdd
    {
        [JsonPropertyName("path")]
        public string? Path { get; set; }
    }

    private sealed class NoAddRow
    {
        [JsonPropertyName("txn")]
        public string? Txn { get; set; }
    }

    private sealed class NumericPathRow
    {
        [JsonPropertyName("add")]
        public NumericPathAdd? Add { get; set; }
    }

    private sealed class NumericPathAdd
    {
        [JsonPropertyName("path")]
        public int? Path { get; set; }
    }
}
