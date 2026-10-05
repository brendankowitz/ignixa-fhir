// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;
using Ignixa.Application.Features.History;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools.FhirOperations;

public class GetResourceHistoryToolTests
{
    private static readonly byte[] ValidPatientJson =
        Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"p1"}""");

    /// <summary>
    /// Mirrors SqlServerHistoryQueryExecutor.PagingProbeSentinel: the same content-free stand-in a
    /// data layer yields when its lookahead row cannot be mapped.
    /// </summary>
    private static readonly SearchEntryResult PagingProbeSentinel = new(
        ResourceType: string.Empty,
        ResourceId: string.Empty,
        VersionId: string.Empty,
        LastModified: DateTimeOffset.UnixEpoch,
        ResourceBytes: ReadOnlyMemory<byte>.Empty)
    {
        IsPagingProbe = true,
    };

    private static GetResourceHistoryTool CreateTool(IMediator mediator)
    {
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        var tenantStore = Substitute.For<ITenantConfigurationStore>();
        return new GetResourceHistoryTool(contextAccessor, tenantStore, mediator);
    }

    private static SearchEntryResult MakeEntry(string versionId) => new(
        ResourceType: "Patient",
        ResourceId: "p1",
        VersionId: versionId,
        LastModified: DateTimeOffset.UtcNow,
        ResourceBytes: ValidPatientJson);

    // Reproduces the COMPOUND fault: an earlier row inside the requested window failed to map (and
    // was silently dropped, same as before fcbc8f8b), so by the time the loop reaches the lookahead
    // row, entries.Count is still below effectiveCount -- the count=2 break at
    // GetResourceHistoryTool.cs never fires, and the loop reaches the sentinel that stands in for the
    // unmappable lookahead row. Before the IsPagingProbe guard, JsonDocument.Parse on the sentinel's
    // empty ResourceBytes throws JsonException and crashes the tool call. The single-probe case (no
    // earlier drop) never reaches this: the count break fires first and the loop never sees the
    // sentinel.
    [Fact]
    public async Task GivenEarlierUnmappableRowAndUnmappableLookaheadRow_WhenGettingHistory_ThenSkipsSentinelWithoutThrowing()
    {
        // Arrange
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetResourceHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new HistoryResult
            {
                // Only one real row survives an in-window mapping failure; the second position is the
                // probe sentinel standing in for an unmappable lookahead row -- entries.Count (1) never
                // reaches effectiveCount (2), so the loop reaches the sentinel instead of breaking early.
                Entries = ToAsyncEnumerable(MakeEntry("2"), PagingProbeSentinel),
                Links = [],
            });

        var tool = CreateTool(mediator);

        // Act
        var result = await tool.GetResourceHistoryAsync(
            resourceType: "Patient",
            id: "p1",
            count: 2,
            offset: null,
            tenantId: 1,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Entries.Count.ShouldBe(1);
        result.Entries[0].Resource.RootElement.GetProperty("id").GetString().ShouldBe("p1");
    }

    private static async IAsyncEnumerable<SearchEntryResult> ToAsyncEnumerable(
        params SearchEntryResult[] entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
        }

        await Task.CompletedTask;
    }
}
