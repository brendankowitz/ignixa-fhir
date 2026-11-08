// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.FileSystem.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// Regression: the sidecar writer left its temporary file behind when the write or the rename failed. The cancelled write
/// whose temporary file cannot be deleted fails at 78d8b45, where the delete's own I/O error replaced the cancellation.
/// The successful and the plain cancelled writes are guards.
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

        // Moving a file over a directory is an IOException on Linux and an UnauthorizedAccessException on Windows.
        Exception thrown = await Should.ThrowAsync<Exception>(() => AtomicFile.WriteAllTextAsync(target, "{}", NullLogger.Instance, CancellationToken.None));
        (thrown is IOException or UnauthorizedAccessException).ShouldBeTrue();

        Directory.GetFiles(_directory, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAWrite_WhenItSucceeds_ThenOnlyTheTargetExistsWithTheContent()
    {
        string target = Path.Combine(_directory, "z.metadata.json");

        await AtomicFile.WriteAllTextAsync(target, "{\"a\":1}", NullLogger.Instance, CancellationToken.None);

        Directory.GetFiles(_directory).ShouldBe([target]);
        (await File.ReadAllTextAsync(target)).ShouldBe("{\"a\":1}");
    }

    [Fact]
    public async Task GivenACancelledWrite_WhenWriting_ThenTheCancellationSurfacesUnchangedAndNoTemporaryFileRemains()
    {
        string target = Path.Combine(_directory, "c.metadata.json");
        var logger = new RecordingLogger<AtomicFileTests>();

        Exception thrown = await Should.ThrowAsync<Exception>(() => AtomicFile.WriteAllTextAsync(target, "{}", logger, Cancelled()));

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
        Directory.GetFiles(_directory).ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenACancelledWriteWhoseTemporaryFileCannotBeDeleted_WhenWriting_ThenTheCancellationSurfacesAndTheLeftoverIsLogged()
    {
        // The directory does not exist, so deleting the temporary file fails with an I/O error of its own.
        string target = Path.Combine(_directory, "missing", "d.metadata.json");
        var logger = new RecordingLogger<AtomicFileTests>();

        Exception thrown = await Should.ThrowAsync<Exception>(() => AtomicFile.WriteAllTextAsync(target, "{}", logger, Cancelled()));

        thrown.ShouldBeAssignableTo<OperationCanceledException>();
        (LogLevel level, string message, Exception? deleteFailure) = logger.Entries.ShouldHaveSingleItem();
        level.ShouldBe(LogLevel.Warning);
        message.ShouldContain(Path.Combine(_directory, "missing", "d.metadata.json."));
        message.ShouldContain(".tmp");
        deleteFailure.ShouldBeAssignableTo<IOException>();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static CancellationToken Cancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        return source.Token;
    }
}
