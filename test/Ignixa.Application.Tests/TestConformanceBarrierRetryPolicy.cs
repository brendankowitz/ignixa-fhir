using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ignixa.Application.Tests;

internal static class TestConformanceBarrierRetryPolicy
{
    public static ConformanceBarrierRetryPolicy Create() =>
        new(
            Substitute.For<IConformanceDefinitionsSynchronizer>(),
            Options.Create(new ConformanceTransitionOptions()),
            NullLogger<ConformanceBarrierRetryPolicy>.Instance);
}
