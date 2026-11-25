// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using DeltaLake.Errors;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>Inputs the writer refuses before it touches a table, and tables it refuses to write to.</summary>
public class FabricSilverDeltaWriterValidationTests
{
    [Fact]
    public void GivenDefaultOptions_WhenRead_ThenTheyAreTheMeasuredCeilings()
    {
        var options = new FabricSilverWriterOptions();

        options.MaxSourceResourcesPerTable.ShouldBe(50_000);
        options.MaxTargetRows.ShouldBe(50_000);
        options.MaxTargetFiles.ShouldBe(100);
        options.MaxMergeAttempts.ShouldBe(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenANonPositiveLimit_WhenOptionsAreCreated_ThenEachLimitIsRejected(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FabricSilverWriterOptions { MaxSourceResourcesPerTable = value })
            .ParamName.ShouldBe(nameof(FabricSilverWriterOptions.MaxSourceResourcesPerTable));
        Should.Throw<ArgumentOutOfRangeException>(() => new FabricSilverWriterOptions { MaxTargetRows = value })
            .ParamName.ShouldBe(nameof(FabricSilverWriterOptions.MaxTargetRows));
        Should.Throw<ArgumentOutOfRangeException>(() => new FabricSilverWriterOptions { MaxTargetFiles = value })
            .ParamName.ShouldBe(nameof(FabricSilverWriterOptions.MaxTargetFiles));
        Should.Throw<ArgumentOutOfRangeException>(() => new FabricSilverWriterOptions { MaxMergeAttempts = value })
            .ParamName.ShouldBe(nameof(FabricSilverWriterOptions.MaxMergeAttempts));
    }

    [Fact]
    public void GivenTheSmallestLimits_WhenOptionsAreCreated_ThenTheyAreKept()
    {
        var options = new FabricSilverWriterOptions { MaxSourceResourcesPerTable = 1, MaxTargetRows = 1, MaxTargetFiles = 1, MaxMergeAttempts = 1 };

        options.ShouldBe(new FabricSilverWriterOptions { MaxSourceResourcesPerTable = 1, MaxTargetRows = 1, MaxTargetFiles = 1, MaxMergeAttempts = 1 });
        options.MaxTargetFiles.ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GivenABlankTablePath_WhenABatchIsCreated_ThenItIsRejected(string tablePath)
    {
        var contract = await WriterContracts.PatientAsync();

        Should.Throw<ArgumentException>(() => WriterContracts.Batch(tablePath, contract)).ParamName.ShouldBe("tablePath");
    }

    [Fact]
    public async Task GivenANullArgumentOrResource_WhenABatchIsCreated_ThenEachIsRejectedByName()
    {
        var contract = await WriterContracts.PatientAsync();
        var projection = FabricSilverProjection.Create(contract, WriterContracts.R4);

        Should.Throw<ArgumentNullException>(() => new FabricSilverTableBatch("Patient", null!, projection, [])).ParamName.ShouldBe("contract");
        Should.Throw<ArgumentNullException>(() => new FabricSilverTableBatch("Patient", contract, null!, [])).ParamName.ShouldBe("projection");
        Should.Throw<ArgumentNullException>(() => new FabricSilverTableBatch("Patient", contract, projection, null!)).ParamName.ShouldBe("resources");
        var nullElement = Should.Throw<ArgumentException>(() => new FabricSilverTableBatch("Patient", contract, projection, [null!]));
        nullElement.ShouldNotBeOfType<ArgumentNullException>();
        nullElement.ParamName.ShouldBe("resources");
    }

    [Fact]
    public async Task GivenAProjectionOfAnotherContract_WhenABatchIsCreated_ThenItIsRejected()
    {
        var contract = await WriterContracts.PatientAsync();
        var other = await PatientContractAsync(fields => fields.Remove(FieldNamed(fields, "gender")));

        Should.Throw<ArgumentException>(() => new FabricSilverTableBatch("Patient", contract, FabricSilverProjection.Create(other, WriterContracts.R4), []))
            .ParamName.ShouldBe("projection");
    }

    [Theory]
    [InlineData("type")]
    [InlineData("nullability")]
    public async Task GivenAProjectionOfAContractDifferingOnlyInAColumnTypeOrNullability_WhenABatchIsCreated_ThenItIsRejected(string difference)
    {
        var contract = await WriterContracts.PatientAsync();
        var other = await PatientContractAsync(fields =>
        {
            if (difference == "type")
            {
                FieldNamed(fields, "visits")["type"] = "integer";
            }
            else
            {
                FieldNamed(fields, "gender")["nullable"] = false;
            }
        });

        Should.Throw<ArgumentException>(() => new FabricSilverTableBatch("Patient", contract, FabricSilverProjection.Create(other, WriterContracts.R4), []))
            .ParamName.ShouldBe("projection");
    }

    [Fact]
    public async Task GivenAProjectionOfAnEqualContractLoadedSeparately_WhenABatchIsCreated_ThenItIsAccepted()
    {
        var contract = await WriterContracts.PatientAsync();
        var equal = await WriterContracts.PatientAsync();

        var batch = new FabricSilverTableBatch("Patient", contract, FabricSilverProjection.Create(equal, WriterContracts.R4), []);

        batch.Contract.ShouldBeSameAs(contract);
    }

    [Theory]
    [InlineData("remove meta")]
    [InlineData("meta as string")]
    [InlineData("meta without lastUpdated")]
    public async Task GivenAContractWithoutATimestampMetaLastUpdatedField_WhenABatchIsCreated_ThenItIsRejected(string change)
    {
        var contract = await PatientContractAsync(fields =>
        {
            var meta = FieldNamed(fields, "meta");
            switch (change)
            {
                case "remove meta":
                    fields.Remove(meta);
                    break;
                case "meta as string":
                    meta["type"] = "string";
                    break;
                default:
                    meta["type"] = WriterContracts.Struct(WriterContracts.Field("versionId", "string"));
                    break;
            }
        });

        var exception = Should.Throw<ArgumentException>(() => WriterContracts.Batch("Patient", contract));

        exception.ParamName.ShouldBe("contract");
        exception.Message.ShouldContain("struct column 'meta' with a timestamp field 'lastUpdated'");
    }

    [Fact]
    public async Task GivenAContractWithoutMetaLastUpdated_WhenABatchIsCreated_ThenTheWriterNamesTheMergeColumn()
    {
        var contract = await PatientContractAsync(fields => fields.Remove(FieldNamed(fields, "meta_lastUpdated")));

        var exception = Should.Throw<ArgumentException>(() => WriterContracts.Batch("Patient", contract));

        exception.ParamName.ShouldBe("contract");
        exception.Message.ShouldContain("timestamp column 'meta_lastUpdated'");
    }

    // The projection, not the writer, owns these rules; the tests pin that no batch can be built past them.
    [Theory]
    [InlineData("remove id")]
    [InlineData("remove msftCreatedDatetime")]
    [InlineData("msftCreatedDatetime as date")]
    [InlineData("meta_lastUpdated as string")]
    [InlineData("add double")]
    [InlineData("add binary")]
    public async Task GivenAContractTheProjectionRefuses_WhenABatchIsCreated_ThenNoBatchExists(string change)
    {
        var contract = await PatientContractAsync(fields =>
        {
            switch (change)
            {
                case "remove id":
                    fields.Remove(FieldNamed(fields, "id"));
                    break;
                case "remove msftCreatedDatetime":
                    fields.Remove(FieldNamed(fields, "msftCreatedDatetime"));
                    break;
                case "msftCreatedDatetime as date":
                    FieldNamed(fields, "msftCreatedDatetime")["type"] = "date";
                    break;
                case "meta_lastUpdated as string":
                    FieldNamed(fields, "meta_lastUpdated")["type"] = "string";
                    break;
                default:
                    fields.Add(WriterContracts.Field("extra", change["add ".Length..]));
                    break;
            }
        });

        Should.Throw<InvalidDataException>(() => WriterContracts.Batch("Patient", contract));
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("1st")]
    [InlineData("tick`")]
    [InlineData("trailing\n")]
    public async Task GivenAColumnNameTheWriterCannotQuote_WhenABatchIsCreated_ThenItIsRejectedWithoutTheName(string name)
    {
        var contract = await PatientContractAsync(fields => fields.Add(WriterContracts.Field(name, "string")));

        var exception = Should.Throw<ArgumentException>(() => WriterContracts.Batch("Patient", contract));

        exception.ParamName.ShouldBe("contract");
        exception.Message.ShouldNotContain(name);
    }

    [Fact]
    public async Task GivenNullArguments_WhenWritten_ThenEachIsRejected()
    {
        var contract = await WriterContracts.PatientAsync();
        var batch = WriterContracts.Batch("Patient", contract);

        (await Should.ThrowAsync<ArgumentNullException>(() => FabricSilverDeltaWriter.WriteBatchAsync(null!, WriterContracts.Ingestion(), CancellationToken.None)))
            .ParamName.ShouldBe("batches");
        (await Should.ThrowAsync<ArgumentNullException>(() => FabricSilverDeltaWriter.WriteBatchAsync([batch], null!, CancellationToken.None)))
            .ParamName.ShouldBe("ingestion");
        (await Should.ThrowAsync<ArgumentNullException>(() => FabricSilverDeltaWriter.WriteBatchAsync([batch], WriterContracts.Ingestion(), null!, CancellationToken.None)))
            .ParamName.ShouldBe("options");
        (await Should.ThrowAsync<ArgumentNullException>(() => FabricSilverDeltaWriter.WriteBatchAsync([batch, null!], WriterContracts.Ingestion(), CancellationToken.None)))
            .ParamName.ShouldBe("batches");
    }

    [Fact]
    public async Task GivenTwoBatchesForOneTableInDifferentCase_WhenWritten_ThenTheBatchIsRejectedAndNothingIsCreated()
    {
        using var root = new TemporaryDirectory();
        var contract = await WriterContracts.PatientAsync();

        var exception = await Should.ThrowAsync<ArgumentException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(root.Combine("Other"), contract, SyntheticPatients.Patient("c", "2024-01-01T00:00:00Z")),
                WriterContracts.Batch(root.Combine("Patient"), contract, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z")),
                WriterContracts.Batch(root.Combine("PATIENT"), contract, SyntheticPatients.Patient("b", "2024-01-01T00:00:00Z")),
            ],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.ParamName.ShouldBe("batches");
        Directory.EnumerateFileSystemEntries(root.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenADirectoryHoldingFilesButNoLog_WhenWritten_ThenValidationFailsAndNoTableIsCreatedOnTopOfIt()
    {
        using var root = new TemporaryDirectory();
        var table = Directory.CreateDirectory(root.Combine("Patient")).FullName;
        await File.WriteAllTextAsync(Path.Combine(table, "notes.txt"), "not a table");
        var contract = await WriterContracts.PatientAsync();

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"))],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Validate);
        exception.Attempts.ShouldBe(0);
        exception.CompletedTables.ShouldBeEmpty();
        exception.InnerException.ShouldBeOfType<FabricSilverTableMismatchException>().Aspect.ShouldBe(FabricSilverTableAspect.UnrecognizedContent);
        Directory.Exists(Path.Combine(table, "_delta_log")).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnExistingTableOfAnotherContract_WhenWritten_ThenValidationFailsAndTheTableIsUnchanged()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        await SyntheticPatients.SeedAsync(table, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"));
        var other = await PatientContractAsync(fields => fields.Remove(FieldNamed(fields, "gender")));

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, other, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z"))],
            WriterContracts.Ingestion(),
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Validate);
        exception.Message.ShouldContain(table);
        exception.InnerException.ShouldBeOfType<FabricSilverTableMismatchException>().Aspect.ShouldBe(FabricSilverTableAspect.Schema);
        DeltaLogReader.Read(table).Version.ShouldBe(1);
    }

    [Fact]
    public async Task GivenACreationRaceLostToATableOfAnotherContract_WhenWritten_ThenItFailsInValidateWithTheMismatch()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();
        var other = await PatientContractAsync(fields => fields.Remove(FieldNamed(fields, "gender")));
        var options = new FabricSilverWriterOptions
        {
            BeforeCreateTable = async path => await FabricSilverDeltaTables.EnsureTableAsync(path, other, CancellationToken.None),
        };

        var exception = await Should.ThrowAsync<FabricSilverWriteException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-02-01T00:00:00Z"))],
            WriterContracts.Ingestion(),
            options,
            CancellationToken.None));

        exception.Phase.ShouldBe(FabricSilverWritePhase.Validate);
        exception.MayHaveCommitted.ShouldBeFalse();
        exception.InnerException.ShouldBeOfType<FabricSilverTableMismatchException>().Aspect.ShouldBe(FabricSilverTableAspect.Schema);
        DeltaLogReader.Read(table).Version.ShouldBe(0);
    }

    [Fact]
    public async Task GivenACanceledToken_WhenWritten_ThenCancellationSurfacesAndNoTableIsCreated()
    {
        using var root = new TemporaryDirectory();
        var table = root.Combine("Patient");
        var contract = await WriterContracts.PatientAsync();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => FabricSilverDeltaWriter.WriteBatchAsync(
            [WriterContracts.Batch(table, contract, SyntheticPatients.Patient("a", "2024-01-01T00:00:00Z"))],
            WriterContracts.Ingestion(),
            canceled.Token));

        Directory.Exists(table).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Commit failed: a concurrent transaction deleted data this operation read.", true)]
    [InlineData("Commit failed: a concurrent transactions added new data", true)]
    [InlineData("Metadata changed since last commit.", false)]
    [InlineData("commit failed: a concurrent transaction", false)]
    public void GivenAnEngineError_WhenClassified_ThenOnlyAConcurrentCommitConflictIsRetryable(string message, bool retryable)
    {
        MergeConflict.IsRetryable(new DeltaRuntimeException(message, 1)).ShouldBe(retryable);
    }

    [Fact]
    public void GivenTheConflictTextOutsideAnEngineError_WhenClassified_ThenItIsNotRetryable()
    {
        MergeConflict.IsRetryable(new InvalidOperationException("Commit failed: a concurrent transaction deleted data this operation read.")).ShouldBeFalse();
    }

    private static Task<FabricSilverContract> PatientContractAsync(Action<JsonArray> change)
    {
        var schema = WriterContracts.PatientSchema();
        change(schema["fields"]!.AsArray());
        return WriterContracts.LoadAsync(schema, "Patient");
    }

    private static JsonObject FieldNamed(JsonArray fields, string name) =>
        fields.OfType<JsonObject>().Single(field => (string?)field["name"] == name);
}
