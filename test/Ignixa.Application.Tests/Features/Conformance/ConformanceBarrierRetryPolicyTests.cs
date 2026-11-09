using System.Diagnostics.Metrics;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceBarrierRetryPolicyTests
{
    public enum RetryAttempt
    {
        Succeeds,
        IsStillStale,
        FailsForAnotherReason,
    }

    // §5.3: a stale allocation synchronizes definitions and retries once; a second stale rejection is a 503.
    [Theory]
    [InlineData(RetryAttempt.Succeeds, "retried_ok")]
    [InlineData(RetryAttempt.IsStillStale, "failed")]
    [InlineData(RetryAttempt.FailsForAnotherReason, "failed")]
    public async Task GivenTheFirstAllocationIsStale_WhenDefinitionsAreSynchronizedAndTheOperationRetries_ThenTheRetryOutcomeIsRecordedAndSurfaced(
        RetryAttempt retry,
        string expectedOutcome)
    {
        var eventStore = TestConformanceRefresher.EmptyEventStore();
        var policy = CreatePolicy(eventStore);
        var retryFailure = new IOException("Retry storage failure.");
        var attempts = 0;
        using var listener = ListenForOutcomes(out var outcomes);

        var execution = policy.ExecuteAsync(
            _ => ++attempts == 1 || retry == RetryAttempt.IsStillStale
                ? Task.FromException<int>(TestWritePath.Stale())
                : retry == RetryAttempt.Succeeds ? Task.FromResult(42) : Task.FromException<int>(retryFailure),
            CancellationToken.None);

        switch (retry)
        {
            case RetryAttempt.Succeeds:
                (await execution).ShouldBe(42);
                break;
            case RetryAttempt.IsStillStale:
                var stale = await Should.ThrowAsync<ConformanceStaleException>(execution);
                stale.StatusCode.ShouldBe(503);
                stale.RetryAfter.ShouldBe(TimeSpan.FromSeconds(17));
                break;
            default:
                (await Should.ThrowAsync<IOException>(execution)).ShouldBeSameAs(retryFailure);
                break;
        }

        attempts.ShouldBe(2);
        outcomes.Count(outcome => outcome == expectedOutcome).ShouldBe(1);
        _ = eventStore.Received(1).ReadFromAsync(0, CancellationToken.None);
    }

    private static ConformanceBarrierRetryPolicy CreatePolicy(ISourceEventStore eventStore) =>
        new(TestConformanceRefresher.Create(new ConformanceState(), eventStore), Options.Create(new ConformanceTransitionOptions { SyncIntervalSeconds = 17 }), NullLogger<ConformanceBarrierRetryPolicy>.Instance);

    // Finding F4: the meter is process-wide and other test classes record barrier outcomes in parallel;
    // measurements are recorded synchronously, so an AsyncLocal scopes capture to this test's flow.
    private static readonly AsyncLocal<bool> _capturing = new();

    private static MeterListener ListenForOutcomes(out List<string> outcomes)
    {
        _capturing.Value = true;
        outcomes = [];
        var captured = outcomes;
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "Ignixa.Conformance" && instrument.Name == "conformance.barrier.rejections")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (!_capturing.Value)
            {
                return;
            }

            lock (captured)
            {
                captured.AddRange(tags.ToArray().Where(tag => tag.Key == "outcome").Select(tag => tag.Value).OfType<string>());
            }
        });
        listener.Start();
        return listener;
    }
}
