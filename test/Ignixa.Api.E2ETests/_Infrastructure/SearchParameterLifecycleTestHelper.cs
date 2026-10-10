using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Ignixa.Api.E2ETests._Infrastructure;

internal static class SearchParameterLifecycleTestHelper
{
    public static async Task CommitTransitionAsync(IServiceProvider services, string stagedCanonical)
    {
        var state = services.GetRequiredService<ConformanceState>();
        var staged = state.FindByCanonical(stagedCanonical)
            ?? throw new InvalidOperationException($"Missing staged definition {stagedCanonical}.");
        var outgoing = state.GetSearchParameter(staged.ResourceType, staged.Code)
            ?? throw new InvalidOperationException($"Missing outgoing definition for {staged.ResourceType}.{staged.Code}.");
        var store = services.GetRequiredService<ISourceEventStore>();
        await store.AppendAsync(
        [
            new NewSourceEvent(
                "transition:test",
                nameof(SearchParameterTransitionCommitted),
                new SearchParameterTransitionCommitted(
                    staged.SearchParamId,
                    [staged.ActivationEventId],
                    [outgoing.DeactivationEventId!.Value]))
        ],
        state.LastProcessedEventId,
        CancellationToken.None);

        await state.CatchUpAsync(store, CancellationToken.None);
        await services.GetRequiredService<ConformanceRefresher>()
            .RefreshAsync(force: false, CancellationToken.None);
        await WaitForStatusAsync(state, stagedCanonical, SearchParameterStatus.Pending);
    }

    public static async Task CompleteReindexAsync(IServiceProvider services, string canonical)
    {
        var state = services.GetRequiredService<ConformanceState>();
        var parameter = state.FindByCanonical(canonical)
            ?? throw new InvalidOperationException($"Missing pending definition {canonical}.");
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        long activationEventId = parameter.ActivationEventId;
        string jobId = $"test-reindex-{Guid.NewGuid():N}";
        var store = services.GetRequiredService<ISourceEventStore>();

        await store.AppendAsync(
        [
            new NewSourceEvent(
                "reindex:test",
                nameof(SearchParameterReindexStarted),
                new SearchParameterReindexStarted(
                    parameter.Canonical,
                    parameter.Code,
                    parameter.ResourceType,
                    jobId,
                    [parameter.ResourceType],
                    activationEventId)),
            new NewSourceEvent(
                "reindex:test",
                nameof(SearchParameterReindexCompleted),
                new SearchParameterReindexCompleted(
                    parameter.Canonical,
                    parameter.Code,
                    parameter.ResourceType,
                    jobId,
                    0,
                    TimeSpan.Zero,
                    activationEventId))
        ],
        state.LastProcessedEventId,
        CancellationToken.None);

        await state.CatchUpAsync(store, CancellationToken.None);
        await services.GetRequiredService<ConformanceRefresher>()
            .RefreshAsync(force: false, CancellationToken.None);
        await WaitForStatusAsync(state, canonical, SearchParameterStatus.Enabled);
    }

    private static async Task WaitForStatusAsync(
        ConformanceState state,
        string canonical,
        SearchParameterStatus expectedStatus)
    {
        var timeout = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (state.FindByCanonical(canonical)?.Status == expectedStatus)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        var parameter = state.FindByCanonical(canonical)
            ?? throw new InvalidOperationException($"Search parameter {canonical} was not found.");
        parameter.Status.ShouldBe(expectedStatus);
    }
}
