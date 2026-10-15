using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Tests;

internal static class TestConformanceBarrierRetryPolicy
{
    public static ConformanceBarrierRetryPolicy Create() =>
        new(
            TestConformanceRefresher.Create(new ConformanceState()),
            Options.Create(new ConformanceTransitionOptions()),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);
}
