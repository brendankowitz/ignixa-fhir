// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using static Ignixa.SqlOnFhir.Fabric.Delta.Tests.SyntheticPatients;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The healthcare data solutions upsert criteria, each read back through the Parquet and log oracles rather than
/// through the engine that wrote them.
/// </summary>
public class FabricSilverDeltaWriterMergeTests
{
    private const string FirstIngestedAt = "2024-05-06T07:08:09.000000Z";
    private const string SecondIngestedAt = "2024-06-07T08:09:10.000000Z";
    private const string Stored = "2024-01-01T00:00:00.000001Z";

    [Theory]
    [InlineData("2023-12-31T23:59:59.999999Z")]
    [InlineData(Stored)]
    public async Task GivenStoredRow_WhenOlderOrEqualLastUpdatedArrives_ThenNoVersionIsWrittenAndTheRowIsUnchanged(string incoming)
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("a", Stored, "Stored"));
        var before = await DeltaParquetReader.ReadLiveRowsAsync(table);

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("a", incoming, "Incoming"))],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        outcome.CommittedVersion.ShouldBeNull();
        outcome.Outcome.ShouldBe(FabricSilverTableOutcome.Validated);
        (outcome.InsertedRows, outcome.UpdatedRows, outcome.UnchangedRows, outcome.Attempts).ShouldBe((0L, 0L, 1L, 1));
        DeltaLogReader.Read(table).Version.ShouldBe(1);
        (await DeltaParquetReader.ReadLiveRowsAsync(table)).Select(row => row.ToJsonString())
            .ShouldBe(before.Select(row => row.ToJsonString()));
    }

    [Fact]
    public async Task GivenStoredRow_WhenNewerLastUpdatedArrives_ThenTheRowIsReplacedAndKeepsItsCreatedDatetime()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("a", Stored, "Stored", r =>
        {
            r["gender"] = "female";
            r["balance"] = 1.5;
        }));

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("a", "2024-01-01T00:00:00.000002Z", "Newer", r =>
            {
                r["meta"]!["versionId"] = "2";
                r["balance"] = 2.25;
            }))],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.CommittedVersion, outcome.InsertedRows, outcome.UpdatedRows, outcome.UnchangedRows).ShouldBe((2L, 0L, 1L, 0L));
        var row = RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a");
        FamilyOf(row).ShouldBe("Newer");
        var nameString = (string?)row["name_string"];
        nameString.ShouldNotBeNull().ShouldContain("Newer");
        nameString.ShouldNotContain("Stored");
        ((string?)row["meta"]!["versionId"]).ShouldBe("2");
        ((string?)row["balance"]).ShouldBe("2.2500000000");
        ((string?)row["meta_lastUpdated"]).ShouldBe("2024-01-01T00:00:00.000002Z");
        ((string?)row["msftCreatedDatetime"]).ShouldBe(FirstIngestedAt);
        ((string?)row["msftModifiedDatetime"]).ShouldBe(SecondIngestedAt);
        ((string?)row["msftFilePath"]).ShouldBe(WriterContracts.SecondFile);

        // The whole row is replaced, so a field the newer version lacks is cleared rather than kept.
        row["gender"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenResourcesWithoutLastUpdated_WhenWritten_ThenTheyAreDroppedAndNotInserted()
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();
        var table = root.Combine("Patient");

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("kept", Stored), Patient("undated", lastUpdated: null))],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.Outcome, outcome.CommittedVersion, outcome.InsertedRows, outcome.DroppedRows)
            .ShouldBe((FabricSilverTableOutcome.Created, (long?)1, 1L, 1));
        (await DeltaParquetReader.ReadLiveRowsAsync(table)).ShouldHaveSingleItem()["id"]!.GetValue<string>()
            .ShouldBe(SilverId("Patient", "kept"));
    }

    [Fact]
    public async Task GivenOnlyResourcesWithoutLastUpdated_WhenWritten_ThenTheTableIsCreatedEmptyAndNoMergeRuns()
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();
        var table = root.Combine("Patient");

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("undated", lastUpdated: null))],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.Outcome, outcome.CommittedVersion, outcome.DroppedRows, outcome.Attempts)
            .ShouldBe((FabricSilverTableOutcome.Created, (long?)null, 1, 0));
        DeltaLogReader.Read(table).Version.ShouldBe(0);
        (await DeltaParquetReader.ReadLiveRowsAsync(table)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenDuplicateIdsInOneBatch_WhenWritten_ThenTheLatestWinsAndATieKeepsTheLastOccurrence()
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();
        var table = root.Combine("Patient");

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(
                    table,
                    contract,
                    Patient("latest", "2024-03-01T00:00:00Z", "Newest"),
                    Patient("latest", "2024-01-01T00:00:00Z", "Older"),
                    Patient("tied", "2024-02-02T00:00:00Z", "FirstOfTie"),
                    Patient("tied", "2024-02-02T00:00:00Z", "LastOfTie")),
            ],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.InsertedRows, outcome.CollapsedRows, outcome.DroppedRows).ShouldBe((2L, 2, 0));
        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        rows.Count.ShouldBe(2);
        FamilyOf(RowOf(rows, "latest")).ShouldBe("Newest");
        FamilyOf(RowOf(rows, "tied")).ShouldBe("LastOfTie");
    }

    [Fact]
    public async Task GivenStoredRow_WhenANewKeyArrivesBesideAnUnchangedOne_ThenOnlyTheNewKeyIsInserted()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SeedAsync(table, Patient("a", Stored, "Stored"));

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patient("a", Stored, "Replayed"), Patient("b", Stored, "New"))],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            CancellationToken.None);

        var outcome = result.Tables.ShouldHaveSingleItem();
        (outcome.CommittedVersion, outcome.InsertedRows, outcome.UpdatedRows, outcome.UnchangedRows).ShouldBe((2L, 1L, 0L, 1L));
        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);
        rows.Count.ShouldBe(2);
        FamilyOf(RowOf(rows, "a")).ShouldBe("Stored");
        var inserted = RowOf(rows, "b");
        FamilyOf(inserted).ShouldBe("New");
        ((string?)inserted["msftCreatedDatetime"]).ShouldBe(SecondIngestedAt);
    }
}
