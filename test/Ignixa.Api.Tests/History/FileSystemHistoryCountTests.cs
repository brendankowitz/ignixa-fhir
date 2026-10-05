using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.History;
using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Api.Tests.History;

public sealed class FileSystemHistoryCountTests : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "history-count-tests", Guid.NewGuid().ToString("N"));
    private readonly FileBasedFhirRepository _repository;

    public FileSystemHistoryCountTests()
    {
        _repository = new FileBasedFhirRepository(_directory, NullLogger<FileBasedFhirRepository>.Instance);
    }

    public void Dispose()
    {
        _repository.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData(0, 2005)]
    [InlineData(1, 2006)]
    [InlineData(2, 2007)]
    public async Task GivenMetadataWithoutBodies_WhenCountingHistory_ThenCountsAllVersionsIncludingDeletes(int scope, int expected)
    {
        for (int version = 1; version <= 2005; version++)
        {
            await WriteMetadataAsync("Patient", "history", version, Epoch, version == 2005);
        }

        await WriteMetadataAsync("Patient", "other", 1, Epoch.AddHours(1));
        await WriteMetadataAsync("Observation", "other", 1, Epoch.AddHours(1));

        (await CountAsync(scope, new HistoryQueryParameters { Count = 1, Offset = 2000 })).ShouldBe(expected);
        (await CountAsync(scope, new HistoryQueryParameters { Since = Epoch, Until = Epoch })).ShouldBe(2005);
        (await CountAsync(scope, new HistoryQueryParameters { Since = Epoch.AddTicks(1), Until = Epoch })).ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenCorruptMetadata_WhenCountingHistory_ThenFailsInsteadOfReportingAnIncompleteTotal(int scope)
    {
        await WriteMetadataAsync("Patient", "history", 1, Epoch);
        string corrupt = Path.Combine(_directory, "_internal", "Patient", "history", "corrupt.metadata.json");
        await File.WriteAllTextAsync(corrupt, "{not json");

        await Should.ThrowAsync<JsonException>(() => CountAsync(scope, new HistoryQueryParameters()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenCancelledCountWithNoHistory_WhenCounting_ThenDoesNotReturnSuccessfulZero(int scope)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CountAsync(scope, new HistoryQueryParameters(), cancellation.Token));
    }

    private async Task WriteMetadataAsync(string resourceType, string resourceId, int version, DateTimeOffset timestamp, bool isDeleted = false)
    {
        string directory = Path.Combine(_directory, "_internal", resourceType, resourceId);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, $"{version}.metadata.json"), JsonSerializer.Serialize(new
        {
            TransactionId = version.ToString(),
            ResourceType = resourceType,
            ResourceId = resourceId,
            VersionId = version.ToString(),
            LastModified = timestamp,
            IsDeleted = isDeleted,
        }));
    }

    private Task<int> CountAsync(int scope, HistoryQueryParameters parameters, CancellationToken cancellationToken = default) => scope switch
    {
        0 => HistoryCountHelper.CountResourceHistoryAsync(_repository, new ResourceKey("Patient", "history"), parameters, cancellationToken),
        1 => HistoryCountHelper.CountTypeHistoryAsync(_repository, "Patient", 1, parameters, cancellationToken),
        _ => HistoryCountHelper.CountSystemHistoryAsync(_repository, 1, parameters, cancellationToken),
    };
}
