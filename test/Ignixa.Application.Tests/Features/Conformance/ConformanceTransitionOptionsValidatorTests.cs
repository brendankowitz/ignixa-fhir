using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceTransitionOptionsValidatorTests
{
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
