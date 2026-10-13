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
    [Theory]
    [InlineData(null, 30, 240)]
    [InlineData("5", 5, 165)]
    public void GivenASyncInterval_WhenOptionsAreResolved_ThenOneMissedPollKeepsTheLeaseValidAndTheTimingsValidate(
        string? syncIntervalSeconds,
        int expectedSyncIntervalSeconds,
        int expectedGraceSeconds)
    {
        using var provider = syncIntervalSeconds is null ? BuildProvider() : BuildProvider(("Conformance:SyncIntervalSeconds", syncIntervalSeconds));

        provider.GetRequiredService<IStartupValidator>().Validate();
        var conformance = provider.GetRequiredService<IOptions<ConformanceTransitionOptions>>().Value;

        var staleness = TimeSpan.FromSeconds(3 * expectedSyncIntervalSeconds);
        (conformance.SyncIntervalSeconds, conformance.MaxStaleness, conformance.TransitionSafetyMargin, conformance.TransitionGrace)
            .ShouldBe((expectedSyncIntervalSeconds, staleness, TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(expectedGraceSeconds)));
        provider.GetRequiredService<IOptions<ReindexOptions>>().Value.BarrierDelay.ShouldBe(staleness);
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
