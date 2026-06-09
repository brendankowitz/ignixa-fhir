// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Utilities;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Utilities;

public class DebounceInvalidationStrategyTests
{
    [Fact]
    public async Task GivenRapidCoalescedRequests_WhenTimersFireDuringResets_ThenNoUnhandledExceptionCrashesProcess()
    {
        // Regression: timer callbacks used to read CancellationTokenSource.Token after a
        // concurrent reset/dispose had disposed the CTS, throwing ObjectDisposedException
        // from an async-void timer callback and killing the test host.
        using var strategy = new DebounceInvalidationStrategy(TimeSpan.FromMilliseconds(1));
        var executions = 0;

        for (var i = 0; i < 200; i++)
        {
            strategy.RequestInvalidation(
                tenantId: 1,
                () =>
                {
                    Interlocked.Increment(ref executions);
                    return Task.CompletedTask;
                });

            await Task.Delay(2);
        }

        await Task.Delay(100);

        executions.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task GivenPendingInvalidation_WhenDisposedBeforeTimerFires_ThenActionDoesNotExecuteAndNothingThrows()
    {
        var strategy = new DebounceInvalidationStrategy(TimeSpan.FromMilliseconds(50));
        var executedAfterDispose = 0;

        strategy.RequestInvalidation(1, () => Task.CompletedTask);
        strategy.Dispose();

        strategy.RequestInvalidation(
            tenantId: 2,
            () =>
            {
                Interlocked.Increment(ref executedAfterDispose);
                return Task.CompletedTask;
            });

        await Task.Delay(200);

        executedAfterDispose.ShouldBe(0);
    }
}
