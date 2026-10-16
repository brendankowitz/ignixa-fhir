// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using Ignixa.CliOnFhir.Caching;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Views;
using Ignixa.SqlOnFhir.Evaluation;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.SearchFixture;
using static Ignixa.CliOnFhir.Tests.ViewFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Where a run may write, and that it leaves nothing behind but a complete result.</summary>
public class SqlViewOutputSafetyTests
{
    // Fine for the first observations, then a column with several values on o5: the run fails part way through.
    private const string FailingView = """{"resourceType":"ViewDefinition","resource":"Observation","select":[{"column":[{"name":"id","path":"id"},{"name":"codes","path":"code.coding.code | iif(id = 'o5', 'a' | 'b', {})"}]}]}""";

    private const string PatientsData = """
        {"resourceType":"Patient","id":"p1","gender":"female","birthDate":"1975-03-04"}
        {"resourceType":"Patient","id":"p2","gender":"male","birthDate":"1985-06-07"}
        """;

    // The folder is root/data. A link outside it, followed by .., names a place the textual path does not: a check made on one
    // reading of the path and a write made on the other put the file in the data folder.
    [Fact]
    public async Task GivenALinkFollowedByDotDot_WhenOutIsWritten_ThenItGoesWhereTheCheckSawAndNotIntoTheDataFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege Windows CI does not grant
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        Directory.CreateDirectory(Path.Combine(root.Path, "other", "deeper"));
        string link = Path.Combine(root.Path, "lnk");
        Directory.CreateSymbolicLink(link, Path.Combine(root.Path, "other", "deeper"));
        string data = Path.Combine(root.Path, "data");
        string[] before = Snapshot(data);

        ToolEnvelope<ViewFacts?> envelope = await RunInAsync(data, new ViewRequest(PatientView, Out: Path.Combine(link, "..", "data", "bypass.ndjson"), Format: "ndjson"));

