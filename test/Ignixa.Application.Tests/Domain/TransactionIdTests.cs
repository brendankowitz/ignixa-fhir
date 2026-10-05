// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Models;
using Shouldly;

namespace Ignixa.Application.Tests.Domain;

/// <summary>
/// Regression guard for the transaction ID collision that broke
/// FileBasedSearchServiceProbeRowTests on CI: two <see cref="TransactionId.Generate"/> calls
/// landing in the same clock millisecond used to return the SAME value, and
/// FileBasedFhirRepository names its NDJSON files after that value
/// (tx-{TransactionId}.ndjson) -- a collision meant the second resource's write silently
/// overwrote the first resource's file, while its metadata sidecar still pointed at the (now
/// wrong) content. On fast CI runners, three back-to-back resource writes routinely land in the
/// same millisecond, so this reproduces the collision directly rather than relying on timing.
/// </summary>
public sealed class TransactionIdTests
{
    [Fact]
    public void GivenManyRapidCalls_WhenGenerateCalled_ThenEveryValueIsUnique()
    {
        var ids = new HashSet<long>();

        for (var i = 0; i < 10_000; i++)
        {
            var generated = TransactionId.Generate();
            ids.Add(generated.Value).ShouldBeTrue($"TransactionId {generated.Value} was generated more than once");
        }

        ids.Count.ShouldBe(10_000);
    }

    [Fact]
    public void GivenRapidCalls_WhenGenerateCalled_ThenValuesAreStrictlyIncreasing()
    {
        long previous = TransactionId.Generate().Value;

        for (var i = 0; i < 1_000; i++)
        {
            long next = TransactionId.Generate().Value;
            next.ShouldBeGreaterThan(previous);
            previous = next;
        }
    }
}
