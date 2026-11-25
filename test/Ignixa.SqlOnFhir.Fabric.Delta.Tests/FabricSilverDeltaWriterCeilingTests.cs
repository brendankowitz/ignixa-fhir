// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Apache.Arrow;
using Apache.Arrow.Types;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The fail-closed size ceilings: a batch over one writes nothing to any table, and a size that cannot be proven fails.
/// </summary>
public class FabricSilverDeltaWriterCeilingTests
{
    [Fact]
    public async Task GivenABatchOverTheSourceCeiling_WhenWritten_ThenItFailsBeforeAnyTableIsCreated()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await WriterContracts.PatientAsync();

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(first, contract, Patients("a", "b")),
                WriterContracts.Batch(second, contract, Patients("a", "b", "c")),
            ],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxSourceResourcesPerTable = 2 },
            CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.SourceResources);
        exception.TablePath.ShouldBe(second);
        exception.LimitValue.ShouldBe(2);
        exception.Actual.ShouldBe(3);
        exception.Message.ShouldContain(second);
        Directory.Exists(first).ShouldBeFalse();
        Directory.Exists(second).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenABatchAtTheSourceCeiling_WhenWritten_ThenItIsWritten()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patients("a", "b"))],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxSourceResourcesPerTable = 2 },
            CancellationToken.None);

        result.Tables[0].InsertedRows.ShouldBe(2);
    }

    [Fact]
    public async Task GivenDuplicatesThatReduceUnderTheSourceCeiling_WhenWritten_ThenTheInputResourcesAreCounted()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(
                    table,
                    contract,
                    SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"),
                    SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z"),
                    SyntheticPatients.Patient("a", "2024-03-01T00:00:00Z")),
            ],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxSourceResourcesPerTable = 2 },
            CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.SourceResources);
        exception.Actual.ShouldBe(3);
        Directory.Exists(table).ShouldBeFalse();
    }

    /// <summary>
    /// Another writer creates the table and fills it past the row ceiling between this call's first look and its own
    /// creation, so the call loses the creation race to a table it must still refuse to merge into.
    /// </summary>
    [Fact]
    public async Task GivenACreationRaceLostToATableOverTheTargetRowCeiling_WhenWritten_ThenTheCeilingStillFails()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();
        var options = new FabricSilverWriterOptions
        {
            MaxTargetRows = 2,
            BeforeCreateTable = async path => await SyntheticPatients.SeedAsync(path, Patients("a", "b", "c")),
        };

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patients("new"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.TargetRows);
        exception.TablePath.ShouldBe(table);
        exception.Actual.ShouldBe(3);
        DeltaLogReader.Read(table).Version.ShouldBe(1);
    }

    [Fact]
    public async Task GivenTheSecondTableOverTheTargetRowCeiling_WhenWritten_ThenNoTableChangesAndTheErrorNamesThatTable()
    {
        using var root = new TemporaryDirectory();
        var first = root.Combine("First");
        var second = root.Combine("Second");
        var contract = await SyntheticPatients.SeedAsync(first, Patients("a"));
        await SyntheticPatients.SeedAsync(second, Patients("a", "b", "c"));

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(first, contract, Patients("new")),
                WriterContracts.Batch(second, contract, Patients("new")),
            ],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxTargetRows = 2 },
            CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.TargetRows);
        exception.TablePath.ShouldBe(second);
        exception.LimitValue.ShouldBe(2);
        exception.Actual.ShouldBe(3);
        exception.Message.ShouldContain(second);
        DeltaLogReader.Read(first).Version.ShouldBe(1);
        DeltaLogReader.Read(second).Version.ShouldBe(1);
    }

    [Fact]
    public async Task GivenATableAtTheTargetRowCeiling_WhenWritten_ThenItIsMerged()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, Patients("a", "b"));

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patients("c"))],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxTargetRows = 2 },
            CancellationToken.None);

        result.Tables[0].CommittedVersion.ShouldBe(2);
    }

    [Fact]
    public async Task GivenATableOverTheTargetFileCeiling_WhenWritten_ThenNothingIsWrittenAndTheErrorAsksForCompaction()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, Patients("a"));
        await FabricSilverDeltaWriter.WriteBatchAsync([WriterContracts.Batch(table, contract, Patients("b"))], WriterContracts.Ingestion(), CancellationToken.None);
        DeltaLogReader.Read(table).LiveFiles.Count.ShouldBe(2);

        var exception = await Should.ThrowAsync<FabricSilverLimitExceededException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patients("c"))],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxTargetFiles = 1 },
            CancellationToken.None));

        exception.Limit.ShouldBe(FabricSilverLimit.TargetFiles);
        exception.TablePath.ShouldBe(table);
        exception.LimitValue.ShouldBe(1);
        exception.Actual.ShouldBe(2);
        exception.Message.ShouldContain(table);
        exception.Message.ShouldContain("OPTIMIZE");
        DeltaLogReader.Read(table).Version.ShouldBe(2);
    }

    [Fact]
    public async Task GivenATableAtTheTargetFileCeiling_WhenWritten_ThenItIsMerged()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await SyntheticPatients.SeedAsync(table, Patients("a"));
        await FabricSilverDeltaWriter.WriteBatchAsync([WriterContracts.Batch(table, contract, Patients("b"))], WriterContracts.Ingestion(), CancellationToken.None);

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, Patients("c"))],
            WriterContracts.Ingestion(),
            new FabricSilverWriterOptions { MaxTargetFiles = 2 },
            CancellationToken.None);

        result.Tables[0].CommittedVersion.ShouldBe(3);
    }

    [Theory]
    [InlineData("no batch")]
    [InlineData("two batches")]
    [InlineData("two rows")]
    [InlineData("two columns")]
    [InlineData("string count")]
    [InlineData("null count")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The batches are disposed in the finally block.")]
    public void GivenARowCountResultOfTheWrongShape_WhenChecked_ThenTheCountIsUnknownAndFailsClosed(string shape)
    {
        List<RecordBatch> rows = shape switch
        {
            "no batch" => [],
            "two batches" => [Count(1), Count(1)],
            "two rows" => [Count(1, 2)],
            "two columns" => [new RecordBatch(new Schema([Int64Field("a"), Int64Field("b")], null), [Longs(1), Longs(1)], 1)],
            "string count" => [new RecordBatch(new Schema([new Field("row_count", StringType.Default, true)], null), [new StringArray.Builder().Append("1").Build()], 1)],
            "null count" => [new RecordBatch(new Schema([Int64Field("row_count")], null), [new Int64Array.Builder().AppendNull().Build()], 1)],
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        try
        {
            var exception = Should.Throw<FabricSilverLimitExceededException>(() => TargetTableSize.RequireRows(rows, "Tables/Patient", 10));

            exception.Limit.ShouldBe(FabricSilverLimit.TargetRowsUnknown);
            exception.Actual.ShouldBeNull();
            exception.LimitValue.ShouldBe(10);
        }
        finally
        {
            rows.ForEach(batch => batch.Dispose());
        }
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void GivenARowCountBesideAnEmptyBatch_WhenChecked_ThenOnlyACountAboveTheLimitFails(long count, bool fails)
    {
        using var empty = new RecordBatch(new Schema([Int64Field("row_count")], null), [new Int64Array.Builder().Build()], 0);
        using var counted = Count(count);
        var check = () => TargetTableSize.RequireRows([empty, counted], "Tables/Patient", 10);

        if (fails)
        {
            var exception = check.ShouldThrow<FabricSilverLimitExceededException>();
            exception.Limit.ShouldBe(FabricSilverLimit.TargetRows);
            exception.Actual.ShouldBe(count);
        }
        else
        {
            check.ShouldNotThrow();
        }
    }

    private static Ignixa.Serialization.SourceNodes.ResourceJsonNode[] Patients(params string[] ids) =>
        [.. ids.Select(id => SyntheticPatients.Patient(id, "2024-01-01T00:00:00Z"))];

    private static Field Int64Field(string name) => new(name, Int64Type.Default, true);

    private static Int64Array Longs(params long[] values) => new Int64Array.Builder().AppendRange(values).Build();

    private static RecordBatch Count(params long[] values) =>
        new(new Schema([Int64Field("row_count")], null), [Longs(values)], values.Length);
}
