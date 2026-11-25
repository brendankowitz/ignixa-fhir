// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using DeltaLake.Table;
using Ignixa.Serialization.SourceNodes;
using Xunit.Abstractions;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

/// <summary>
/// The B3 memory budget: one mixed 5,000-row batch merged into a table at the default target-row ceiling, with rows of
/// realistic width, in a few large files and in as many small files as the default file ceiling allows.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in through <c>IGNIXA_FABRIC_DELTA_MEMORY_TEST=1</c>, and each case must run on its own filter, so the test host
/// holds nothing but that case. The budget is on peak private bytes, the metric the probe measured, read from the
/// operating system's peak commit charge of the process (<see cref="Process.PeakPagedMemorySize64"/>, which is
/// PeakPagefileUsage on Windows); peak working set (<see cref="Process.PeakWorkingSet64"/>) is asserted as a second
/// figure. The two are not ordered: committed but untouched pages (DataFusion and Arrow pre-allocate capacity) and
/// trimmed pages count only in private bytes, shared image pages only in working set. Both are operating-system high-water
/// marks with no sampling gap, but they cover the process lifetime, seeding included, and a garbage collection does not
/// reset them; that is a conservative pass condition because seeding can only raise them. Seeding runs with the
/// writer's engine settings so it does not set the peak on a many-core host. Isolating the merge in a child process
/// would need a second executable, since the test assembly has no runnable entry point, so it is not done; the merge
/// window's sampled private bytes and working set are reported beside the lifetime figures instead. Peak private bytes
/// is read only on Windows: elsewhere no equivalent is defined, so the cases skip rather than assert another metric.
/// </para>
/// <para>
/// The table is seeded with plain appends (one file each) rather than through the writer, because seeding a hundred
/// files through a hundred merges would take far longer and measure nothing new.
/// </para>
/// </remarks>
public sealed class FabricSilverDeltaWriterMemoryBudgetTests(ITestOutputHelper output) : IDisposable
{
    private const string OptInVariable = "IGNIXA_FABRIC_DELTA_MEMORY_TEST";

    /// <summary>4.5 GiB.</summary>
    private const long BudgetBytes = 4_831_838_208;

    private const int SourceRows = 5_000;
    private const string SeedLastUpdated = "2025-01-01T00:00:00Z";
    private const string BatchLastUpdated = "2025-06-01T00:00:00Z";

    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [SkippableFact]
    public Task GivenAWideObservationTableInTenFiles_WhenAMixedBatchIsMerged_ThenPeakMemoryStaysInBudget() =>
        MeasureAsync("Observation", WideSilverResources.ObservationSchema(), WideSilverResources.Observation, files: 10);

    [SkippableFact]
    public Task GivenAWideObservationTableInAHundredFiles_WhenAMixedBatchIsMerged_ThenPeakMemoryStaysInBudget() =>
        MeasureAsync("Observation", WideSilverResources.ObservationSchema(), WideSilverResources.Observation, files: 100);

    [SkippableFact]
    public Task GivenAWidePatientTableInTenFiles_WhenAMixedBatchIsMerged_ThenPeakMemoryStaysInBudget() =>
        MeasureAsync("Patient", WideSilverResources.PatientSchema(), WideSilverResources.Patient, files: 10);

    [SkippableFact]
    public Task GivenAWidePatientTableInAHundredFiles_WhenAMixedBatchIsMerged_ThenPeakMemoryStaysInBudget() =>
        MeasureAsync("Patient", WideSilverResources.PatientSchema(), WideSilverResources.Patient, files: 100);

    // Not opt-in: keeps the wide fixtures valid against the projection and the writer, so the measurement cannot rot.
    [Fact]
    public async Task GivenWideObservationsAndPatients_WhenWritten_ThenEveryRowLandsWithItsCompanionsFilled()
    {
        var observations = await WriterContracts.LoadAsync(WideSilverResources.ObservationSchema(), "Observation");
        var patients = await WriterContracts.LoadAsync(WideSilverResources.PatientSchema(), "Patient");
        var observationTable = _directory.Combine("Observation");
        var patientTable = _directory.Combine("Patient");

        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [
                WriterContracts.Batch(observationTable, observations, [.. Enumerable.Range(0, 3).Select(key => WideSilverResources.Observation(key, SeedLastUpdated))]),
                WriterContracts.Batch(patientTable, patients, [.. Enumerable.Range(0, 3).Select(key => WideSilverResources.Patient(key, SeedLastUpdated))]),
            ],
            WriterContracts.Ingestion(),
            CancellationToken.None);

        result.Tables.Select(table => table.InsertedRows).ShouldBe([3, 3]);
        var observationRow = (await DeltaParquetReader.ReadLiveRowsAsync(observationTable))[0];
        foreach (var column in new[] { "code_string", "subject_string", "component_string", "note_string", "identifier_string" })
        {
            ((string?)observationRow[column]).ShouldNotBeNullOrEmpty(column);
        }

