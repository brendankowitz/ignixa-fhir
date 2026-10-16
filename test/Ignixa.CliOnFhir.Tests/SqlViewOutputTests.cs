// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Views;
using Parquet;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

public class SqlViewOutputTests
{
    [Fact]
    public async Task GivenOutCsv_WhenRun_ThenEveryRowIsInTheFileAndTheFactsGivePathRowsAndSize()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "obs.csv");

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Out: path));

        ViewFacts facts = Facts(envelope);
        ViewFileFacts file = facts.File.ShouldNotBeNull();
        file.ShouldBe(new ViewFileFacts(path, "csv", Rows: 5, Bytes: new FileInfo(path).Length, Created: true));
        facts.Total.ShouldBe(5);
        facts.Rows.ShouldBeNull();
        string[] lines = await File.ReadAllLinesAsync(path);
        lines.Length.ShouldBe(6);
        lines[0].TrimStart('﻿').ShouldBe("id,patient,code,value");
        lines[1].ShouldBe("o1,Patient/p1,8867-4,60");
        envelope.Evidence.Count.ShouldBe(5);
    }

    [Fact]
    public async Task GivenOutNdjsonWithADistinctCount_WhenRun_ThenEachLineIsARowObjectAndTheCountIsInTheFacts()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "obs.out");

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Out: path, Format: "ndjson", Distinct: "patient"));

        ViewFacts facts = Facts(envelope);
        facts.File.ShouldNotBeNull().Format.ShouldBe("ndjson");
        facts.Distinct.ShouldBe(new DistinctFacts("patient", Values: 2, Nulls: 0));
        string[] lines = await File.ReadAllLinesAsync(path);
        lines.Length.ShouldBe(5);
        JsonNode row = JsonNode.Parse(lines[3])!;
        ((string)row["id"]!).ShouldBe("o4");
        ((string)row["code"]!).ShouldBe("8480-6");
    }

    [Fact]
    public async Task GivenOutParquet_WhenRun_ThenTheFileHoldsTheRowsAndTheColumns()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "nested", "obs.parquet");

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Out: path));

        ViewFacts facts = Facts(envelope);
        facts.File.ShouldNotBeNull().Rows.ShouldBe(5);
        facts.File.Format.ShouldBe("parquet");
        await using FileStream stream = File.OpenRead(path);
        using ParquetReader reader = await ParquetReader.CreateAsync(stream);
        reader.Schema.Fields.Select(field => field.Name).ShouldBe(["id", "patient", "code", "value"]);
        reader.RowGroupCount.ShouldBe(1);
        using ParquetRowGroupReader group = reader.OpenRowGroupReader(0);
        group.RowCount.ShouldBe(5);
    }

    [Fact]
    public async Task GivenAPartialDateInAParquetDateColumn_WhenRun_ThenItIsKeptAsText()
    {
        using TestFolder folder = new TestFolder().Write(PatientsFile, """
            {"resourceType":"Patient","id":"p1","birthDate":"1975-03"}
            {"resourceType":"Patient","id":"p2","birthDate":"1985-06-07"}
            """);
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "patients.parquet");
        const string view = """{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"id","path":"id"},{"name":"born","path":"birthDate","type":"date"}]}]}""";

        await RunAsync(folder, new ViewRequest(view, Out: path));

        await using FileStream stream = File.OpenRead(path);
        using ParquetReader reader = await ParquetReader.CreateAsync(stream);
        using ParquetRowGroupReader group = reader.OpenRowGroupReader(0);
        Parquet.Data.DataColumn born = await group.ReadColumnAsync(reader.Schema.DataFields[1]);
        born.Data.Cast<string>().ShouldBe(["1975-03", "1985-06-07"]);
    }

    [Fact]
    public async Task GivenOutInsideTheSourceFolder_WhenRun_ThenItIsRefusedAndNothingIsWritten()
    {
        using TestFolder folder = Clinic();
        string[] before = Snapshot(folder.Path);

        foreach (string relative in new[] { "out.csv", Path.Combine("sub", "deeper", "out.csv"), Path.Combine("sub", "..", "OUT.CSV") })
        {
            ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Out: Path.Combine(folder.Path, relative)));

            IssueItem error = Error(envelope);
            error.Code.ShouldBe("out-refused");
            error.Diagnostics.ShouldContain("inside the data folder");
        }

        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public async Task GivenOutThroughASymlinkIntoTheSourceFolder_WhenRun_ThenItIsRefusedAndNothingIsWritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege Windows CI does not grant
        }

        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string link = Path.Combine(elsewhere.Path, "link");
        Directory.CreateSymbolicLink(link, folder.Path);
        string[] before = Snapshot(folder.Path);

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(ObservationView, Out: Path.Combine(link, "out.csv")));

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(folder.Path).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnExistingOutFile_WhenRunWithoutAndWithOverwrite_ThenItIsRefusedThenReplaced()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder().Write("keep.csv", "precious");
        string path = Path.Combine(elsewhere.Path, "keep.csv");

        ToolEnvelope<ViewFacts?> refused = await RunAsync(folder, new ViewRequest(PatientView, Out: path));
        (await File.ReadAllTextAsync(path)).ShouldBe("precious");
        ToolEnvelope<ViewFacts?> replaced = await RunAsync(folder, new ViewRequest(PatientView, Out: path, Overwrite: true));

        Error(refused).Diagnostics.ShouldContain("overwrite");
        Facts(replaced).File.ShouldNotBeNull().Rows.ShouldBe(3);
        (await File.ReadAllTextAsync(path)).ShouldContain("p1,female,1975-03-04");
    }

    [Fact]
    public async Task GivenOutThatIsADirectory_WhenRun_ThenItIsRefused()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Out: elsewhere.Path, Format: "csv"));

        Error(envelope).Code.ShouldBe("out-refused");
    }

    [Theory]
    [InlineData("rows.txt", null)]
    [InlineData("rows.csv", "xlsx")]
    public async Task GivenAFormatThatIsNotOffered_WhenRun_ThenInvalidParameterIsReported(string file, string? format)
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Out: Path.Combine(elsewhere.Path, file), Format: format));

        Error(envelope).Code.ShouldBe("invalid-parameter");
        Directory.GetFiles(elsewhere.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenFormatOrPagingWithoutOutOrWithIt_WhenRun_ThenTheContradictionIsRefused()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "rows.csv");

        ToolEnvelope<ViewFacts?> formatOnly = await RunAsync(folder, new ViewRequest(PatientView, Format: "csv"));
        ToolEnvelope<ViewFacts?> paged = await RunAsync(folder, new ViewRequest(PatientView, Out: path, Count: 5));

        Error(formatOnly).Code.ShouldBe("invalid-parameter");
        Error(paged).Code.ShouldBe("invalid-parameter");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAViewWithNoRows_WhenRunWithOut_ThenNoFileIsCreatedAndTheFactsSayWhy()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "none.csv");

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Where: "gender = 'other'", Out: path));

        Facts(envelope).File.ShouldBe(new ViewFileFacts(path, "csv", Rows: 0, Bytes: 0, Created: false));
        File.Exists(path).ShouldBeFalse();
        envelope.Issues.Single().Code.ShouldBe("out-not-created");
    }

    [Fact]
    public async Task GivenARunThatFailsAfterRowsWereWritten_WhenRunWithOut_ThenNoHalfWrittenFileRemains()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "partial.csv");
        // Fine for the first patients, but a column that holds several values on the last: the run fails part way through.
        const string view = """{"resourceType":"ViewDefinition","resource":"Observation","select":[{"column":[{"name":"id","path":"id"},{"name":"codes","path":"code.coding.code | iif(id = 'o5', 'a' | 'b', {})"}]}]}""";

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(view, Out: path));

        Error(envelope).Code.ShouldBe("view-evaluation-failed");
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenARunWithOut_WhenFinished_ThenTheSourceFolderIsUnchanged()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string[] before = Snapshot(folder.Path);

        await RunAsync(folder, new ViewRequest(ObservationView, Out: Path.Combine(elsewhere.Path, "obs.csv"), Compartment: "Patient/p1"));

        Snapshot(folder.Path).ShouldBe(before);
    }

    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => File.Exists(entry) ? $"{Path.GetRelativePath(root, entry)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}" : Path.GetRelativePath(root, entry))];
}
