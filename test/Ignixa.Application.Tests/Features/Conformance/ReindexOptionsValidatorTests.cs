using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ReindexOptionsValidatorTests
{
    [Fact]
    public void GivenDefaults_WhenValidated_ThenConfigurationIsAccepted()
    {
        ReindexOptionsValidator.Validate(new ReindexOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidLimits_WhenValidated_ThenAllInvalidKeysAreReported()
    {
        var result = ReindexOptionsValidator.Validate(new ReindexOptions
        {
            DefaultMaximumNumberOfResourcesPerQuery = 0,
            DefaultMaximumNumberOfResourcesPerWrite = 10_001,
            DefaultMaximumConcurrency = 17,
            OrphanGrace = TimeSpan.Zero,
            StaleJobTimeout = TimeSpan.Zero,
            DrainWarningAfter = TimeSpan.Zero,
            ContinueAsNewThreshold = 0
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(message => message.Contains("DefaultMaximumNumberOfResourcesPerQuery", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("DefaultMaximumNumberOfResourcesPerWrite", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("DefaultMaximumConcurrency", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("OrphanGrace", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("StaleJobTimeout", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("DrainWarningAfter", StringComparison.Ordinal));
        result.Failures.ShouldContain(message => message.Contains("ContinueAsNewThreshold", StringComparison.Ordinal));
    }
}
