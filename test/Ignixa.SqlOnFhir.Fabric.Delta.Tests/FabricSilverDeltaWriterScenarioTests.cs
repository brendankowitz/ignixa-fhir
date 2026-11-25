// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using static Ignixa.SqlOnFhir.Fabric.Delta.Tests.SyntheticPatients;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The ingestion scenarios a landing-zone pipeline produces, and the change data feed each one leaves, read back
/// through the Parquet and log oracles.
/// </summary>
public class FabricSilverDeltaWriterScenarioTests
{
    private const string Stored = "2024-01-01T00:00:00Z";

    [Fact]
    public async Task GivenNoTable_WhenTheFirstFileIsIngested_ThenTheTableHoldsOneRowPerResourceWithItsLineage()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("a", Stored, "Ash"), Patient("b", Stored, "Birch"))],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        outcome.TablePath.ShouldBe(Path.GetFullPath(table));
        (outcome.Outcome, outcome.CommittedVersion, outcome.InsertedRows, outcome.Attempts)
            .ShouldBe((FabricSilverTableOutcome.Created, (long?)1, 2L, 1));
        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        rows.Count.ShouldBe(2);
        foreach (var (id, family) in new[] { ("a", "Ash"), ("b", "Birch") })
        {
            var row = RowOf(rows, id);
            FamilyOf(row).ShouldBe(family);
            ((string?)row["resourceType"]).ShouldBe("Patient");
            ((string?)row["msftSourceSystem"]).ShouldBe(WriterContracts.Namespace);
            ((string?)row["msftFilePath"]).ShouldBe(WriterContracts.FirstFile);
            ((string?)row["msftCreatedDatetime"]).ShouldBe((string?)row["msftModifiedDatetime"]);
        }

        (await EngineTableReader.ReadIdsAsync(table)).Ids.ShouldBe(rows.Select(row => (string)row["id"]!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GivenIngestedFile_WhenTheSameFileIsReplayed_ThenNoVersionIsWrittenAndNoRowChanges()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        Func<ResourceJsonNode[]> resources = () => [Patient("a", Stored, "Ash"), Patient("b", Stored, "Birch")];
        var contract = await SeedAsync(table, resources());
        var before = await DeltaParquetReader.ReadLiveRowsAsync(table);

        var replay = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, resources())], WriterContracts.Ingestion(), CancellationToken.None);

        var outcome = replay.Tables.ShouldHaveSingleItem();
        (outcome.Outcome, outcome.CommittedVersion, outcome.UnchangedRows).ShouldBe((FabricSilverTableOutcome.Validated, (long?)null, 2L));
        DeltaLogReader.Read(table).Version.ShouldBe(1);
        (await DeltaParquetReader.ReadLiveRowsAsync(table)).Select(row => row.ToJsonString())
            .ShouldBe(before.Select(row => row.ToJsonString()));
    }

    [Fact]
    public async Task GivenIngestedFile_WhenAnUpdateArrivesFromADifferentFile_ThenTheRowsLineageMovesToThatFile()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("a", Stored, "Ash"), Patient("b", Stored, "Birch"));

        await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("b", "2024-02-01T00:00:00Z", "Beech"))],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            CancellationToken.None);

        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        ((string?)RowOf(rows, "a")["msftFilePath"]).ShouldBe(WriterContracts.FirstFile);
        ((string?)RowOf(rows, "b")["msftFilePath"]).ShouldBe(WriterContracts.SecondFile);
        FamilyOf(RowOf(rows, "b")).ShouldBe("Beech");
    }

    [Fact]
    public async Task GivenTwoSourceNamespaces_WhenBothSendTheSameLocalPatientId_ThenTheTableHoldsTwoRowsWithDistinctIds()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("shared", Stored, "First"));

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("shared", Stored, "Second"))],
            WriterContracts.Ingestion(sourceNamespace: WriterContracts.OtherNamespace),
            CancellationToken.None);

        result.Tables[0].InsertedRows.ShouldBe(1);
        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        rows.Count.ShouldBe(2);
        FamilyOf(RowOf(rows, "shared")).ShouldBe("First");
        var other = RowOf(rows, "shared", WriterContracts.OtherNamespace);
        FamilyOf(other).ShouldBe("Second");
        ((string?)other["msftSourceSystem"]).ShouldBe(WriterContracts.OtherNamespace);
        SilverId("Patient", "shared").ShouldNotBe(SilverId("Patient", "shared", WriterContracts.OtherNamespace));
    }

    [Fact]
    public async Task GivenNoTable_WhenRowsAreInserted_ThenTheChangeFeedRecordsAnInsertPerRow()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        await SeedAsync(table, Patient("a", Stored, "Ash"), Patient("b", Stored, "Birch"));

        var changes = await DeltaParquetReader.ReadChangesAsync(table, 1);

        changes.Select(change => (string?)change["_change_type"]).ShouldAllBe(type => type == "insert");
        changes.Select(FamilyOf).Order(StringComparer.Ordinal).ShouldBe(["Ash", "Birch"]);
        DeltaLogReader.Read(table).ChangeFiles.Where(change => change.Version == 1).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GivenTwoRowsInOneFile_WhenOneIsUpdated_ThenTheChangeFeedHoldsItsPreAndPostImageButNotTheCopiedRow()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("a", Stored, "Ash"), Patient("b", Stored, "Birch"));
        var seededFile = DeltaLogReader.Read(table).LiveFiles.Keys.ShouldHaveSingleItem();

        await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("b", "2024-02-01T00:00:00Z", "Beech"))],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            CancellationToken.None);

        // The update rewrote the file holding both rows, so 'a' was copied into the new file.
        var snapshot = DeltaLogReader.Read(table);
        snapshot.LiveFiles.Keys.ShouldNotContain(seededFile);
        var changes = await DeltaParquetReader.ReadChangesAsync(table, 2);
        changes.Count.ShouldBe(2);
        var preimage = changes.Single(change => (string?)change["_change_type"] == "update_preimage");
        var postimage = changes.Single(change => (string?)change["_change_type"] == "update_postimage");
        ((string?)preimage["id"]).ShouldBe(SilverId("Patient", "b"));
        ((string?)postimage["id"]).ShouldBe(SilverId("Patient", "b"));
        (FamilyOf(preimage), (string?)preimage["msftFilePath"]).ShouldBe(("Birch", WriterContracts.FirstFile));
        (FamilyOf(postimage), (string?)postimage["msftFilePath"]).ShouldBe(("Beech", WriterContracts.SecondFile));
        ((string?)postimage["meta_lastUpdated"]).ShouldBe("2024-02-01T00:00:00.000000Z");
    }
}
