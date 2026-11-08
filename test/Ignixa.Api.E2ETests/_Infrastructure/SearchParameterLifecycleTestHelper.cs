using System.Net;
using System.Text.Json.Nodes;
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
    /// <summary>Spec §4.4 phase two: commits a staged transition as the transition orchestration would.</summary>
    public static async Task CommitTransitionAsync(IServiceProvider services, string stagedCanonical)
    {
        var state = services.GetRequiredService<ConformanceState>();
        var staged = state.FindByCanonical(stagedCanonical) ?? throw new InvalidOperationException($"Missing staged definition {stagedCanonical}.");
        var outgoing = state.GetSearchParameter(staged.ResourceType, staged.Code)
            ?? throw new InvalidOperationException($"Missing outgoing definition for {staged.ResourceType}.{staged.Code}.");
        await AppendAndRefreshAsync(
            services,
            stagedCanonical,
            SearchParameterStatus.Pending,
            new NewSourceEvent("transition:test", nameof(SearchParameterTransitionCommitted), new SearchParameterTransitionCommitted(
                staged.SearchParamId, [staged.ActivationEventId], [outgoing.DeactivationEventId!.Value])));
    }

    /// <summary>Spec §4.3: enables a pending parameter with a synthetic, guarded reindex start and completion.</summary>
    public static async Task CompleteReindexAsync(IServiceProvider services, string canonical)
    {
        var parameter = services.GetRequiredService<ConformanceState>().FindByCanonical(canonical)
            ?? throw new InvalidOperationException($"Missing pending definition {canonical}.");
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        var jobId = $"test-reindex-{Guid.NewGuid():N}";
        await AppendAndRefreshAsync(
            services,
            canonical,
            SearchParameterStatus.Enabled,
            new NewSourceEvent("reindex:test", nameof(SearchParameterReindexStarted), new SearchParameterReindexStarted(
                parameter.Canonical, parameter.Code, parameter.ResourceType, jobId, [parameter.ResourceType], parameter.ActivationEventId)),
            new NewSourceEvent("reindex:test", nameof(SearchParameterReindexCompleted), new SearchParameterReindexCompleted(
                parameter.Canonical, parameter.Code, parameter.ResourceType, jobId, 0, TimeSpan.Zero, parameter.ActivationEventId)));
    }

    /// <summary>
    /// Spec §4.2: a hidden parameter answers 400 under strict handling, and under lenient handling it is ignored with
    /// a warning saying why. With <paramref name="within"/> it polls until that holds, for a refresh still in flight.
    /// </summary>
    public static async Task AssertHiddenAsync(HttpClient client, string query, string code, bool redefined, TimeSpan? within = null)
    {
        var warning = $"Search parameter '{code}' is {(redefined ? "being redefined" : "pending reindex")} and was ignored.";
        var expires = DateTimeOffset.UtcNow + (within ?? TimeSpan.Zero);
        while (true)
        {
            var strict = await SearchAsync(client, query);
            var lenient = await SearchAsync(client, query, "lenient");
            if (DateTimeOffset.UtcNow >= expires ||
                (strict.StatusCode == HttpStatusCode.BadRequest && lenient.Body.Contains(warning, StringComparison.Ordinal)))
            {
                strict.StatusCode.ShouldBe(HttpStatusCode.BadRequest, strict.Body);
                lenient.StatusCode.ShouldBe(HttpStatusCode.OK, lenient.Body);
                lenient.Body.ShouldContain(warning);
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    public static async Task<(HttpStatusCode StatusCode, string Body)> SearchAsync(HttpClient client, string query, string handling = "strict")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        request.Headers.Add("Prefer", $"handling={handling}");
        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The ids of a strict search's matches; fails unless the search succeeds.</summary>
    public static async Task<IReadOnlyList<string>> SearchIdsAsync(HttpClient client, string query)
    {
        var (statusCode, body) = await SearchAsync(client, query);
        statusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["entry"]?.AsArray().Select(entry => entry!["resource"]!["id"]!.GetValue<string>()).ToArray() ?? [];
    }

    /// <summary>Spec §4.5: renews the staleness lease, for tests whose host does not poll.</summary>
    public static void RenewLease(IServiceProvider services)
    {
        var lease = services.GetRequiredService<ConformanceLease>();
        lease.Renew(lease.CaptureStart());
    }

    private static async Task AppendAndRefreshAsync(
        IServiceProvider services, string canonical, SearchParameterStatus expectedStatus, params NewSourceEvent[] events)
    {
        var state = services.GetRequiredService<ConformanceState>();
        var store = services.GetRequiredService<ISourceEventStore>();
        await store.AppendAsync(events, state.LastProcessedEventId, CancellationToken.None);
        await state.CatchUpAsync(store, CancellationToken.None);
        await services.GetRequiredService<ConformanceRefresher>().RefreshAsync(force: false, CancellationToken.None);
        await WaitForStatusAsync(state, canonical, expectedStatus);
    }

    public static async Task WaitForStatusAsync(
        ConformanceState state,
        string canonical,
        SearchParameterStatus expectedStatus,
        TimeSpan? within = null)
    {
        var timeout = DateTimeOffset.UtcNow + (within ?? TimeSpan.FromSeconds(5));
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