        observationRow["subject"]!["msftSourceReference"].ShouldNotBeNull();
        observationRow["component"]!.AsArray().Count.ShouldBe(3);
        var patientRow = (await DeltaParquetReader.ReadLiveRowsAsync(patientTable))[0];
        foreach (var column in new[] { "name_string", "telecom_string", "address_string", "identifier_string" })
        {
            ((string?)patientRow[column]).ShouldNotBeNullOrEmpty(column);
        }
    }

    private async Task MeasureAsync(string resourceType, JsonObject schema, Func<int, string, ResourceJsonNode> resource, int files)
    {
        Skip.IfNot(Environment.GetEnvironmentVariable(OptInVariable) == "1", $"{OptInVariable} is not 1; the memory-budget measurement is opt-in.");
        Skip.IfNot(
            OperatingSystem.IsWindows(),
            "The budget is on peak private bytes, read from the Windows peak commit charge (Process.PeakPagedMemorySize64); no equivalent is defined on this operating system.");

        var options = new FabricSilverWriterOptions();
        var targetRows = checked((int)options.MaxTargetRows);
        files.ShouldBeLessThanOrEqualTo(options.MaxTargetFiles);
        var contract = await WriterContracts.LoadAsync(schema, resourceType);
        var tablePath = _directory.Combine(resourceType);

        var setup = Stopwatch.StartNew();
        await SeedAsync(tablePath, contract, resource, targetRows, files);
        setup.Stop();
        (await LiveDataFiles.CountAsync(tablePath, files, CancellationToken.None)).ShouldBe(files);
        var seededBytes = ParquetBytes(tablePath);

        // Half the batch updates keys spread over every file, half inserts new keys.
        var source = Enumerable.Range(0, SourceRows)
            .Select(n => resource(n % 2 == 0 ? (int)((long)n * 10_007 % targetRows) : targetRows + n, BatchLastUpdated))
            .ToArray();
        var batch = WriterContracts.Batch(tablePath, contract, source);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        var beforeWorkingSet = process.WorkingSet64;

        using var sampler = new MemorySampler();
        var merge = Stopwatch.StartNew();
        var result = await FabricSilverDeltaWriter.WriteBatchAsync(
            [batch],
            WriterContracts.Ingestion(filePath: WriterContracts.SecondFile, ingestedAt: WriterContracts.SecondIngestion),
            options,
            CancellationToken.None);
        merge.Stop();
        var window = sampler.Stop();
        process.Refresh();
        var peakPrivateBytes = process.PeakPagedMemorySize64;
        var peakWorkingSet = process.PeakWorkingSet64;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""
            {resourceType}: {targetRows:N0} rows in {files} files, {seededBytes / 1_048_576.0:F1} MiB on disk, {(double)seededBytes / targetRows:F0} bytes per row; seeded in {setup.Elapsed.TotalSeconds:F1} s
            merge of {SourceRows:N0} rows (inserted {result.Tables[0].InsertedRows:N0}, updated {result.Tables[0].UpdatedRows:N0}, attempts {result.Tables[0].Attempts}): {merge.Elapsed.TotalSeconds:F1} s
            working set before merge {Mib(beforeWorkingSet)}; merge window (sampled) peak private bytes {Mib(window.PeakPrivateBytes)}, peak working set {Mib(window.PeakWorkingSetBytes)}
            process lifetime peak, including seeding: private bytes {Gib(peakPrivateBytes)}, working set {Gib(peakWorkingSet)}; budget {Gib(BudgetBytes)} for each
            """));

        result.Tables[0].InsertedRows.ShouldBe(SourceRows / 2);
        result.Tables[0].UpdatedRows.ShouldBe(SourceRows / 2);
        peakPrivateBytes.ShouldBeLessThanOrEqualTo(BudgetBytes, "peak private bytes");
        peakWorkingSet.ShouldBeLessThanOrEqualTo(BudgetBytes, "peak working set");
    }

    // One plain append per file at version 1..files, every row from the first ingestion.
    private static async Task SeedAsync(
        string tablePath,
        FabricSilverContract contract,
        Func<int, string, ResourceJsonNode> resource,
        int targetRows,
        int files)
    {
        await FabricSilverDeltaTables.EnsureTableAsync(tablePath, contract, CancellationToken.None);
        var shape = WriterContracts.Batch(tablePath, contract);
        var projection = FabricSilverProjection.Create(contract, WriterContracts.R4);
        var rowsPerFile = targetRows / files;
        using var engine = new DeltaEngine(new EngineOptions
        {
            DataFusionExecutionTargetPartitions = MergeEngineSettings.TargetPartitions,
            DataFusionExecutionBatchSize = MergeEngineSettings.BatchSize,
        });

        for (var file = 0; file < files; file++)
        {
            var rows = Enumerable.Range(file * rowsPerFile, rowsPerFile)
                .Select(key => projection.Project(resource(key, SeedLastUpdated), WriterContracts.Ingestion()))
                .ToList();
            using var arrow = SilverArrowConverter.Convert(shape.Columns, shape.ArrowSchema, rows);
            using var table = await engine.LoadTableAsync(new TableOptions { TableLocation = tablePath }, CancellationToken.None);
            await table.InsertAsync(
                [arrow],
                shape.ArrowSchema,
                new InsertOptions { SaveMode = SaveMode.Append, MaxRowsPerGroup = (ulong)rowsPerFile },
                CancellationToken.None);
        }
    }

    private static long ParquetBytes(string tablePath) =>
        Directory.EnumerateFiles(tablePath, "*.parquet", SearchOption.TopDirectoryOnly).Sum(file => new FileInfo(file).Length);

    private static string Mib(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1_048_576.0:F0} MiB");

    private static string Gib(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{Mib(bytes)} ({bytes / 1_073_741_824.0:F2} GiB)");
}
