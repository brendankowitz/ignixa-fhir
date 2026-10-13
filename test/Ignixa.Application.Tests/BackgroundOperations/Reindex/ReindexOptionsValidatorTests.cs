using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

/// <summary>Spec §10.2: the Reindex configuration section is validated at startup.</summary>
public class ReindexOptionsValidatorTests
{
    [Fact]
    public void GivenEveryKeyOutOfRange_WhenValidated_ThenEachIsReportedWithItsRange()
    {
        var result = ReindexOptionsValidator.Validate(new ReindexOptions
        {
            DefaultMaximumNumberOfResourcesPerQuery = 10_001,
            DefaultMaximumNumberOfResourcesPerWrite = 0,
            DefaultMaximumConcurrency = 17,
            OrphanGrace = TimeSpan.Zero,
            StaleJobTimeout = TimeSpan.FromMinutes(30),
            DrainWarningAfter = TimeSpan.Zero,
            ContinueAsNewThreshold = 0
        });

        result.Failures.ShouldBe(
        [
            "Reindex:DefaultMaximumNumberOfResourcesPerQuery must be between 1 and 10000.",
            "Reindex:DefaultMaximumNumberOfResourcesPerWrite must be between 1 and 10000.",
            "Reindex:DefaultMaximumConcurrency must be between 1 and 16.",
            "Reindex:OrphanGrace must be greater than zero.",
            "Reindex:DrainWarningAfter must be greater than zero.",
            "Reindex:ContinueAsNewThreshold must be between 1 and 2147483647."
        ]);
    }
}
