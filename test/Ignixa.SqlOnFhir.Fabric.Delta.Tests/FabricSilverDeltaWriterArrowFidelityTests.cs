// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// Values that are easy to lose between a projected row and Parquet, each read back with the independent reader.
/// </summary>
public class FabricSilverDeltaWriterArrowFidelityTests
{
    [Fact]
    public async Task GivenEdgeValues_WhenWritten_ThenTheIndependentReaderReadsThemBackExactly()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();

        await FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(
                    table,
                    contract,
                    SyntheticPatients.Patient("full", "2024-01-01T00:00:00.123456Z", customize: resource =>
                    {
                        resource["balance"] = JsonNode.Parse("-1234567890123456789012345678.1234567891");
                        resource["active"] = true;
                        resource["tiny"] = JsonNode.Parse("0.12345678901234567890123456789012345678");
                        resource["seenAt"] = JsonNode.Parse("""["1500-02-28T10:00:00.000001Z", null]""");
                        resource["weights"] = JsonNode.Parse("[1.5, null, -2.000001]");
                        resource["birthDate"] = "1066-10-14";
                        resource["contact"] = JsonNode.Parse("""{"relationship":{"detail":{"codes":[{"code":"x","rank":3},{"code":null}]}},"flag":-1}""");
                    }),
                    SyntheticPatients.Patient("empty", "2024-01-01T00:00:00Z", customize: resource =>
                    {
                        resource["weights"] = new JsonArray();
                        resource["contact"] = JsonNode.Parse("""{"flag":null}""");
                        resource["active"] = false;
                        resource["balance"] = 0;
                    }),
                    SyntheticPatients.Patient("absent", "2024-01-01T00:00:00Z")),
            ],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        var rows = await DeltaParquetReader.ReadLiveRowsAsync(table);

        var full = SyntheticPatients.RowOf(rows, "full");
        ((string?)full["balance"]).ShouldBe("-1234567890123456789012345678.1234567891");
        ((bool?)full["active"]).ShouldBe(true);
        ((string?)full["tiny"]).ShouldBe("0.12345678901234567890123456789012345678");
        ((string?)full["meta_lastUpdated"]).ShouldBe("2024-01-01T00:00:00.123456Z");
        ((string?)full["meta"]!["lastUpdated"]).ShouldBe("2024-01-01T00:00:00.123456Z");
        ShouldBeJson(full["seenAt"], """["1500-02-28T10:00:00.000001Z",null]""");
        ShouldBeJson(full["weights"], """["1.500000",null,"-2.000001"]""");
        ((string?)full["birthDate"]).ShouldBe("1066-10-14");
        ShouldBeJson(full["contact"], """{"relationship":{"detail":{"codes":[{"code":"x","rank":3},{"code":null,"rank":null}]}},"flag":-1}""");

        var empty = SyntheticPatients.RowOf(rows, "empty");
        ShouldBeJson(empty["weights"], "[]");
        ShouldBeJson(empty["contact"], """{"relationship":null,"flag":null}""");
        ((bool?)empty["active"]).ShouldBe(false);
        ((string?)empty["balance"]).ShouldBe("0.0000000000");

        var absent = SyntheticPatients.RowOf(rows, "absent");
        absent["weights"].ShouldBeNull();
        absent["contact"].ShouldBeNull();
        absent["balance"].ShouldBeNull();
    }

    [Fact]
    public async Task GivenAColumnNameThatNeedsQuoting_WhenARowIsInsertedAndUpdated_ThenTheColumnFollowsEachWrite()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(
            table,
            SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z", customize: resource => resource[WriterContracts.HyphenatedColumn] = "first"));

        var updated = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z", customize: resource => resource[WriterContracts.HyphenatedColumn] = "second"))],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        updated.Tables[0].UpdatedRows.ShouldBe(1);
        var row = SyntheticPatients.RowOf(await DeltaParquetReader.ReadLiveRowsAsync(table), "a");
        ((string?)row[WriterContracts.HyphenatedColumn]).ShouldBe("second");
    }

    private static void ShouldBeJson(JsonNode? actual, string expected) =>
        JsonNode.DeepEquals(actual, JsonNode.Parse(expected)).ShouldBeTrue($"Expected {expected} but read {actual?.ToJsonString() ?? "null"}.");
}
