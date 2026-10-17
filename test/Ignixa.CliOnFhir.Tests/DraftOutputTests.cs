// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Drafting;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;
using static Ignixa.CliOnFhir.Tests.DraftFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>A draft written with out goes through the output machinery of view, deid and map, never into the data folder (Task 12).</summary>
public class DraftOutputTests
{
    [Fact]
    public async Task GivenAnOutDirectory_WhenDrafted_ThenTheDraftIsWrittenAsTypeAndIdJsonAndNothingElseIsLeft()
    {
        using TestFolder root = ClinicRoot();
        string[] before = Snapshot(Data(root));

        ToolEnvelope<DraftFacts?> envelope = await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root)));

        DraftFacts facts = Facts(envelope);
        string expected = Path.Combine(Out(root), $"ServiceRequest-{Text(facts.Resource["id"])}.json");
        DraftFileFacts file = facts.File.ShouldNotBeNull();
        file.Path.ShouldBe(expected);
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([expected]);
        byte[] bytes = await File.ReadAllBytesAsync(expected);
        file.Bytes.ShouldBe(bytes.Length);
        bytes[0].ShouldBe((byte)'{');
        JsonNode.DeepEquals(JsonNode.Parse(bytes), facts.Resource).ShouldBeTrue();
        Snapshot(Data(root)).ShouldBe(before);
    }

    [Theory]
    [InlineData("data")]
    [InlineData("data/sub")]
    public async Task GivenAnOutDirectoryInTheFolder_WhenDrafted_ThenItIsRefusedAndTheFolderIsUnchanged(string where)
    {
        using TestFolder root = ClinicRoot();
        string[] before = Snapshot(Data(root));

        Error(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Path.Combine(root.Path, where)))).Code.ShouldBe("out-refused");

        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnOutDirectoryAroundTheFolder_WhenDrafted_ThenItIsRefusedAsDeidAndMapRefuseIt()
    {
        using TestFolder root = ClinicRoot();

        Error(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: root.Path))).Code.ShouldBe("out-refused");

        Directory.GetFiles(root.Path).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAPathThroughALinkThenParent_WhenDrafted_ThenItIsResolvedAsTheSystemDoesAndRefusedInTheFolder()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // symbolic links need a privilege on Windows
        }

        using TestFolder root = ClinicRoot();
        string deeper = Directory.CreateDirectory(Path.Combine(root.Path, "other", "deeper")).FullName;
        string link = Path.Combine(root.Path, "lnk");
        Directory.CreateSymbolicLink(link, deeper);
        string[] before = Snapshot(Data(root));

        // lnk/.. is root/other as the system resolves it, but root as text; ../../data from there is the data folder either way.
        Error(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Path.Combine(link, "..", "..", "data")))).Code.ShouldBe("out-refused");

        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnExistingFile_WhenDraftedWithoutAndWithOverwrite_ThenItIsRefusedThenReplaced()
    {
        using TestFolder root = ClinicRoot();
        DraftFacts first = Facts(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root))));
        string path = first.File!.Path;
        await File.WriteAllTextAsync(path, "precious\n");

        IssueItem refused = Error(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root))));
        string kept = await File.ReadAllTextAsync(path);
        DraftFacts replaced = Facts(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root), Overwrite: true)));

        refused.Code.ShouldBe("out-refused");
        kept.ShouldBe("precious\n");
        replaced.File!.Path.ShouldBe(path);
        JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllBytesAsync(path)), replaced.Resource).ShouldBeTrue();
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([path]);
    }

    [Fact]
    public async Task GivenAWriteThatFailsWithOverwrite_WhenDrafted_ThenTheExistingFileIsByteForByteAsItWasAndNoNewFileIsLeft()
    {
        using TestFolder root = ClinicRoot();
        string path = Facts(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root)))).File!.Path;
        await File.WriteAllTextAsync(path, "precious\n");
        byte[] old = await File.ReadAllBytesAsync(path);

        IssueItem error = Error(await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: Out(root), Overwrite: true), new DraftOutputSeams(AfterBegin: _ => throw new IOException("disk full"))));

        error.Code.ShouldBe("out-write-failed");
        (await File.ReadAllBytesAsync(path)).ShouldBe(old);
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([path]);
    }

    [Fact]
    public async Task GivenTheOutDirectorySwappedForALinkIntoTheFolderAfterTheFileIsBegun_WhenDrafted_ThenNothingIsCommittedAndTheFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there; symbolic links need a privilege on Windows
        }

        using TestFolder root = ClinicRoot();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(Data(root), "inner")).FullName;
        string[] before = Snapshot(Data(root));

        ToolEnvelope<DraftFacts?> envelope = await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: output), new DraftOutputSeams(AfterBegin: _ =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, inner);
        }));

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
        envelope.Issues.ShouldContain(issue => issue.Code == "out-temp-left");
        Directory.GetFiles(output + ".old", "ServiceRequest-*.json").ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTheOutDirectorySwappedForALinkIntoTheFolderJustBeforeTheFileIsCreated_WhenDrafted_ThenTheStrayFileIsRemovedAndTheFolderIsUnchanged()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // the open file's path is read from /proc there; symbolic links need a privilege on Windows
        }

        using TestFolder root = ClinicRoot();
        string output = Directory.CreateDirectory(Out(root)).FullName;
        string inner = Directory.CreateDirectory(Path.Combine(Data(root), "inner")).FullName;
        string[] before = Snapshot(Data(root));

        ToolEnvelope<DraftFacts?> envelope = await RunInAsync(Data(root), new DraftRequest("ServiceRequest", MagnesiumOrder, Out: output), new DraftOutputSeams(BeforeCreate: () =>
        {
            Directory.Move(output, output + ".old");
            Directory.CreateSymbolicLink(output, inner);
        }));

        Error(envelope).Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnInvalidDraft_WhenDraftedWithOut_ThenNothingIsWrittenAndAWarningSaysSo()
    {
        using TestFolder root = ClinicRoot();
        string input = MagnesiumOrder.Replace("\"patient\":\"p1\"", "\"patient\":\"nobody\"", StringComparison.Ordinal).Replace("\"reasonReference\":[\"Condition/c1\"],", string.Empty, StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunInAsync(Data(root), new DraftRequest("ServiceRequest", input, Out: Out(root)));

        DraftFacts facts = Facts(envelope);
        facts.Verdict.ShouldBe(DraftFacts.Invalid);
        facts.File.ShouldBeNull();
        envelope.Issues.ShouldContain(issue => issue.Code == "out-not-written");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAWrongDisplayOnTheMedication_WhenDraftedWithOut_ThenNothingIsWritten()
    {
        using TestFolder root = ClinicRoot();
        string input = PotassiumOrder.Replace(PotassiumTabletDisplay, "potassium chloride 10 MEQ tablet", StringComparison.Ordinal);

        ToolEnvelope<DraftFacts?> envelope = await RunInAsync(Data(root), new DraftRequest("MedicationRequest", input, Out: Out(root)));

        Facts(envelope).Verdict.ShouldBe(DraftFacts.Invalid);
        Facts(envelope).File.ShouldBeNull();
        Directory.Exists(Out(root)).ShouldBeFalse();
    }
}
