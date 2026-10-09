using System.Diagnostics.Metrics;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceBarrierRetryPolicyTests
{
    [Fact]
    public async Task GivenTheFirstAllocationIsStale_WhenDefinitionsAreSynchronized_ThenOperationRetriesOnce()
    {
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var policy = CreatePolicy(synchronizer);
        var attempts = 0;
        using var listener = ListenForOutcomes(out var outcomes);

        var result = await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<int>(Stale())
                    : Task.FromResult(42);
            },
            CancellationToken.None);

        result.ShouldBe(42);
        attempts.ShouldBe(2);
        outcomes.ShouldContain("retried_ok");
        await synchronizer.Received(1).SynchronizeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenTheRetryAllocationIsStillStale_WhenOperationRetries_Then503IncludesSyncInterval()
    {
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var policy = CreatePolicy(synchronizer);
        using var listener = ListenForOutcomes(out var outcomes);

        var exception = await Should.ThrowAsync<ConformanceStaleException>(() =>
            policy.ExecuteAsync<int>(
                _ => Task.FromException<int>(Stale()),
                CancellationToken.None));

        exception.StatusCode.ShouldBe(503);
        exception.RetryAfter.ShouldBe(TimeSpan.FromSeconds(17));
        outcomes.ShouldContain("failed");
        await synchronizer.Received(1).SynchronizeAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenTheRetryFailsForAnotherReason_WhenOperationRetries_ThenFailureIsRecordedAndRethrown()
    {
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var policy = CreatePolicy(synchronizer);
        var retryFailure = new IOException("Retry storage failure.");
        var attempts = 0;
        using var listener = ListenForOutcomes(out var outcomes);

        var exception = await Should.ThrowAsync<IOException>(() =>
            policy.ExecuteAsync<int>(
                _ =>
                {
                    attempts++;
                    return attempts == 1
                        ? Task.FromException<int>(Stale())
                        : Task.FromException<int>(retryFailure);
                },
                CancellationToken.None));

        exception.ShouldBeSameAs(retryFailure);
        attempts.ShouldBe(2);
        outcomes.Count(outcome => outcome == "failed").ShouldBe(1);
    }

    private static ConformanceBarrierRetryPolicy CreatePolicy(IConformanceDefinitionsSynchronizer synchronizer) =>
        new(
            synchronizer,
            Options.Create(new ConformanceTransitionOptions { SyncIntervalSeconds = 17 }),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);

    private static StaleConformanceDefinitionsException Stale() => new(101, 11, 29);

    // The meter is process-wide and other test classes record barrier outcomes in parallel;
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
            if (instrument.Meter.Name == "Ignixa.Conformance" &&
                instrument.Name == "conformance.barrier.rejections")
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

            foreach (var tag in tags)
            {
                if (tag.Key == "outcome" && tag.Value is string outcome)
                {
                    lock (captured)
                    {
                        captured.Add(outcome);
                    }
                }
            }
        });
        listener.Start();
        return listener;
    }
}