        // The operating system's reading: lnk is other/deeper, so lnk/.. is other.
        string landed = Path.Combine(root.Path, "other", "data", "bypass.ndjson");
        Facts(envelope).File.ShouldNotBeNull().Path.ShouldBe(landed);
        File.Exists(landed).ShouldBeTrue();
        Snapshot(data).ShouldBe(before);
    }

    [Fact]
    public async Task GivenALinkWhoseDotDotLandsInTheDataFolder_WhenOutIsGiven_ThenItIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        Directory.CreateDirectory(Path.Combine(root.Path, "data", "sub", "deeper"));
        string link = Path.Combine(root.Path, "lnk");
        Directory.CreateSymbolicLink(link, Path.Combine(root.Path, "data", "sub", "deeper"));
        string data = Path.Combine(root.Path, "data");
        string[] before = Snapshot(data);

        ToolEnvelope<ViewFacts?> envelope = await RunInAsync(data, new ViewRequest(PatientView, Out: Path.Combine(link, "..", "out.csv")));

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(data).ShouldBe(before);
    }

    // The check ran, then a directory of the path became a link into the data folder before the file was created.
    [Fact]
    public void GivenADirectoryReplacedByALinkIntoTheDataFolderAfterTheCheck_WhenTheFileIsCreated_ThenItIsRefusedAndNothingIsWritten()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        Directory.CreateDirectory(Path.Combine(data, "inner"));
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string swapped = Path.Combine(root.Path, "out");
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(swapped, "rows.csv"))).ShouldNotBeNull();
        Directory.CreateSymbolicLink(swapped, Path.Combine(data, "inner"));
        string[] before = Snapshot(data);

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => target.Begin([]));

        refused.Issue.Code.ShouldBe("out-refused");
        Snapshot(data).ShouldBe(before);
    }

    // Safe or not, a file is written where it was checked or not at all.
    [Fact]
    public void GivenADirectoryReplacedByALinkElsewhereAfterTheCheck_WhenTheFileIsCreated_ThenItIsRefusedAndNothingIsWrittenThere()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        string elsewhere = Directory.CreateDirectory(Path.Combine(root.Path, "elsewhere")).FullName;
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string swapped = Path.Combine(root.Path, "out");
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(swapped, "rows.csv"))).ShouldNotBeNull();
        Directory.CreateSymbolicLink(swapped, elsewhere);

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => target.Begin([]));

        refused.Issue.Code.ShouldBe("out-refused");
        Directory.GetFileSystemEntries(elsewhere).ShouldBeEmpty();
    }

    // After Begin the writers hold the open file; replacing its directory with a link then changes where the path leads, not where the
    // bytes go. Commit must notice, move nothing, and leave the file; Discard must not delete through the link.
    [Theory]
    [InlineData("ndjson", true)]
    [InlineData("ndjson", false)]
    [InlineData("parquet", true)]
    [InlineData("parquet", false)]
    [InlineData("csv", false)]
    public async Task GivenTheDirectoryReplacedByALinkAfterBegin_WhenCommitted_ThenNothingLandsInTheDataFolderAndNothingIsDeletedThroughTheLink(string format, bool swapBeforeTheFirstWrite)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        string inner = Directory.CreateDirectory(Path.Combine(data, "inner")).FullName;
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string output = Path.Combine(root.Path, "out");
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(output, $"rows.{format}"))).ShouldNotBeNull();
        OutputFile file = target.Begin([]);
        string decoy = Path.Combine(inner, Path.GetFileName(file.TempPath));

        void Swap()
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, inner);
            File.WriteAllText(decoy, "decoy");
        }

        if (swapBeforeTheFirstWrite)
        {
            Swap();
        }

        ViewFileSink sink = ViewFileSink.Create(file.Stream, format, [new ColumnSchema("id", "string", false), new ColumnSchema("gender", "string", false)]);
        await sink.WriteAsync(["p1", "female"], CancellationToken.None);
        await sink.WriteAsync(["p2", "male"], CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);
        if (!swapBeforeTheFirstWrite)
        {
            Swap();
        }

        await sink.DisposeAsync();
        ToolRequestException refused = Should.Throw<ToolRequestException>(() => file.Commit());
        IssueItem left = file.Discard().ShouldNotBeNull();

        refused.Issue.Code.ShouldBe("out-refused");
        left.Code.ShouldBe("out-temp-left");
        left.Severity.ShouldBe("information");
        left.Diagnostics.ShouldContain(OperatingSystem.IsLinux() ? Path.Combine(output + ".old", Path.GetFileName(file.TempPath)) : file.TempPath);
        Directory.GetFileSystemEntries(inner).ShouldBe([decoy]);
        (await File.ReadAllTextAsync(decoy)).ShouldBe("decoy");
        new FileInfo(Path.Combine(output + ".old", Path.GetFileName(file.TempPath))).Length.ShouldBeGreaterThan(0);
        File.Exists(Path.Combine(output + ".old", $"rows.{format}")).ShouldBeFalse();
    }

    [Fact]
    public void GivenAFileThatLandedInTheDataFolder_WhenCheckedAfterTheRename_ThenItIsNamedAndLeftInPlace()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData).Write("data/landed.csv", "x");
        string data = Path.Combine(root.Path, "data");
        var target = new OutputTarget(Path.Combine(data, "landed.csv"), "csv", false, data);

        ToolRequestException error = Should.Throw<ToolRequestException>(() => target.CheckFinal(landed: null));

        error.Issue.Code.ShouldBe("out-refused");
        error.Issue.Diagnostics.ShouldContain(Path.Combine(data, "landed.csv"));
        File.Exists(Path.Combine(data, "landed.csv")).ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAFailedRunWithOverwrite_WhenTheTargetExists_ThenItIsLeftByteForByteAsItWas()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder().Write("keep.csv", "precious,content\n1,2\n");
        string path = Path.Combine(elsewhere.Path, "keep.csv");
        byte[] before = await File.ReadAllBytesAsync(path);

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(FailingView, Out: path, Overwrite: true));

        Error(envelope).Code.ShouldBe("view-evaluation-failed");
        (await File.ReadAllBytesAsync(path)).ShouldBe(before);
        Directory.GetFileSystemEntries(elsewhere.Path).ShouldBe([path]);
    }

    [Fact]
    public async Task GivenOverwriteOfAHardLinkToADataFile_WhenTheRunSucceeds_ThenTheDataFileIsNotWrittenThrough()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        string linked = Path.Combine(root.Path, "linked.csv");
        if (!TryHardLink(Path.Combine(data, "patients.ndjson"), linked))
        {
            return; // no ln to make the link with
        }

        string[] before = Snapshot(data);

        ToolEnvelope<ViewFacts?> envelope = await RunInAsync(data, new ViewRequest(PatientView, Out: linked, Overwrite: true));

        Facts(envelope).File.ShouldNotBeNull().Rows.ShouldBe(2);
        Snapshot(data).ShouldBe(before);
        (await File.ReadAllTextAsync(linked)).ShouldStartWith("id,gender,birth_date");
    }

    [Fact]
    public async Task GivenARunThatFails_WhenItCreatedDirectories_ThenThoseAreRemovedAndOthersKept()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder().Write("existing/keep.txt", "x");
        string existing = Path.Combine(elsewhere.Path, "existing");

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(FailingView, Out: Path.Combine(existing, "made", "deeper", "rows.csv")));

        Error(envelope).Code.ShouldBe("view-evaluation-failed");
        Directory.Exists(Path.Combine(existing, "made")).ShouldBeFalse();
        Directory.GetFileSystemEntries(existing).ShouldBe([Path.Combine(existing, "keep.txt")]);
    }

    [Fact]
    public async Task GivenAViewWithNoRows_WhenOutNeedsNewDirectories_ThenTheyAreNotLeftBehind()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();

        ToolEnvelope<ViewFacts?> envelope = await RunAsync(folder, new ViewRequest(PatientView, Where: "gender = 'other'", Out: Path.Combine(elsewhere.Path, "made", "rows.csv")));

        Facts(envelope).File.ShouldNotBeNull().Created.ShouldBeFalse();
        Directory.GetFileSystemEntries(elsewhere.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenASuccessfulRun_WhenItCreatedDirectories_ThenTheFileAloneIsInThemWithNoTemporaryFile()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "made", "deeper", "rows.csv");

        await RunAsync(folder, new ViewRequest(PatientView, Out: path));

        Directory.GetFileSystemEntries(Path.GetDirectoryName(path)!).ShouldBe([path]);
    }

    [Fact]
    public async Task GivenOutRefusedBecauseItIsInTheFolder_WhenReported_ThenTheReasonDoesNotSayCsvOrParquetAreReadAsData()
    {
        using TestFolder folder = Clinic();

        IssueItem error = Error(await RunAsync(folder, new ViewRequest(PatientView, Out: Path.Combine(folder.Path, "out.parquet"))));

        error.Code.ShouldBe("out-refused");
        error.Diagnostics.ShouldContain("the folder is never written");
        error.Diagnostics.ShouldNotContain("cache files");
        error.Diagnostics.ShouldNotContain("would be read as data");
    }

    // The tool's own catalog must be able to read what the tool wrote: no byte order mark in front of the first line.
    [Fact]
    public async Task GivenRowsThatAreResourcesWrittenAsNdjson_WhenTheOutputIsCatalogued_ThenEveryLineIsRead()
    {
        using TestFolder folder = Clinic();
        using var elsewhere = new TestFolder();
        string path = Path.Combine(elsewhere.Path, "out", "patients.ndjson");
        const string view = """{"resourceType":"ViewDefinition","resource":"Patient","select":[{"column":[{"name":"resourceType","path":"'Patient'"},{"name":"id","path":"id"},{"name":"gender","path":"gender"}]}]}""";

        await RunAsync(folder, new ViewRequest(view, Out: path));

        (await File.ReadAllBytesAsync(path))[0].ShouldBe((byte)'{');
        var session = new FolderSession(Path.GetDirectoryName(path)!, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        session.Catalog.Diagnostics.ShouldBeEmpty();
        session.Catalog.Entries.Select(entry => entry.Id).ShouldBe(["p1", "p2", "p3"]);
    }

    // The race the handle check closes: the directory passed its last check, then became a link into the data folder just before
    // the file was created, so the file was created in the data folder. No path check can see that once the link is gone again;
    // the open file's real path does. Nothing may be left in the data folder, and the refusal names nothing in it.
    [Fact]
    public void GivenTheDirectorySwappedForALinkIntoTheDataFolderJustBeforeTheFileIsCreated_WhenBegun_ThenItIsRefusedAndTheDataFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there; symbolic links need a privilege on Windows
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string output = Directory.CreateDirectory(Path.Combine(root.Path, "out")).FullName;
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(output, "rows.csv"))).ShouldNotBeNull();
        string[] before = Snapshot(data);
        List<IssueItem> notes = [];

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => target.Begin(notes, beforeCreate: () =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, data);
        }));

        refused.Issue.Code.ShouldBe("out-refused");
        refused.Issue.Diagnostics.ShouldNotContain(data);
        notes.ShouldBeEmpty();
        Snapshot(data).ShouldBe(before);
        Directory.GetFileSystemEntries(output + ".old").ShouldBeEmpty();
    }

    // Outside the data folder too: a file is created where it was checked or not at all.
    [Fact]
    public void GivenTheDirectorySwappedForALinkElsewhereJustBeforeTheFileIsCreated_WhenBegun_ThenItIsRefusedAndNothingIsLeftThere()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        string elsewhere = Directory.CreateDirectory(Path.Combine(root.Path, "elsewhere")).FullName;
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string output = Directory.CreateDirectory(Path.Combine(root.Path, "out")).FullName;
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(output, "rows.csv"))).ShouldNotBeNull();

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => target.Begin([], beforeCreate: () =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, elsewhere);
        }));

        refused.Issue.Code.ShouldBe("out-refused");
        Directory.GetFileSystemEntries(elsewhere).ShouldBeEmpty();
    }

    // The directory was moved away after Begin and a plain directory put in its place: every path check passes again, but the
    // file with the rows is not where its path says. Commit must not rename by that path, and Discard must say where the file is.
    [Fact]
    public async Task GivenTheDirectoryMovedAndAPlainOneMadeInItsPlaceAfterBegin_WhenCommitted_ThenNothingIsRenamedAndTheFileIsReportedWhereItIs()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // Windows does not move a directory with an open file in it
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string output = Directory.CreateDirectory(Path.Combine(root.Path, "out")).FullName;
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(output, "rows.ndjson"))).ShouldNotBeNull();
        OutputFile file = target.Begin([]);
        ViewFileSink sink = ViewFileSink.Create(file.Stream, "ndjson", [new ColumnSchema("id", "string", false)]);
        await sink.WriteAsync(["p1"], CancellationToken.None);
        await sink.CompleteAsync(CancellationToken.None);
        await sink.DisposeAsync();
        Directory.Move(output, output + ".old");
        Directory.CreateDirectory(output);

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => file.Commit());
        IssueItem left = file.Discard().ShouldNotBeNull();

        refused.Issue.Code.ShouldBe("out-refused");
        left.Code.ShouldBe("out-temp-left");
        string moved = Path.Combine(output + ".old", Path.GetFileName(file.TempPath));
        left.Diagnostics.ShouldContain(moved);
        (await File.ReadAllTextAsync(moved)).ShouldContain("p1");
        Directory.GetFileSystemEntries(output).ShouldBeEmpty();
    }

    // A failed run cleans up after itself through a directory that something else removed meanwhile: what is gone is gone, and the
    // cleanup itself must not fail (it did, with DirectoryNotFoundException, which surfaced as view-internal-error).
    [Fact]
    public void GivenTheOutputDirectoryRemovedAfterBegin_WhenDiscarded_ThenNothingThrowsAndNothingIsLeft()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows does not delete a directory with an open file in it
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        var session = new FolderSession(data, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
        string output = Path.Combine(root.Path, "made", "deeper");
        OutputTarget target = OutputTarget.Resolve(session, new ViewRequest(PatientView, Out: Path.Combine(output, "rows.csv"))).ShouldNotBeNull();
        OutputFile file = target.Begin([]);
        Directory.Delete(Path.Combine(root.Path, "made"), recursive: true);

        IssueItem? left = file.Discard();

        left.ShouldBeNull();
        Directory.Exists(Path.Combine(root.Path, "made")).ShouldBeFalse();
    }

    [Fact]
    public void GivenAFileTheHandleShowsLandedSomewhereElse_WhenCheckedAfterTheRename_ThenItIsNamedAndLeftInPlace()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData).Write("elsewhere/rows.csv", "x");
        string data = Path.Combine(root.Path, "data");
        string landed = Path.Combine(root.Path, "elsewhere", "rows.csv");
        var target = new OutputTarget(Path.Combine(root.Path, "out", "rows.csv"), "csv", false, data);

        ToolRequestException error = Should.Throw<ToolRequestException>(() => target.CheckFinal(landed));

        error.Issue.Code.ShouldBe("out-refused");
        error.Issue.Diagnostics.ShouldContain(landed);
        File.Exists(landed).ShouldBeTrue();
    }

    [Fact]
    public void GivenAFileTheHandleShowsInTheDataFolder_WhenCheckedAfterTheRename_ThenItIsNamedAndLeftInPlace()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData).Write("data/rows.csv", "x");
        string data = Path.Combine(root.Path, "data");
        string landed = Path.Combine(CanonicalPath.Resolve(data), "rows.csv");
        var target = new OutputTarget(Path.Combine(root.Path, "out", "rows.csv"), "csv", false, data);

        ToolRequestException error = Should.Throw<ToolRequestException>(() => target.CheckFinal(landed));

        error.Issue.Code.ShouldBe("out-refused");
        error.Issue.Diagnostics.ShouldContain("inside the data folder");
        File.Exists(landed).ShouldBeTrue();
    }

    // A bind mount is the data folder under a path no link leads to: only its device and inode give it away.
    [Fact]
    public async Task GivenABindMountOfTheDataFolder_WhenOutIsUnderIt_ThenItIsRefusedAndTheDataFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // identities are read with statx, which is Linux's
        }

        using TestFolder root = new TestFolder().Write("data/patients.ndjson", PatientsData);
        string data = Path.Combine(root.Path, "data");
        string mount = Directory.CreateDirectory(Path.Combine(root.Path, "mounted")).FullName;
        if (!TryRun("mount", "--bind", data, mount))
        {
            return; // mounting needs privileges this environment does not grant
        }

        try
        {
            string[] before = Snapshot(data);

            IssueItem error = Error(await RunInAsync(data, new ViewRequest(PatientView, Out: Path.Combine(mount, "sub", "rows.csv"))));

            error.Code.ShouldBe("out-refused");
            error.Diagnostics.ShouldContain("bind mount");
            Snapshot(data).ShouldBe(before);
        }
        finally
        {
            TryRun("umount", mount).ShouldBeTrue();
        }
    }

    private static bool TryHardLink(string existing, string link) => TryRun("ln", existing, link);

    private static bool TryRun(string command, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(command) { RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(entry => File.Exists(entry) ? $"{Path.GetRelativePath(root, entry)}:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry)))}" : Path.GetRelativePath(root, entry))];
}
