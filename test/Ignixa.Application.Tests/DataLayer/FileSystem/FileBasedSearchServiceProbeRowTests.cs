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
/// Ports fcbc8f8b's SQL Server "corrupt probe row" regression guard to the FileSystem data layer,
/// which has the same defect shape: <see cref="FileBasedFhirRepository.GetAsync"/> returns null for
/// a resource its own metadata scan (<see cref="FileBasedSearchService.SearchStreamAsync{TSearchOptions}"/>'s
/// Step 1) already reported as present -- a concurrent-delete race -- and, before this fix, that row
/// was silently dropped rather than substituted with a <see cref="SearchEntryResult.IsPagingProbe"/>
/// sentinel, letting a probe row's proof that a further page exists vanish along with it.
/// <para>
/// Only the concurrent-delete race can reach this code with a null result: FileBasedFhirRepository's
/// OTHER null-producing path doesn't exist -- a genuinely corrupt or missing NDJSON resource file
/// makes <c>GetAsync</c> throw instead (see its own try/catch), never return null. So the race is
/// simulated directly rather than via file corruption: the test drives the returned
/// <see cref="IAsyncEnumerable{T}"/> one step at a time and deletes the not-yet-fetched probe
/// resource's on-disk metadata between the two real page rows and the probe row -- exactly the
/// window between Step 1 (metadata scan, already captured into the page) and Step 4's per-key fetch
/// that a genuine concurrent delete would land in.
/// </para>
/// </summary>
public sealed class FileBasedSearchServiceProbeRowTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), $"ignixa-fs-probe-{Guid.NewGuid():N}");
    private readonly FileBasedFhirRepository _repository;
    private readonly FileBasedSearchService _searchService;

    public FileBasedSearchServiceProbeRowTests()
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
    public async Task GivenAConcurrentlyDeletedProbeRow_WhenSearchStreamAsyncCalled_ThenYieldsAPagingProbeSentinel()
    {
        // Arrange -- 3 Patients, page size 2 with ProbeExtraRow: the third row fetched is purely the
        // lookahead FileBasedSearchService.FetchCount asks for.
        var tag = Guid.NewGuid().ToString("N");
        var ids = new[] { $"probe-fs-a-{tag}", $"probe-fs-b-{tag}", $"probe-fs-c-{tag}" };
        foreach (var id in ids)
        {
            await CreatePatientAsync(id);
        }

        var options = new SearchOptions { ResourceType = "Patient", MaxItemCount = 2, ProbeExtraRow = true };

        // Act -- drive the enumerable by hand so the deletion can land between the two real page
        // rows and the still-unfetched probe row.
        var results = new List<SearchEntryResult>();
        await using var enumerator = _searchService.SearchStreamAsync(options, CancellationToken.None).GetAsyncEnumerator();

        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        results.Add(enumerator.Current);
        (await enumerator.MoveNextAsync()).ShouldBeTrue();
        results.Add(enumerator.Current);

        // The metadata scan behind these two rows already captured all 3 ids into the page before
        // either MoveNextAsync call above ran -- FileBasedSearchService.SearchStreamAsync's Step 3
        // materializes pagedKeys with .ToList() before Step 4 ever calls GetAsync. Deleting the
        // third (not yet fetched) resource's metadata now reproduces a genuine concurrent delete: its
        // presence was already proven, but by the time its own row is fetched, it's gone.
        var probeId = ids.Single(id => results.All(r => r.ResourceId != id));
        Directory.Delete(Path.Combine(_baseDirectory, "_internal", "Patient", probeId), recursive: true);

        (await enumerator.MoveNextAsync()).ShouldBeTrue(
            "the probe row's proof that a further page exists must survive its own concurrent deletion");
        results.Add(enumerator.Current);

        (await enumerator.MoveNextAsync()).ShouldBeFalse();

        // Assert -- the two real page rows are ordinary content, and the third is the content-free
        // sentinel standing in for the deleted lookahead row.
        results.Count.ShouldBe(3);
        results.Take(2).ShouldAllBe(r => !r.IsPagingProbe);
        results[2].IsPagingProbe.ShouldBeTrue();
    }
}
