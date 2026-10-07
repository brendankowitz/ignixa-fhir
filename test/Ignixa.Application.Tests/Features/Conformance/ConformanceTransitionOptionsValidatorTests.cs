using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceTransitionOptionsValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveSyncInterval_WhenValidated_ThenItFails(int seconds)
    {
        var result = ConformanceTransitionOptionsValidator.Validate(
            new ConformanceTransitionOptions
            {
                SyncIntervalSeconds = seconds,
                MaxStaleness = TimeSpan.FromSeconds(10),
                TransitionGrace = TimeSpan.FromSeconds(11),
            },
            new ReindexOptions { BarrierDelay = TimeSpan.FromSeconds(10) });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveMaxStaleness_WhenValidated_ThenItFails(int seconds)
    {
        var result = ConformanceTransitionOptionsValidator.Validate(
            new ConformanceTransitionOptions
            {
                MaxStaleness = TimeSpan.FromSeconds(seconds),
                TransitionGrace = TimeSpan.FromSeconds(11),
            },
            new ReindexOptions { BarrierDelay = TimeSpan.FromSeconds(10) });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GivenNonPositiveTransitionGrace_WhenValidated_ThenItFails(int seconds)
    {
        var result = ConformanceTransitionOptionsValidator.Validate(
            new ConformanceTransitionOptions
            {
                MaxStaleness = TimeSpan.FromSeconds(-2),
                TransitionGrace = TimeSpan.FromSeconds(seconds),
            },
            new ReindexOptions { BarrierDelay = TimeSpan.FromSeconds(0) });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void GivenNegativeBarrierDelay_WhenValidated_ThenItFails()
    {
        var result = ConformanceTransitionOptionsValidator.Validate(
            new ConformanceTransitionOptions
            {
                MaxStaleness = TimeSpan.FromSeconds(-2),
                TransitionGrace = TimeSpan.FromSeconds(-1),
            },
            new ReindexOptions { BarrierDelay = TimeSpan.FromSeconds(-1) });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void GivenGraceEqualToMaxStaleness_WhenValidated_ThenItFails()
    {
        var options = new ConformanceTransitionOptions
        {
            MaxStaleness = TimeSpan.FromSeconds(10),
            TransitionGrace = TimeSpan.FromSeconds(10),
        };

        var result = ConformanceTransitionOptionsValidator.Validate(options, new ReindexOptions
        {
            BarrierDelay = TimeSpan.FromSeconds(10),
        });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void GivenBarrierDelayBelowMaxStaleness_WhenValidated_ThenItFails()
    {
        var options = new ConformanceTransitionOptions
        {
            MaxStaleness = TimeSpan.FromSeconds(10),
            TransitionGrace = TimeSpan.FromSeconds(11),
        };

        var result = ConformanceTransitionOptionsValidator.Validate(options, new ReindexOptions
        {
            BarrierDelay = TimeSpan.FromSeconds(9),
        });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void GivenSafeTransitionDurations_WhenValidated_ThenItSucceeds()
    {
        var options = new ConformanceTransitionOptions
        {
            MaxStaleness = TimeSpan.FromSeconds(10),
            TransitionGrace = TimeSpan.FromSeconds(11),
        };

        var result = ConformanceTransitionOptionsValidator.Validate(options, new ReindexOptions
        {
            BarrierDelay = TimeSpan.FromSeconds(10),
        });

        result.Succeeded.ShouldBeTrue();
    }
}
