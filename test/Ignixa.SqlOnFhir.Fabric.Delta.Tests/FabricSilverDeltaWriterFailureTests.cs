// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// What a failed or contended write leaves behind, read with the independent log and Parquet readers and the engine.
/// </summary>
public class FabricSilverDeltaWriterFailureTests
{
    private const string InvalidDate = "1500-02-29T10:00:00Z";

    [Fact]
    public async Task GivenAnInvalidNestedValueInTheSecondTablesBatch_WhenWritten_ThenNoTableChangesAndTheErrorNamesThePathButNotTheValue()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await SyntheticPatients.SeedAsync(second, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var firstFiles = DataFiles(first);
        var secondFiles = DataFiles(second);

        var exception = await Should.ThrowAsync<FabricSilverResourceRejectedException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(first, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                WriterContracts.Batch(
                    second,
                    contract,
                    SyntheticPatients.Patient("b", "2024-02-01T00:00:00Z"),
                    SyntheticPatients.Patient("bad", "2024-02-01T00:00:00Z", customize: resource => resource["seenAt"] = new System.Text.Json.Nodes.JsonArray(InvalidDate))),
            ],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile),
            CancellationToken.None));

        exception.TablePath.ShouldBe(second);
        exception.Position.ShouldBe(1);
        exception.SourceId.ShouldBe("bad");
        exception.FieldPath.ShouldBe("seenAt[0]");
        exception.Message.ShouldContain(second);
        exception.Message.ShouldNotContain("1500");
        DeltaLogReader.Read(first).Version.ShouldBe(1);
        DeltaLogReader.Read(second).Version.ShouldBe(1);
        DataFiles(first).ShouldBe(firstFiles);
        DataFiles(second).ShouldBe(secondFiles);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(first), "a")).ShouldBe("Doe");
    }

    // A source id is reported only when it is a well-formed FHIR id; anything else may be a field value in disguise.
    [Theory]
    [InlineData("has space")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task GivenARejectedResourceWithAMalformedSourceId_WhenWritten_ThenTheErrorOmitsTheId(string sourceId)
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();

        var exception = await Should.ThrowAsync<FabricSilverResourceRejectedException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(root.Combine("Patient"), contract, SyntheticPatients.Patient(sourceId, "2024-01-01T00:00:00Z", customize: resource => resource["active"] = "yes"))],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.SourceId.ShouldBeNull();
        exception.FieldPath.ShouldBe("active");
        exception.Message.ShouldNotContain(sourceId);
    }

    [SkippableFact]
    public async Task GivenTheSecondTablesCommitFails_WhenWritten_ThenTheErrorListsTheFirstTablesVersionAndAReplayRepairsTheSecond()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await SyntheticPatients.SeedAsync(second, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        FabricSilverTableBatch[] batches =
        [
            WriterContracts.Batch(first, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
            WriterContracts.Batch(second, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
        ];
        var ingestion = WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion);

        using (var injection = new UnwritableDirectory(LogDirectory(second)))
        {
            Skip.IfNot(injection.IsEffective, "This account can create files in a directory it denied itself, so a commit cannot be made to fail.");

            var exception = await Should.ThrowAsync<FabricSilverWriteException>(
                () => FabricSilverDeltaWriter.WriteBatchAsync(batches, ingestion, CancellationToken.None));

            exception.TablePath.ShouldBe(second);
            exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
            exception.MayHaveCommitted.ShouldBeFalse();
            exception.Message.ShouldContain(second);
            exception.Attempts.ShouldBe(1);
            exception.InnerException.ShouldNotBeNull();
            var completed = exception.CompletedTables.ShouldHaveSingleItem();
            completed.TablePath.ShouldBe(first);
            completed.CommittedVersion.ShouldBe(2);
            completed.UpdatedRows.ShouldBe(1);
        }

        DeltaLogReader.Read(second).Version.ShouldBe(1);
        var firstFilesAfterFailure = DeltaLogReader.Read(first).LiveFiles.Keys.Order(StringComparer.Ordinal).ToList();

        var replay = await FabricSilverDeltaWriter.WriteBatchAsync(batches, ingestion, CancellationToken.None);

        replay.Tables[0].CommittedVersion.ShouldBeNull();
        replay.Tables[0].UnchangedRows.ShouldBe(1);
        replay.Tables[1].CommittedVersion.ShouldBe(2);
        replay.Tables[1].UpdatedRows.ShouldBe(1);
        DeltaLogReader.Read(first).Version.ShouldBe(2);
        DeltaLogReader.Read(first).LiveFiles.Keys.Order(StringComparer.Ordinal).ShouldBe(firstFilesAfterFailure);
        foreach (var table in new[] { first, second })
        {
            SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a")).ShouldBe("Changed");
        }
    }

    [SkippableFact]
    public async Task GivenACommitThatCannotBeWritten_WhenMerged_ThenTheTableKeepsItsVersionAndTheNextWriteSucceedsBesideTheOrphans()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var seededFiles = DataFiles(table);
        FabricSilverTableBatch[] batches = [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))];

        using (var injection = new UnwritableDirectory(LogDirectory(table)))
        {
            Skip.IfNot(injection.IsEffective, "This account can create files in a directory it denied itself, so a commit cannot be made to fail.");

            var exception = await Should.ThrowAsync<FabricSilverWriteException>(
                () => FabricSilverDeltaWriter.WriteBatchAsync(batches, WriterContracts.Ingestion(), CancellationToken.None));

            exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
            exception.MayHaveCommitted.ShouldBeFalse();
            exception.CompletedTables.ShouldBeEmpty();
        }

        // The merge wrote its data files before the commit failed; nothing references them.
        var orphans = DataFiles(table).Except(seededFiles).ToList();
        orphans.ShouldNotBeEmpty();
        DeltaLogReader.Read(table).Version.ShouldBe(1);
        var engineRead = await EngineTableReader.ReadIdsAsync(table);
        engineRead.Version.ShouldBe(1);
        engineRead.Ids.ShouldBe([SyntheticPatients.SilverId("Patient", "a")]);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a")).ShouldBe("Doe");

        var retried = await FabricSilverDeltaWriter.WriteBatchAsync(batches, WriterContracts.Ingestion(), CancellationToken.None);

        retried.Tables[0].CommittedVersion.ShouldBe(2);
        orphans.ShouldBeSubsetOf(DataFiles(table));
        DeltaLogReader.Read(table).LiveFiles.Keys.ShouldNotContain(live => orphans.Contains(live));
        (await EngineTableReader.ReadIdsAsync(table)).Version.ShouldBe(2);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a")).ShouldBe("Changed");
    }

    /// <summary>
    /// The binding reports a canceled token as a cancellation even when the native merge committed, so a merge the
    /// engine has started must run to the end and be reported. The cancellation is requested through the internal seam
    /// at the last moment before the engine merge starts, which is as close to "in flight" as the binding allows.
    /// </summary>
    [Fact]
    public async Task GivenCancellationOnceTheFirstTablesMergeHasStarted_WhenWritten_ThenThatMergeCommitsAndIsReportedAndTheSecondIsUntouched()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await SyntheticPatients.SeedAsync(second, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        using var cancellation = new CancellationTokenSource();
        var options = new FabricSilverWriterOptions
        {
            BeforeEngineMerge = table =>
            {
                if (table == first)
                {
                    cancellation.Cancel();
                }
            },
        };

        // A plain await, as callers write it: an async method faulting with an OperationCanceledException ends Canceled,
        // and await rethrows the stored exception, where Shouldly's continuation would only see TaskCanceledException.
        FabricSilverWriteCanceledException? exception = null;
        try
        {
            await FabricSilverDeltaWriter.WriteBatchAsync(
                [
                    WriterContracts.Batch(first, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                    WriterContracts.Batch(second, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                ],
                WriterContracts.Ingestion(filePath: WriterContracts.SecondFile),
                options,
                cancellation.Token);
        }
        catch (FabricSilverWriteCanceledException caught)
        {
            exception = caught;
        }

        exception.ShouldNotBeNull();
        exception.ShouldBeAssignableTo<OperationCanceledException>();
        exception.CancellationToken.ShouldBe(cancellation.Token);
        var committed = exception.CompletedTables.ShouldHaveSingleItem();
        committed.TablePath.ShouldBe(first);
        committed.CommittedVersion.ShouldBe(2);
        committed.UpdatedRows.ShouldBe(1);
        DeltaLogReader.Read(first).Version.ShouldBe(2);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(first), "a")).ShouldBe("Changed");
        DeltaLogReader.Read(second).Version.ShouldBe(1);
    }

    /// <summary>
    /// An engine failure after the commit (the binding's post-commit checkpoint or log cleanup) cannot be provoked on
    /// demand, so the internal seam throws as the engine merge returns; one thrown just before the engine starts is the
    /// counterpart that provably committed nothing. A replay is safe either way.
    /// </summary>
    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 2, true)]
    public async Task GivenAnEngineFailureBeforeOrAfterTheCommit_WhenWritten_ThenMayHaveCommittedFollowsTheTableVersionAndAReplayIsSafe(
        bool afterCommit,
        long versionAfterFailure,
        bool replayFindsTheRowWritten)
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        FabricSilverTableBatch[] batches = [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))];
        Action<string> fail = _ => throw new IOException("Injected engine failure.");
        var options = afterCommit ? new FabricSilverWriterOptions { AfterEngineMerge = fail } : new FabricSilverWriterOptions { BeforeEngineMerge = fail };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(
            () => FabricSilverDeltaWriter.WriteBatchAsync(batches, WriterContracts.Ingestion(), options, CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
        exception.MayHaveCommitted.ShouldBe(afterCommit);
        exception.Attempts.ShouldBe(1);
        exception.CompletedTables.ShouldBeEmpty();
        exception.InnerException.ShouldBeOfType<IOException>();
        exception.VersionCheckException.ShouldBeNull();
        exception.Message.ShouldContain("may or may not be committed");
        DeltaLogReader.Read(table).Version.ShouldBe(versionAfterFailure);

        var replay = await FabricSilverDeltaWriter.WriteBatchAsync(batches, WriterContracts.Ingestion(), CancellationToken.None);

        replay.Tables[0].CommittedVersion.ShouldBe(replayFindsTheRowWritten ? null : 2);
        replay.Tables[0].UnchangedRows.ShouldBe(replayFindsTheRowWritten ? 1 : 0);
        DeltaLogReader.Read(table).Version.ShouldBe(2);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a")).ShouldBe("Changed");
    }

    /// <summary>
    /// The seam fails the merge after its commit and first leaves a malformed next commit in the log, so the engine
    /// cannot load the table to read its version again. The table is then reported as possibly committed, and the
    /// load failure is kept beside the merge's own failure rather than dropped.
    /// </summary>
    [Fact]
    public async Task GivenAFailureAfterTheCommitAndATableThatCannotBeReloaded_WhenWritten_ThenMayHaveCommittedIsSetAndTheReloadFailureIsReported()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var options = new FabricSilverWriterOptions
        {
            AfterEngineMerge = path =>
            {
                File.WriteAllText(Path.Combine(LogDirectory(path), "00000000000000000003.json"), "not json");
                throw new IOException("Injected engine failure.");
            },
        };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
        exception.MayHaveCommitted.ShouldBeTrue();
        exception.InnerException.ShouldBeOfType<IOException>();
        exception.VersionCheckException.ShouldNotBeNull();
        exception.VersionCheckException.ShouldNotBeSameAs(exception.InnerException);
    }

    /// <summary>
    /// Two writers, each with its own engine and merge gate, update different rows of the same data file at the same
    /// moment, so their merges remove the same file and the later commit conflicts. The writer offers no hook between
    /// loading a snapshot and committing, so the overlap cannot be forced; ten simultaneous rounds make a conflict all
    /// but certain, and the assertions hold whether or not each round collided.
    /// </summary>
    /// <remarks>
    /// On a Windows local filesystem the engine occasionally fails a racing merge with a non-conflict I/O error while
    /// opening the other writer's commit file. The writer fails closed on it, so the writer replays its own batch
    /// once, as a caller would; newer-wins makes the replay commit nothing when the failed attempt had committed.
    /// </remarks>
    [Fact]
    public async Task GivenTwoWritersRacingOnOneFile_WhenEachUpdatesItsOwnRow_ThenNoUpdateIsLostAndConflictsAreRetried()
    {
        const int Rounds = 10;
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(
            table,
            SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"),
            SyntheticPatients.Patient("b", "2024-01-01T00:00:00Z"));
        using var firstGate = new SemaphoreSlim(1, 1);
        using var secondGate = new SemaphoreSlim(1, 1);
        var attempts = new List<int>();
        var replays = 0;

        for (var round = 1; round <= Rounds; round++)
        {
            var lastUpdated = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(round).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var outcomes = await Task.WhenAll(
                Task.Run(() => WriteOrReplayAsync(table, contract, firstGate, SyntheticPatients.Patient("a", lastUpdated, $"A{round}"))),
                Task.Run(() => WriteOrReplayAsync(table, contract, secondGate, SyntheticPatients.Patient("b", lastUpdated, $"B{round}"))));

            foreach (var (result, replayed) in outcomes)
            {
                if (replayed)
                {
                    // The failed attempt may have committed the row, so the replay's update count is not fixed.
                    replays++;
                    continue;
                }

                result.Tables[0].UpdatedRows.ShouldBe(1);
                attempts.Add(result.Tables[0].Attempts);
            }
        }

        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        rows.Count.ShouldBe(2);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(rows, "a")).ShouldBe($"A{Rounds}");
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(rows, "b")).ShouldBe($"B{Rounds}");
        DeltaLogReader.Read(table).Version.ShouldBe(1 + (2 * Rounds));

        // A non-conflict failure ends a race without a retry, so the retry evidence is only required of a run in which
        // no writer failed.
        if (replays == 0)
        {
            attempts.ShouldContain(attempt => attempt > 1, "No round collided, so no retry was exercised.");
        }
    }

    private static async Task<(FabricSilverBatchResult Result, bool Replayed)> WriteOrReplayAsync(
        string table,
        FabricSilverContract contract,
        SemaphoreSlim gate,
        ResourceJsonNode resource)
    {
        try
        {
            return (await WriteAsync(table, contract, gate, resource), false);
        }
        catch (FabricSilverWriteException exception) when (exception.Phase == FabricSilverWritePhase.Merge)
        {
            return (await WriteAsync(table, contract, gate, resource), true);
        }
    }

    private static Task<FabricSilverBatchResult> WriteAsync(string table, FabricSilverContract contract, SemaphoreSlim gate, ResourceJsonNode resource) =>
        FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, resource)],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MergeGate = gate },
            CancellationToken.None);

    private static string LogDirectory(string table) => Path.Combine(table, "_delta_log");

    // Every Parquet file under the table, data and change files alike, relative to the table.
    private static List<string> DataFiles(string table) =>
        [.. Directory.EnumerateFiles(table, "*.parquet", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(table, file).Replace('\\', '/'))
            .Where(file => !file.StartsWith("_delta_log/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
}
