// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class BundleChannelExecutorTests
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task GivenSlowFirstEntry_WhenExecutingBatchStreaming_ThenEntriesPulledAheadOfFirstResponseAreBoundedByWindow()
    {
        const int channelCapacity = 8;
        const int entryCount = 1_000;
        var sourceEntriesYielded = 0;
        var slowEntryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowEntry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = CreateExecutor(slowEntryStarted, releaseSlowEntry);
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Batch,
            ChannelCapacity = channelCapacity,
            MaxParallelism = 4
        };

        await using var responseEnumerator = executor.ExecuteStreamingAsync(
            CreateEntries(entryCount, () => Interlocked.Increment(ref sourceEntriesYielded)),
            new ReferenceResolutionContext(),
            options,
            CancellationToken.None).GetAsyncEnumerator();

        var firstResponse = responseEnumerator.MoveNextAsync().AsTask();
        await slowEntryStarted.Task.WaitAsync(HangTimeout);
        await WaitForSourceProgressToStopAsync(() => Volatile.Read(ref sourceEntriesYielded));

        var entriesYieldedBeforeFirstResponse = Volatile.Read(ref sourceEntriesYielded);
        releaseSlowEntry.SetResult();
        (await firstResponse.WaitAsync(HangTimeout)).ShouldBeTrue();

        var responses = new List<int> { GetResponseIndex(responseEnumerator.Current) };
        while (await responseEnumerator.MoveNextAsync())
        {
            responses.Add(GetResponseIndex(responseEnumerator.Current));
        }

        entriesYieldedBeforeFirstResponse.ShouldBeLessThanOrEqualTo(channelCapacity + 1);
        responses.ShouldBe(Enumerable.Range(0, entryCount));
    }

    [Fact]
    public async Task GivenCallerStopsEnumeratingEarly_WhenExecutingBatchStreaming_ThenBackgroundWorkStops()
    {
        var blockedEntryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundWorkStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationSource = new CancellationTokenSource();
        var executor = CreateExecutor(context => ExecuteRequestUntilCancellationAsync(
            context,
            blockedEntryStarted,
            backgroundWorkStopped));
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Batch,
            ChannelCapacity = 8,
            MaxParallelism = 4
        };
        var responseEnumerator = executor.ExecuteStreamingAsync(
            CreateEntries(1_000, static () => { }),
            new ReferenceResolutionContext(),
            options,
            cancellationSource.Token).GetAsyncEnumerator();

        try
        {
            (await responseEnumerator.MoveNextAsync()).ShouldBeTrue();
            (await responseEnumerator.MoveNextAsync()).ShouldBeTrue();
            await blockedEntryStarted.Task.WaitAsync(HangTimeout);

            await responseEnumerator.DisposeAsync().AsTask().WaitAsync(HangTimeout);
            await backgroundWorkStopped.Task.WaitAsync(HangTimeout);
        }
        finally
        {
            cancellationSource.Cancel();
            await responseEnumerator.DisposeAsync();
        }
    }

    [Fact]
    public async Task GivenSourceStreamThrowsMidway_WhenExecutingBatchStreaming_ThenExceptionSurfacesWithoutHanging()
    {
        var sourceMayThrow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedEntryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundWorkStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationSource = new CancellationTokenSource();
        var executor = CreateExecutor(context => ExecuteRequestUntilCancellationAsync(
            context,
            blockedEntryStarted,
            backgroundWorkStopped,
            firstBlockedEntryIndex: 1));
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Batch,
            ChannelCapacity = 8,
            MaxParallelism = 4
        };
        var responseEnumerator = executor.ExecuteStreamingAsync(
            CreateEntriesThenThrowAsync(sourceMayThrow.Task),
            new ReferenceResolutionContext(),
            options,
            cancellationSource.Token).GetAsyncEnumerator();

        try
        {
            (await responseEnumerator.MoveNextAsync()).ShouldBeTrue();
            GetResponseIndex(responseEnumerator.Current).ShouldBe(0);
            await blockedEntryStarted.Task.WaitAsync(HangTimeout);

            sourceMayThrow.SetResult();
            var exception = await Should.ThrowAsync<JsonException>(async () =>
            {
                await responseEnumerator.MoveNextAsync().AsTask().WaitAsync(HangTimeout);
            });

            exception.Message.ShouldBe("The bundle entry source failed.");
            await backgroundWorkStopped.Task.WaitAsync(HangTimeout);
        }
        finally
        {
            cancellationSource.Cancel();
            await responseEnumerator.DisposeAsync();
        }
    }

    private static BundleChannelExecutor CreateExecutor(
        TaskCompletionSource slowEntryStarted,
        TaskCompletionSource releaseSlowEntry)
        => CreateExecutor(context => ExecuteRequestAsync(context, slowEntryStarted, releaseSlowEntry));

    private static BundleChannelExecutor CreateExecutor(Func<HttpContext, Task> executeRequest)
    {
        var pipelineExecutor = Substitute.For<IPipelineExecutor>();
        pipelineExecutor.ExecuteAsync(Arg.Any<HttpContext>())
            .Returns(callInfo => executeRequest(callInfo.Arg<HttpContext>()));

        var requestContextAccessor = new FhirRequestContextAccessor
        {
            RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1)
        };
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };
        var entryExecutor = new BundleEntryExecutor(
            pipelineExecutor,
            httpContextAccessor,
            requestContextAccessor,
            new RecyclableMemoryStreamManager(),
            NullLogger<BundleEntryExecutor>.Instance);

        return new BundleChannelExecutor(entryExecutor, NullLogger<BundleChannelExecutor>.Instance);
    }

    private static async Task ExecuteRequestAsync(
        HttpContext context,
        TaskCompletionSource slowEntryStarted,
        TaskCompletionSource releaseSlowEntry)
    {
        var entryIndex = int.Parse(context.Request.Path.Value!.Split('/')[^1]);
        if (entryIndex == 0)
        {
            slowEntryStarted.TrySetResult();
            await releaseSlowEntry.Task.WaitAsync(context.RequestAborted);
        }

        await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($$"""{"index":{{entryIndex}}}"""), context.RequestAborted);
    }

    private static async Task ExecuteRequestUntilCancellationAsync(
        HttpContext context,
        TaskCompletionSource blockedEntryStarted,
        TaskCompletionSource backgroundWorkStopped,
        int firstBlockedEntryIndex = 2)
    {
        var entryIndex = int.Parse(context.Request.Path.Value!.Split('/')[^1]);
        if (entryIndex >= firstBlockedEntryIndex)
        {
            blockedEntryStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                backgroundWorkStopped.TrySetResult();
                throw;
            }
        }

        await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($$"""{"index":{{entryIndex}}}"""), context.RequestAborted);
    }

    private static async IAsyncEnumerable<BundleEntryContext> CreateEntries(
        int entryCount,
        Action entryYielded,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < entryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryYielded();
            yield return CreateEntry(index);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<BundleEntryContext> CreateEntriesThenThrowAsync(
        Task sourceMayThrow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return CreateEntry(0);
        yield return CreateEntry(1);
        await sourceMayThrow.WaitAsync(cancellationToken);
        throw new JsonException("The bundle entry source failed.");
    }

    private static BundleEntryContext CreateEntry(int index) =>
        new()
        {
            Index = index,
            HttpVerb = "GET",
            ResourceType = "Patient",
            ResourceId = index.ToString(),
            RequestUrl = $"Patient/{index}",
            Resource = null,
            FullUrl = null
        };

    private static async Task WaitForSourceProgressToStopAsync(Func<int> getSourceEntriesYielded)
    {
        var previousCount = getSourceEntriesYielded();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25));
            var currentCount = getSourceEntriesYielded();
            if (currentCount == previousCount)
            {
                return;
            }

            previousCount = currentCount;
        }

        throw new TimeoutException("The entry source did not stop progressing while the first response was blocked.");
    }

    private static int GetResponseIndex(BundleEntryResponse response) =>
        JsonDocument.Parse(response.ResourceJson!).RootElement.GetProperty("index").GetInt32();
}
