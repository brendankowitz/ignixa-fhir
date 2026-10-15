using Ignixa.Application.BackgroundOperations.Reindex;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>Spec §6.1: the tuning parameters are validated against their ranges.</summary>
public class ReindexJobParametersTests
{
    [Theory]
    [InlineData(0, 1_000, 4, 0)]
    [InlineData(10_001, 1_000, 4, 0)]
    [InlineData(10_000, 0, 4, 0)]
    [InlineData(10_000, 10_001, 4, 0)]
    [InlineData(10_000, 1_000, 0, 0)]
    [InlineData(10_000, 1_000, 17, 0)]
    [InlineData(10_000, 1_000, 4, -1)]
    [InlineData(10_000, 1_000, 4, 60_001)]
    public void GivenOutOfRangeValue_WhenParametersAreCreated_ThenValidationFails(
        int maximumNumberOfResourcesPerQuery,
        int maximumNumberOfResourcesPerWrite,
        int maximumConcurrency,
        int queryDelayIntervalInMilliseconds)
    {
        Should.Throw<ReindexValidationException>(() => ReindexJobParameters.Create(
            maximumNumberOfResourcesPerQuery,
            maximumNumberOfResourcesPerWrite,
            maximumConcurrency,
            queryDelayIntervalInMilliseconds));
    }
}
