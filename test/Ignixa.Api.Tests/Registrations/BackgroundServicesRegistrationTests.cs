using Ignixa.Api.Configuration;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

/// <summary>M-L4 and M-L5: the registered conformance timings derive from the sync interval and validate at startup.</summary>
public sealed class BackgroundServicesRegistrationTests
{
    [Fact]
    public void GivenDefaultConformanceSettings_WhenOptionsAreResolved_ThenOneMissedPollKeepsTheLeaseValid()
    {
        using var provider = BuildProvider();

        var conformance = provider.GetRequiredService<IOptions<ConformanceTransitionOptions>>().Value;

        conformance.SyncIntervalSeconds.ShouldBe(30);
        conformance.MaxStaleness.ShouldBe(TimeSpan.FromSeconds(90));
        conformance.TransitionSafetyMargin.ShouldBe(TimeSpan.FromSeconds(150));
        conformance.TransitionGrace.ShouldBe(TimeSpan.FromSeconds(240));
        provider.GetRequiredService<IOptions<ReindexOptions>>().Value.BarrierDelay.ShouldBe(TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void GivenAShortSyncIntervalAndDerivedDurations_WhenOptionsAreResolved_ThenTheSafetyMarginStillValidates()
    {
        using var provider = BuildProvider(("Conformance:SyncIntervalSeconds", "5"));

        provider.GetRequiredService<IStartupValidator>().Validate();
        var conformance = provider.GetRequiredService<IOptions<ConformanceTransitionOptions>>().Value;

        conformance.MaxStaleness.ShouldBe(TimeSpan.FromSeconds(15));
        conformance.TransitionSafetyMargin.ShouldBe(TimeSpan.FromSeconds(150));
        conformance.TransitionGrace.ShouldBe(TimeSpan.FromSeconds(165));
    }

    [Fact]
    public void GivenReindexEnabledAndTransactionWatcherDisabled_WhenStartingOptionsValidation_ThenStartupFails()
    {
        using var provider = BuildProvider(("Reindex:Enabled", "true"), ("TransactionWatcher:Enabled", "false"));

        var error = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        error.Message.ShouldContain("TransactionWatcher:Enabled must be true when Reindex:Enabled is true.");
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddIgnixaBackgroundServices(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());
        return services.BuildServiceProvider();
    }
}
