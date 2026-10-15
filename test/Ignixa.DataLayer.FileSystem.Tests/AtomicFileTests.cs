// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.FileSystem.FileSystem;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// Regression: the sidecar writer left its temporary file behind when the write or the rename failed. The successful
/// write is a guard.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("ignixa-atomicfile-tests-").FullName;

    [Fact]
    public async Task GivenARenameThatFails_WhenWriting_ThenTheFailureSurfacesAndNoTemporaryFileRemains()
    {
        string target = Path.Combine(_directory, "x.metadata.json");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "occupied"), "a directory in the way makes the rename fail");

        await Should.ThrowAsync<IOException>(() => AtomicFile.WriteAllTextAsync(target, "{}", CancellationToken.None));

        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAWrite_WhenItSucceeds_ThenOnlyTheTargetExistsWithTheContent()
    {
        string target = Path.Combine(_directory, "z.metadata.json");

        await AtomicFile.WriteAllTextAsync(target, "{\"a\":1}", CancellationToken.None);

        Directory.GetFiles(_directory).ShouldBe([target]);
        (await File.ReadAllTextAsync(target)).ShouldBe("{\"a\":1}");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
