// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mapping;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;
using static Ignixa.CliOnFhir.Tests.MapFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A map run writes its file through the same safeguards as a view's output file, all or none.</summary>
public class MapOutputSafetyTests
{
    [Fact]
    public async Task GivenTheOutputDirectorySwappedForALinkIntoTheDataFolderAfterTheFileIsBegun_WhenRun_ThenNothingIsCommittedAndTheDataFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there; symbolic links need a privilege on Windows
        }

        using TestFolder root = Root();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(Data(root), "inner")).FullName;
        string[] before = Snapshot(Data(root));

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: output), afterBegin: _ =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, inner);
        });

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
        envelope.Issues.ShouldContain(issue => issue.Code == "out-temp-left");
        File.Exists(Path.Combine(output + ".old", "Basic.ndjson")).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAWriteThatFailsWithOverwrite_WhenRun_ThenTheExistingFileIsByteForByteAsItWasAndNoNewFileIsLeft()
    {
        using TestFolder root = Root().Write("out/Basic.ndjson", "precious\n");
        byte[] old = await File.ReadAllBytesAsync(Path.Combine(Out(root), "Basic.ndjson"));

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Out(root), Overwrite: true), afterBegin: _ => throw new IOException("disk full")));

        error.Code.ShouldBe("out-write-failed");
        (await File.ReadAllBytesAsync(Path.Combine(Out(root), "Basic.ndjson"))).ShouldBe(old);
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([Path.Combine(Out(root), "Basic.ndjson")]);
    }

    [Fact]
    public async Task GivenAnExistingTypeFileWithoutOverwrite_WhenRun_ThenItIsRefusedAndLeftAsItWas()
    {
        using TestFolder root = Root().Write("out/Basic.ndjson", "old\n");

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Out(root))));

        error.Code.ShouldBe("out-refused");
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Basic.ndjson"))).ShouldBe("old\n");
    }

    [Theory]
    [InlineData("data")]
    [InlineData("data/sub")]
    public async Task GivenAnOutputDirectoryInTheFolder_WhenRun_ThenItIsRefusedAndTheFolderIsUnchanged(string where)
    {
        using TestFolder root = Root();
        string[] before = Snapshot(Data(root));

        Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Path.Combine(root.Path, where)))).Code.ShouldBe("out-refused");

        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenNoResourceTransformed_WhenWritingToADirectory_ThenNoFileIsCreated()
    {
        using TestFolder root = Root();

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Out(root), ResourceType: "Observation"));

        Facts(envelope).Transformed.ShouldBe(0);
        envelope.Issues.ShouldContain(issue => issue.Code == "out-not-created");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }
}
