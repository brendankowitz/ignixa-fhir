using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests;

internal static class TestConformanceBarrierRetryPolicy
{
    public static ConformanceBarrierRetryPolicy Create() =>
        new(
            Substitute.For<IConformanceDefinitionsSynchronizer>(),
            TimeSpan.FromSeconds(30),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);
}
