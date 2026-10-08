// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ignixa.DataLayer.FileSystem.Tests;

/// <summary>
/// <see cref="FileBasedFhirRepository"/> is append-only and cannot physically remove a version, so
/// <c>$bulk-delete</c>'s hard-delete and purge-history modes must be rejected rather than silently
/// no-op'd. Compare the TTL cleanup path's identical contract on <c>TryHardDeleteExpiredResourceAsync</c>.
/// </summary>
public sealed class FileBasedFhirRepositoryBulkDeleteTests : IDisposable
{
    private readonly string _baseDirectory;
    private readonly FileBasedFhirRepository _repository;

    public FileBasedFhirRepositoryBulkDeleteTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), $"ignixa-filerepo-bulkdelete-tests-{Guid.NewGuid()}");
        _repository = new FileBasedFhirRepository(_baseDirectory, NullLogger<FileBasedFhirRepository>.Instance);
    }

    [Fact]
    public void GivenAFileBasedRepository_WhenReadingSupportsPhysicalDeletion_ThenItIsFalse()
    {
        // Act / Assert
        _repository.SupportsPhysicalDeletion.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAFileBasedRepository_WhenHardDeleteAsync_ThenNotSupportedExceptionIsThrown()
    {
        // Arrange
        var key = new ResourceKey("Patient", "bulk-delete-1");

        // Act
        var thrown = await Should.ThrowAsync<NotSupportedException>(
            async () => await _repository.HardDeleteAsync(key));

        // Assert
        thrown.Message.ShouldContain("FileBasedFhirRepository");
    }

    [Fact]
    public async Task GivenAFileBasedRepository_WhenPurgeHistoryAsync_ThenNotSupportedExceptionIsThrown()
    {
        // Arrange
        var key = new ResourceKey("Patient", "bulk-delete-2");

        // Act
        var thrown = await Should.ThrowAsync<NotSupportedException>(
            async () => await _repository.PurgeHistoryAsync(key));

        // Assert
        thrown.Message.ShouldContain("FileBasedFhirRepository");
    }

    public void Dispose()
    {
        _repository.Dispose();

        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }
}
