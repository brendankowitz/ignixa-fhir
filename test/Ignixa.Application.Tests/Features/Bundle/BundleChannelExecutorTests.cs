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
        var entriesPastFirstCompleted = 0;
        var slowEntryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowEntry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nonFirstEntriesCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = CreateExecutor(
            slowEntryStarted,
            releaseSlowEntry,
            entryIndex =>
            {
                if (entryIndex != 0 &&
                    Interlocked.Increment(ref entriesPastFirstCompleted) == channelCapacity - 1)
                {
                    nonFirstEntriesCompleted.TrySetResult();
                }
            });
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
        await nonFirstEntriesCompleted.Task.WaitAsync(HangTimeout);

        var entriesYieldedBeforeFirstResponse = Volatile.Read(ref sourceEntriesYielded);
        releaseSlowEntry.SetResult();
        (await firstResponse.WaitAsync(HangTimeout)).ShouldBeTrue();

        var responses = new List<int> { GetResponseIndex(responseEnumerator.Current) };
        responses.AddRange(await CollectRemainingResponseIndexesAsync(responseEnumerator).WaitAsync(HangTimeout));

        entriesYieldedBeforeFirstResponse.ShouldBe(channelCapacity);
        responses.ShouldBe(Enumerable.Range(0, entryCount));
    }

    [Fact]
    public async Task GivenNonZeroFirstEntryIndex_WhenExecutingBatchStreaming_ThenResponsesAreYieldedWithoutDeadlock()
    {
        const int channelCapacity = 8;
        const int firstEntryIndex = 1;
        const int entryCount = channelCapacity + 1;
        using var cancellationSource = new CancellationTokenSource();
        var executor = CreateExecutor(WriteResponseAsync);
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Batch,
            ChannelCapacity = channelCapacity,
            MaxParallelism = 4
        };
        var completion = CollectResponseIndexesAsync(
            executor.ExecuteStreamingAsync(
                CreateEntries(entryCount, static () => { }, firstEntryIndex),
                new ReferenceResolutionContext(),
                options,
                cancellationSource.Token));

        try
        {
            var responses = await completion.WaitAsync(HangTimeout);

            responses.ShouldBe(Enumerable.Range(firstEntryIndex, entryCount));
        }
        finally
        {
            cancellationSource.Cancel();
            try
            {
                await completion;
            }
            catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
            {
            }
        }
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

    [Fact]
    public async Task GivenSourceFaultAfterEarlyDisposal_WhenExecutingBatchStreaming_ThenFaultIsNotSwallowed()
    {
        var sourceMayThrow = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceCancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockedEntryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backgroundWorkStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            CreateEntriesThenThrowAfterCancellationAsync(sourceMayThrow.Task, sourceCancellationObserved),
            new ReferenceResolutionContext(),
            options,
            CancellationToken.None).GetAsyncEnumerator();

        try
        {
            (await responseEnumerator.MoveNextAsync()).ShouldBeTrue();
            GetResponseIndex(responseEnumerator.Current).ShouldBe(0);
            await blockedEntryStarted.Task.WaitAsync(HangTimeout);

            var disposeTask = responseEnumerator.DisposeAsync().AsTask();
            await sourceCancellationObserved.Task.WaitAsync(HangTimeout);
            await backgroundWorkStopped.Task.WaitAsync(HangTimeout);
            await Task.Delay(TimeSpan.FromMilliseconds(25));
            sourceMayThrow.SetResult();

            var exception = await Should.ThrowAsync<JsonException>(async () =>
            {
                await disposeTask.WaitAsync(HangTimeout);
            });

            exception.Message.ShouldBe("The source fault followed cancellation.");
        }
        finally
        {
            sourceMayThrow.TrySetResult();
            await responseEnumerator.DisposeAsync();
        }
    }

    private static BundleChannelExecutor CreateExecutor(
        TaskCompletionSource slowEntryStarted,
        TaskCompletionSource releaseSlowEntry,
        Action<int>? entryCompleted = null)
        => CreateExecutor(context => ExecuteRequestAsync(context, slowEntryStarted, releaseSlowEntry, entryCompleted));

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
        TaskCompletionSource releaseSlowEntry,
        Action<int>? entryCompleted)
    {
        var entryIndex = int.Parse(context.Request.Path.Value!.Split('/')[^1]);
        if (entryIndex == 0)
        {
            slowEntryStarted.TrySetResult();
            await releaseSlowEntry.Task.WaitAsync(context.RequestAborted);
        }

        await WriteResponseAsync(context);
        entryCompleted?.Invoke(entryIndex);
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

        await WriteResponseAsync(context);
    }

    private static async IAsyncEnumerable<BundleEntryContext> CreateEntries(
        int entryCount,
        Action entryYielded,
        int startingIndex = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var offset = 0; offset < entryCount; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryYielded();
            yield return CreateEntry(startingIndex + offset);
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

    private static async IAsyncEnumerable<BundleEntryContext> CreateEntriesThenThrowAfterCancellationAsync(
        Task sourceMayThrow,
        TaskCompletionSource sourceCancellationObserved,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return CreateEntry(0);
        yield return CreateEntry(1);
        using var registration = cancellationToken.Register(() => sourceCancellationObserved.TrySetResult());
        await sourceMayThrow;
        throw new JsonException("The source fault followed cancellation.");
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

    private static Task WriteResponseAsync(HttpContext context)
    {
        var entryIndex = int.Parse(context.Request.Path.Value!.Split('/')[^1]);
        return context.Response.Body.WriteAsync(
            Encoding.UTF8.GetBytes($$"""{"index":{{entryIndex}}}"""),
            context.RequestAborted).AsTask();
    }

    private static async Task<List<int>> CollectResponseIndexesAsync(
        IAsyncEnumerable<BundleEntryResponse> responses)
    {
        var indexes = new List<int>();
        await foreach (var response in responses)
        {
            indexes.Add(GetResponseIndex(response));
        }

        return indexes;
    }

    private static async Task<List<int>> CollectRemainingResponseIndexesAsync(
        IAsyncEnumerator<BundleEntryResponse> responseEnumerator)
    {
        var indexes = new List<int>();
        while (await responseEnumerator.MoveNextAsync())
        {
            indexes.Add(GetResponseIndex(responseEnumerator.Current));
        }

        return indexes;
    }

    private static int GetResponseIndex(BundleEntryResponse response) =>
        JsonDocument.Parse(response.ResourceJson!).RootElement.GetProperty("index").GetInt32();
}
