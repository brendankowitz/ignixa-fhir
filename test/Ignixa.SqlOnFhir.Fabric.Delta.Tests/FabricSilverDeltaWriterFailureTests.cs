// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// What a failed or contended write leaves behind, read with the independent log and Parquet readers and the engine.
/// </summary>
public class FabricSilverDeltaWriterFailureTests
{
    private const string InvalidDate = "1500-02-29T10:00:00Z";

    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

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
                    SyntheticPatients.Patient("bad", "2024-02-01T00:00:00Z", customize: resource => resource["seenAt"] = new JsonArray(InvalidDate))),
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
    [InlineData("abc\n")]
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

    [Fact]
    public async Task GivenTextThatIsNotValidUnicodeInTheBatch_WhenWritten_ThenNoTableChangesAndTheErrorNamesThePathButNotTheValue()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var files = DataFiles(table);
        var invalid = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"bad","meta":{"lastUpdated":"2024-02-01T00:00:00Z"},"name":[{"family":"invented-secret-marker\ud800"}]}""");

        var exception = await Should.ThrowAsync<FabricSilverResourceRejectedException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"), invalid)],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.TablePath.ShouldBe(table);
        exception.Position.ShouldBe(1);
        exception.ResourceType.ShouldBe("Patient");
        exception.SourceId.ShouldBe("bad");
        exception.FieldPath.ShouldBe("name[0].family");
        exception.InnerException.ShouldBeOfType<FabricSilverProjectionException>();
        exception.Message.ShouldNotContain("invented-secret-marker");
        DeltaLogReader.Read(table).Version.ShouldBe(1);
        DataFiles(table).ShouldBe(files);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a")).ShouldBe("Doe");
    }

    [Fact]
    public async Task GivenInvalidTextUnderAPropertyNameTheContractLacks_WhenWritten_ThenTheErrorShowsItsPositionNotItsName()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var invalid = ResourceJsonNode.Parse(
            """{"resourceType":"Patient","id":"bad","meta":{"lastUpdated":"2024-02-01T00:00:00Z"},"invented-secret\nforged-entry":"\ud800"}""");

        var exception = await Should.ThrowAsync<FabricSilverResourceRejectedException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, invalid)],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.FieldPath.ShouldBe("<property 3>");
        exception.Message.ShouldNotContain("invented-secret");
        exception.Message.ShouldNotContain("\n");
        DeltaLogReader.Read(table).Version.ShouldBe(1);
    }

    // No id can be decoded: one is a lone surrogate, one sits beside a resource property name that is one, and one sits
    // beside a resource property name that is repeated.
    [Theory]
    [InlineData("""{"resourceType":"Patient","id":"\ud800","meta":{"lastUpdated":"2024-02-01T00:00:00Z"}}""", "id")]
    [InlineData("""{"resourceType":"Patient","id":"bad","\ud800":1,"meta":{"lastUpdated":"2024-02-01T00:00:00Z"}}""", "Patient")]
    [InlineData("""{"resourceType":"Patient","id":"bad","invented-secret":1,"invented-secret":2,"meta":{"lastUpdated":"2024-02-01T00:00:00Z"}}""", "Patient")]
    public async Task GivenAnIdThatCannotBeDecoded_WhenWritten_ThenRejectedWithoutASourceId(string json, string fieldPath)
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();
        var resource = new ResourceJsonNode(JsonNode.Parse(json)!.AsObject());

        var exception = await Should.ThrowAsync<FabricSilverResourceRejectedException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(root.Combine("Patient"), contract, resource)],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.SourceId.ShouldBeNull();
        exception.FieldPath.ShouldBe(fieldPath);
        exception.Message.ShouldNotContain("invented-secret");
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
    /// On a Windows local filesystem the engine occasionally fails a racing writer with a non-conflict I/O error while
    /// opening the other writer's commit file, whether loading the table, reading its size or merging. The writer
    /// fails closed on it in whichever phase it hit, so the writer replays its own batch once, as a caller would. Every
    /// phase's replay is idempotent: newer-wins makes the replay commit nothing when the failed attempt had committed.
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

    /// <summary>
    /// The seam rewrites the merge's commit info as the engine returns, so the committed version holds no MERGE result
    /// to read back. Moving the commit file away instead would hide the commit from the table's version too.
    /// </summary>
    [Fact]
    public async Task GivenACommitThatCannotBeReadBack_WhenWritten_ThenTheFailureIsReadCommitWithTheCommittedVersion()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var options = new FabricSilverWriterOptions
        {
            AfterEngineMerge = path => RewriteCommitOperation(Path.Combine(LogDirectory(path), "00000000000000000002.json"), "WRITE"),
        };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.ReadCommit);
        exception.MayHaveCommitted.ShouldBeTrue();
        exception.CommittedVersion.ShouldBe(2);
        exception.Attempts.ShouldBe(1);
        exception.CompletedTables.ShouldBeEmpty();
        exception.InnerException.ShouldBeOfType<InvalidDataException>();
    }

    [Fact]
    public async Task GivenAMergeFailureOutsideReadCommit_WhenWritten_ThenNoCommittedVersionIsReported()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var options = new FabricSilverWriterOptions { AfterEngineMerge = _ => throw new IOException("Injected engine failure.") };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
        exception.MayHaveCommitted.ShouldBeTrue();
        exception.CommittedVersion.ShouldBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenMergesThatLoseTheCommitRaceFewerTimesThanTheLimit_WhenWritten_ThenTheRetryCommitsAndCountsTheAttempts(int conflicts)
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var thrown = 0;
        var options = new FabricSilverWriterOptions
        {
            MaxMergeAttempts = 3,
            BeforeEngineMerge = _ =>
            {
                if (thrown++ < conflicts)
                {
                    throw CommitConflict();
                }
            },
        };

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.Attempts, outcome.CommittedVersion, outcome.UpdatedRows).ShouldBe((conflicts + 1, 2L, 1L));
        DeltaLogReader.Read(table).Version.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAMergeThatLosesEveryCommitRace_WhenWritten_ThenItFailsAfterTheLastAttemptAndCommittedNothing()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var tries = 0;
        var options = new FabricSilverWriterOptions
        {
            MaxMergeAttempts = 3,
            BeforeEngineMerge = _ =>
            {
                tries++;
                throw CommitConflict();
            },
        };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        tries.ShouldBe(3);
        exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
        exception.Attempts.ShouldBe(3);
        exception.MayHaveCommitted.ShouldBeFalse();
        exception.VersionCheckException.ShouldBeNull();
        exception.InnerException.ShouldBeOfType<DeltaLake.Errors.DeltaRuntimeException>();
        DeltaLogReader.Read(table).Version.ShouldBe(1);
    }

    /// <summary>
    /// The backoff waits on a clock that never fires, so the write can only end through the token canceled once that
    /// wait has started.
    /// </summary>
    [Fact]
    public async Task GivenCancellationDuringTheRetryBackoff_WhenWritten_ThenItIsAWriteCancellationAndTheTableIsUnchanged()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        using var cancellation = new CancellationTokenSource();
        var clock = new StoppedClock();
        var tries = 0;
        var options = new FabricSilverWriterOptions
        {
            BackoffClock = clock,
            BeforeEngineMerge = _ =>
            {
                tries++;
                throw CommitConflict();
            },
        };

        var write = FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed"))],
            WriterContracts.Ingestion(),
            options,
            cancellation.Token);
        await clock.TimerCreated.WaitAsync(TestTimeout);
        await cancellation.CancelAsync();

        // A canceled task loses its exception type through Should.ThrowAsync, so the await is caught directly.
        FabricSilverWriteCanceledException? exception = null;
        try
        {
            await write.WaitAsync(TestTimeout);
        }
        catch (FabricSilverWriteCanceledException caught)
        {
            exception = caught;
        }

        exception.ShouldNotBeNull();
        exception.CompletedTables.ShouldBeEmpty();
        tries.ShouldBe(1);
        DeltaLogReader.Read(table).Version.ShouldBe(1);
    }

    /// <summary>
    /// The first table's merge seam queues a waiter on the private merge gate, which takes the gate as the first table
    /// releases it and then cancels, so the second table's merge is canceled while it waits for the gate.
    /// </summary>
    [Fact]
    public async Task GivenCancellationWhileWaitingForTheMergeGate_WhenWritten_ThenItIsAWriteCancellationListingTheCompletedTable()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await SyntheticPatients.SeedAsync(second, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        using var cancellation = new CancellationTokenSource();
        using var gate = new SemaphoreSlim(1, 1);
        Task? held = null;
        var options = new FabricSilverWriterOptions
        {
            MergeGate = gate,
            AfterEngineMerge = table =>
            {
                if (table == first)
                {
                    held = gate.WaitAsync();
                    _ = held.ContinueWith(_ => cancellation.Cancel(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                }
            },
        };

        // The gate stays held until the write returns, so the bounded wait is what fails the test if the token is ignored.
        FabricSilverWriteCanceledException? exception = null;
        try
        {
            await FabricSilverDeltaWriter.WriteBatchAsync(
                [
                    WriterContracts.Batch(first, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                    WriterContracts.Batch(second, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                ],
                WriterContracts.Ingestion(),
                options,
                cancellation.Token).WaitAsync(TestTimeout);
        }
        catch (FabricSilverWriteCanceledException caught)
        {
            exception = caught;
        }
        finally
        {
            if (held is not null)
            {
                await held;
                gate.Release();
            }
        }

        exception.ShouldNotBeNull();
        exception.CompletedTables.ShouldHaveSingleItem().TablePath.ShouldBe(first);
        DeltaLogReader.Read(first).Version.ShouldBe(2);
        DeltaLogReader.Read(second).Version.ShouldBe(1);
    }

    /// <summary>
    /// The first table's merge seam commits a schema change to the second table after that table was validated, so
    /// the second table's merge attempt loads a table that no longer matches the contract.
    /// </summary>
    [Fact]
    public async Task GivenATableWhoseSchemaChangesAfterValidation_WhenItsMergeAttemptLoadsIt_ThenTheMergeFailsUnmergedWithTheMismatch()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        await SyntheticPatients.SeedAsync(second, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var options = new FabricSilverWriterOptions
        {
            AfterEngineMerge = table =>
            {
                if (table == first)
                {
                    CommitExtraColumn(second, version: 2);
                }
            },
        };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(first, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
                WriterContracts.Batch(second, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", "Changed")),
            ],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.TablePath.ShouldBe(second);
        exception.Phase.ShouldBe(FabricSilverWritePhase.Merge);
        exception.MayHaveCommitted.ShouldBeFalse();
        exception.Attempts.ShouldBe(1);
        exception.InnerException.ShouldBeOfType<FabricSilverTableMismatchException>().Aspect.ShouldBe(FabricSilverTableAspect.Schema);
        exception.CompletedTables.ShouldHaveSingleItem().TablePath.ShouldBe(first);
        DeltaLogReader.Read(second).Version.ShouldBe(2);
        SyntheticPatients.FamilyOf(SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(second), "a")).ShouldBe("Doe");
    }

    // The delta-rs conflict wording, including its "transactions" (sic).
    private static DeltaLake.Errors.DeltaRuntimeException CommitConflict() =>
        new("Commit failed: a concurrent transactions added new data", 1);

    // Appends a metaData commit whose schema adds a nullable column to the table's version 0 schema.
    private static void CommitExtraColumn(string table, long version)
    {
        var metaData = DeltaLogReader.ReadCommitLines(table, 0)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .Single(action => action.ContainsKey("metaData"))["metaData"]!.AsObject();
        var schema = JsonNode.Parse((string)metaData["schemaString"]!)!.AsObject();
        schema["fields"]!.AsArray().Add(WriterContracts.Field("extra", "string"));
        metaData["schemaString"] = schema.ToJsonString();
        var action = new JsonObject { ["metaData"] = metaData.DeepClone() };
        File.WriteAllText(
            Path.Combine(LogDirectory(table), version.ToString("D20", CultureInfo.InvariantCulture) + ".json"),
            action.ToJsonString() + "\n");
    }

    // Rewrites the commitInfo operation of a commit file, failing if the commit records no MERGE to rewrite.
    private static void RewriteCommitOperation(string commit, string operation)
    {
        var actions = File.ReadAllLines(commit).Where(line => line.Length > 0).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();
        var commitInfo = actions.Single(action => action.ContainsKey("commitInfo"))["commitInfo"]!.AsObject();
        ((string?)commitInfo["operation"]).ShouldBe("MERGE");
        commitInfo["operation"] = operation;
        File.WriteAllText(commit, string.Concat(actions.Select(action => action.ToJsonString() + "\n")));
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
        catch (FabricSilverWriteException)
        {
            return (await WriteAsync(table, contract, gate, resource), true);
        }
        catch (FabricSilverLimitExceededException exception) when (exception.Limit == FabricSilverLimit.TargetFilesUnknown)
        {
            // Counting the files replays the log, so the same I/O error there fails the file ceiling closed.
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
