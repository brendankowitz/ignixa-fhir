// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Ignixa.CliOnFhir.Output;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A deid run writes its type files all or none, through the same safeguards as a view's output file.</summary>
public class DeIdOutputSafetyTests
{
    // The writer holds each open temporary file; replacing the directory with a link into the data folder after one is begun must
    // stop the run before anything is renamed, leave no file of any type, put nothing in the data folder, and delete nothing through
    // the link.
    [Fact]
    public async Task GivenTheOutputDirectorySwappedForALinkIntoTheDataFolderAfterAFileIsBegun_WhenRun_ThenNothingIsCommittedAndTheDataFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there; symbolic links need a privilege on Windows
        }

        using TestFolder root = Root();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(Data(root), "inner")).FullName;
        string[] before = Snapshot(Data(root));
        int begun = 0;

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(Config, output), afterBegin: _ =>
        {
            if (++begun == 2)
            {
                Directory.Move(output, output + ".old");
                Directory.CreateSymbolicLink(output, inner);
            }
        });

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
        Directory.GetFileSystemEntries(output + ".old").ShouldAllBe(entry => entry.EndsWith(".tmp", StringComparison.Ordinal));
        envelope.Issues.Where(issue => issue.Code == "out-temp-left").ShouldNotBeEmpty();
        File.Exists(Path.Combine(output + ".old", "Patient.ndjson")).ShouldBeFalse();
        File.Exists(Path.Combine(output + ".old", "Observation.ndjson")).ShouldBeFalse();
    }

    // The swap comes in the instant before a file is created: the file is created through the link, which only its open handle shows.
    [Fact]
    public void GivenTheOutputDirectorySwappedForALinkIntoTheDataFolderBeforeAFileIsCreated_WhenBegun_ThenItIsRefusedAndTheDataFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using TestFolder root = Root();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string[] before = Snapshot(Data(root));
        var session = OpenSession(Data(root));
        OutputDirectory directory = OutputDirectory.Resolve(session.RootPath, output, overwrite: false);
        OutputTarget target = directory.File("Patient.ndjson");

        ToolRequestException refused = Should.Throw<ToolRequestException>(() => target.Begin([], beforeCreate: () =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, Data(root));
        }));

        refused.Issue.Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
    }

    // Only the second file is moved away: every file is checked before any is renamed, so the first, which is fine, is not renamed either.
    [Fact]
    public async Task GivenOneTemporaryFileMovedAwayAfterItIsBegun_WhenRun_ThenNoFileOfAnyTypeIsCommitted()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there
        }

        using TestFolder root = Root();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string aside = Directory.CreateDirectory(Path.Combine(root.Path, "aside")).FullName;
        int begun = 0;

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(Config, output), afterBegin: file =>
        {
            if (++begun == 2)
            {
                File.Move(file.TempPath, Path.Combine(aside, "moved.tmp"));
            }
        });

        Error(envelope).Code.ShouldBe("out-refused");
        envelope.Issues.ShouldNotContain(issue => issue.Code == "out-partly-committed");
        Directory.GetFileSystemEntries(output).ShouldBeEmpty();
        envelope.Issues.ShouldContain(issue => issue.Code == "out-temp-left" && issue.Diagnostics.Contains(Path.Combine(aside, "moved.tmp")));
    }

    [Fact]
    public async Task GivenAWriteThatFailsPartWayWithOverwrite_WhenRun_ThenTheExistingFileIsByteForByteAsItWasAndNoNewFileIsLeft()
    {
        using TestFolder root = Root().Write("out/Patient.ndjson", "precious\n");
        byte[] old = await File.ReadAllBytesAsync(Path.Combine(Out(root), "Patient.ndjson"));
        int begun = 0;

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root), Overwrite: true), afterBegin: _ =>
        {
            if (++begun == 2)
            {
                throw new IOException("disk full");
            }
        }));

        error.Code.ShouldBe("out-write-failed");
        (await File.ReadAllBytesAsync(Path.Combine(Out(root), "Patient.ndjson"))).ShouldBe(old);
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([Path.Combine(Out(root), "Patient.ndjson")]);
    }

    [Fact]
    public async Task GivenARunThatFailsAfterCreatingTheOutputDirectory_WhenRun_ThenTheDirectoriesItCreatedAreRemoved()
    {
        using TestFolder root = Root();
        string output = Path.Combine(root.Path, "made", "deeper");

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, output), afterBegin: _ => throw new IOException("disk full")));

        error.Code.ShouldBe("out-write-failed");
        Directory.Exists(Path.Combine(root.Path, "made")).ShouldBeFalse();
    }

    // The renames are one per file: a rename that fails after another succeeded rolls that one back, so nothing new is left.
    [Fact]
    public async Task GivenARenameThatFailsAfterAnotherSucceeded_WhenRun_ThenTheFirstIsRolledBackAndNoNewFileIsLeft()
    {
        using TestFolder root = Root();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        int begun = 0;

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(Config, output), afterBegin: _ =>
        {
            if (++begun == 2)
            {
                // A directory where the second file is to be renamed to: its rename fails, after the first one's.
                Directory.CreateDirectory(Path.Combine(output, "Patient.ndjson"));
            }
        });

        Error(envelope).Code.ShouldBe("out-write-failed");
        envelope.Issues.ShouldNotContain(issue => issue.Code == "out-rollback-left");
        Directory.GetFiles(output).ShouldBeEmpty();
    }

    // With overwrite, the targets a failed run had already replaced are restored byte for byte.
    [Fact]
    public async Task GivenARenameThatFailsAfterAnotherReplacedAFileWithOverwrite_WhenRun_ThenThatFileIsRestored()
    {
        using TestFolder root = Root().Write("out/Observation.ndjson", "old observations\n").Write("out/Patient.ndjson", "old patients\n");
        string output = Out(root);
        byte[] observations = await File.ReadAllBytesAsync(Path.Combine(output, "Observation.ndjson"));
        int begun = 0;

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(Config, output, Overwrite: true), afterBegin: _ =>
        {
            if (++begun == 2)
            {
                File.Delete(Path.Combine(output, "Patient.ndjson"));
                Directory.CreateDirectory(Path.Combine(output, "Patient.ndjson"));
            }
        });

        Error(envelope).Code.ShouldBe("out-write-failed");
        envelope.Issues.ShouldNotContain(issue => issue.Code == "out-rollback-left");
        (await File.ReadAllBytesAsync(Path.Combine(output, "Observation.ndjson"))).ShouldBe(observations);
        Directory.GetFiles(output).ShouldBe([Path.Combine(output, "Observation.ndjson")]);
    }

    [Fact]
    public async Task GivenASuccessfulRunWithOverwrite_WhenTheTargetsExisted_ThenTheyAreReplacedAndNoBackupIsLeft()
    {
        using TestFolder root = Root().Write("out/Observation.ndjson", "old\n").Write("out/Patient.ndjson", "old\n");

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(Config, Out(root), Overwrite: true)));

        facts.Written.ShouldBe(4);
        Directory.GetFileSystemEntries(Out(root)).Order(StringComparer.Ordinal).ShouldBe([Path.Combine(Out(root), "Observation.ndjson"), Path.Combine(Out(root), "Patient.ndjson")]);
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Patient.ndjson"))).ShouldNotBe("old\n");
    }

    [Fact]
    public async Task GivenAnOutputPathThatIsAFile_WhenRun_ThenItIsRefused()
    {
        using TestFolder root = Root().Write("out", "a file");

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root))));

        error.Code.ShouldBe("out-refused");
        error.Diagnostics.ShouldContain("is a file");
    }

    [Fact]
    public async Task GivenABindMountOfTheDataFolder_WhenTheOutputDirectoryIsUnderIt_ThenItIsRefused()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // identities are read with statx, which is Linux's
        }

        using TestFolder root = Root();
        string mount = Directory.CreateDirectory(Path.Combine(root.Path, "mounted")).FullName;
        if (!TryRun("mount", "--bind", Data(root), mount))
        {
            return; // mounting needs privileges this environment does not grant
        }

        try
        {
            string[] before = Snapshot(Data(root));

            IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, mount)));

            error.Code.ShouldBe("out-refused");
            error.Diagnostics.ShouldContain("bind mount");
            Snapshot(Data(root)).ShouldBe(before);
        }
        finally
        {
            TryRun("umount", mount).ShouldBeTrue();
        }
    }

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
}
