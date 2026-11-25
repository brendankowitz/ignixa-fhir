// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace Ignixa.SqlOnFhir.Fabric.Delta.Tests;

public sealed class ExclusiveFilePublisherTests : IDisposable
{
    private readonly TemporaryDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task GivenFreeName_WhenPublishing_ThenTheFileHoldsTheContentAndNoStagingFileRemains()
    {
        var path = _directory.Combine("00000000000000000000.json");

        var published = await ExclusiveFilePublisher.TryPublishAsync(path, Encoding.UTF8.GetBytes("ours\n"), CancellationToken.None);

        published.ShouldBeTrue();
        (await File.ReadAllTextAsync(path)).ShouldBe("ours\n");
        EntryNames().ShouldBe(["00000000000000000000.json"]);
    }

    [Fact]
    public async Task GivenTakenName_WhenPublishing_ThenItReportsFalseAndLeavesTheExistingFileUntouched()
    {
        var path = _directory.Combine("00000000000000000000.json");
        await File.WriteAllTextAsync(path, "theirs\n");

        var published = await ExclusiveFilePublisher.TryPublishAsync(path, Encoding.UTF8.GetBytes("ours\n"), CancellationToken.None);

        published.ShouldBeFalse();
        (await File.ReadAllTextAsync(path)).ShouldBe("theirs\n");
        EntryNames().ShouldBe(["00000000000000000000.json"]);
    }

    [Fact]
    public async Task GivenCanceledToken_WhenPublishing_ThenNothingIsPublishedAndNoStagingFileRemains()
    {
        var path = _directory.Combine("00000000000000000000.json");

        await Should.ThrowAsync<OperationCanceledException>(
            () => ExclusiveFilePublisher.TryPublishAsync(path, Encoding.UTF8.GetBytes("ours\n"), new CancellationToken(canceled: true)));

        EntryNames().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenDirectoryAtTheTargetName_WhenPublishing_ThenNothingIsPublishedAndNoStagingFileRemains()
    {
        var path = _directory.Combine("00000000000000000000.json");
        Directory.CreateDirectory(path);

        // Windows reports access denied for a directory target; Unix link() reports EEXIST. Either way nothing is replaced.
        try
        {
            (await ExclusiveFilePublisher.TryPublishAsync(path, Encoding.UTF8.GetBytes("ours\n"), CancellationToken.None))
                .ShouldBeFalse();
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
        }

        Directory.Exists(path).ShouldBeTrue();
        EntryNames().ShouldBe(["00000000000000000000.json"]);
    }

    [Theory]
    [InlineData(".00000000000000000000.json.0123456789abcdef.tmp", true)]
    [InlineData(".anything.tmp", true)]
    [InlineData("00000000000000000000.json", false)]
    [InlineData("00000000000000000000.json.tmp", false)]
    [InlineData(".00000000000000000000.json", false)]
    [InlineData(".x.TMP", false)]
    public void GivenFileName_WhenCheckingForStagingName_ThenOnlyHiddenTmpFilesMatch(string fileName, bool expected)
    {
        ExclusiveFilePublisher.IsStagingFileName(fileName).ShouldBe(expected);
    }

    private string?[] EntryNames() => [.. Directory.GetFileSystemEntries(_directory.Path).Select(Path.GetFileName)];
}
