// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.FileSystem.FileSystem;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.Application.Tests.DataLayer.FileSystem;

/// <summary>
/// FileBasedSearchService.SearchAsync isn't part of ISearchService and has no production caller --
/// SqlServerCompiledSearchService, the other ISearchService implementation, doesn't even define a
/// SearchAsync. It used to be a full, independent copy of SearchStreamAsync's metadata scan,
/// filtering, pagination and paging-probe substitution, with no test exercising it at all: the
/// same defect fixed in both places by the same commit, proven fixed in only one. That gap is
/// exactly the shape of bug this repo has already paid for once (see FileBasedSearchServiceProbeRowTests
/// and TransactionIdTests), so SearchAsync now delegates to SearchStreamAsync instead of duplicating
/// it. These tests assert the delegation is faithful; they don't re-prove SearchStreamAsync's own
/// paging-probe behaviour, which FileBasedSearchServiceProbeRowTests already covers directly and
/// which SearchAsync no longer has an independent copy of to get wrong.
/// </summary>
public sealed class FileBasedSearchServiceSearchAsyncTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), $"ignixa-fs-searchasync-{Guid.NewGuid():N}");
    private readonly FileBasedFhirRepository _repository;
    private readonly FileBasedSearchService _searchService;

    public FileBasedSearchServiceSearchAsyncTests()
    {
        _repository = new FileBasedFhirRepository(_baseDirectory, NullLogger<FileBasedFhirRepository>.Instance);
        _searchService = new FileBasedSearchService(_repository, NullLogger<FileBasedSearchService>.Instance, _baseDirectory);
    }

    public void Dispose()
    {
        _repository.Dispose();
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private async Task CreatePatientAsync(string resourceId)
    {
        var resource = new ResourceWrapper(
            "Patient",
            resourceId,
            "1",
            DateTimeOffset.UtcNow,
            ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"{{resourceId}}"}"""),
            new ResourceRequest("PUT", $"Patient/{resourceId}"));

        await _repository.CreateOrUpdateAsync(resource, CancellationToken.None);
    }

    [Fact]
    public async Task GivenMoreResourcesThanFitOnePage_WhenSearchAsyncCalled_ThenItMatchesSearchStreamAsync()
    {
        var tag = Guid.NewGuid().ToString("N");
        var ids = new[] { $"a-{tag}", $"b-{tag}", $"c-{tag}", $"d-{tag}", $"e-{tag}" };
        foreach (var id in ids)
        {
            await CreatePatientAsync(id);
        }

        var options = new SearchOptions { ResourceType = "Patient", MaxItemCount = 3, ProbeExtraRow = true };

        var viaSearchAsync = await _searchService.SearchAsync(options, CancellationToken.None);

        var viaStream = new List<SearchEntryResult>();
        await foreach (var entry in _searchService.SearchStreamAsync(options, CancellationToken.None))
        {
            viaStream.Add(entry);
        }

        viaSearchAsync.Select(r => r.ResourceId).ShouldBe(viaStream.Select(r => r.ResourceId));
        viaSearchAsync.Select(r => r.IsPagingProbe).ShouldBe(viaStream.Select(r => r.IsPagingProbe));
    }

    [Fact]
    public async Task GivenNoMatchingResources_WhenSearchAsyncCalled_ThenReturnsEmpty()
    {
        var options = new SearchOptions { ResourceType = "Patient", MaxItemCount = 10 };

        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.ShouldBeEmpty();
    }
}
