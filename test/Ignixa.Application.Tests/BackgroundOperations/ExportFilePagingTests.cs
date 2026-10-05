using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public sealed class ExportFilePagingTests : IDisposable
{
    private readonly string _directory = Path.GetFullPath(Path.Combine("export-file-paging", Guid.NewGuid().ToString("N")));
    private readonly FileBasedFhirRepository _repository;
    private readonly FileBasedSearchService _search;

    public ExportFilePagingTests()
    {
        _repository = new FileBasedFhirRepository(_directory, NullLogger<FileBasedFhirRepository>.Instance);
        _search = new FileBasedSearchService(_repository, NullLogger<FileBasedSearchService>.Instance, _directory);
    }

    [Fact]
    public async Task GivenAnExportPartition_WhenPagingToExhaustion_ThenTheFileProviderAdvancesWithoutRepeatingRows()
    {
        for (var i = 0; i < 5; i++)
        {
            var node = ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p{{i}}"}""");
            await _repository.CreateOrUpdateAsync(new ResourceWrapper("Patient", node.Id!, "1",
                DateTimeOffset.UtcNow, node, new ResourceRequest("PUT", $"Patient/{node.Id}")));
        }
        var options = new SearchOptions
        {
            ResourceType = "Patient", MaxItemCount = 2, StartSurrogateId = 0, EndSurrogateId = 4
        };
        var ids = new List<string>();
        foreach (var offset in new[] { 0, 2, 4, 5 })
        {
            options.ContinuationToken = ContinuationToken.Encode(offset, 2);
            var page = await _search.SearchAsync(options, CancellationToken.None);
            page.Count.ShouldBe(Math.Min(2, 5 - offset));
            ids.AddRange(page.Select(entry => entry.ResourceId));
        }

        ids.ShouldBe(["p0", "p1", "p2", "p3", "p4"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenHealthyExportProbes_WhenUsingProviderContinuations_ThenLookaheadIsNeverRenderedOrDuplicated()
    {
        await SeedAsync(5);
        var options = new SearchOptions
        {
            ResourceType = "Patient", MaxItemCount = 2, ProbeExtraRow = true,
            UseExportContinuation = true, StartSurrogateId = 0, EndSurrogateId = 4
        };
        var ids = new List<string>();
        var pages = 0;
        do
        {
            string? next = null;
            await foreach (var entry in _search.SearchStreamAsync(options, CancellationToken.None))
            {
                if (entry.IsPagingProbe)
                {
                    entry.ResourceBytes.IsEmpty.ShouldBeTrue();
                    entry.ContinuationToken.ShouldNotBeNullOrEmpty();
                    next = entry.ContinuationToken;
                }
                else
                {
                    ids.Add(entry.ResourceId);
                }
            }
            options.ContinuationToken = next!;
            (++pages).ShouldBeLessThanOrEqualTo(3);
        }
        while (options.ContinuationToken is not null);

        pages.ShouldBe(3);
        ids.ShouldBe(["p0", "p1", "p2", "p3", "p4"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenASelectedFileDisappears_WhenExportPaging_ThenOnlyAProbeMayBeMissing(bool missingProbe)
    {
        await SeedAsync(3);
        var metadata = await _repository.GetResourceMetadataAsync("Patient", CancellationToken.None);
        var ids = metadata.Select(entry => entry.Location.Id).ToArray();
        var options = new SearchOptions
        {
            ResourceType = "Patient", MaxItemCount = 2, ProbeExtraRow = true, UseExportContinuation = true
        };
        await using var iterator = _search.SearchStreamAsync(options, CancellationToken.None).GetAsyncEnumerator();
        (await iterator.MoveNextAsync()).ShouldBeTrue();
        iterator.Current.ResourceId.ShouldBe(ids[0]);
        if (missingProbe)
        {
            (await iterator.MoveNextAsync()).ShouldBeTrue();
            iterator.Current.ResourceId.ShouldBe(ids[1]);
        }
        Directory.Delete(Path.Combine(_directory, "_internal", "Patient", ids[missingProbe ? 2 : 1]), recursive: true);
        if (!missingProbe)
        {
            await Should.ThrowAsync<InvalidOperationException>(async () => await iterator.MoveNextAsync());
            return;
        }

        (await iterator.MoveNextAsync()).ShouldBeTrue();
        iterator.Current.IsPagingProbe.ShouldBeTrue();
        iterator.Current.ResourceBytes.IsEmpty.ShouldBeTrue();
        options.ContinuationToken = iterator.Current.ContinuationToken!;
        (await iterator.MoveNextAsync()).ShouldBeFalse();
        (await _search.SearchAsync(options, CancellationToken.None)).ShouldBeEmpty();
    }

    private async Task SeedAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var node = ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p{{i}}"}""");
            await _repository.CreateOrUpdateAsync(new ResourceWrapper("Patient", node.Id!, "1",
                DateTimeOffset.UtcNow, node, new ResourceRequest("PUT", $"Patient/{node.Id}")));
        }
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
