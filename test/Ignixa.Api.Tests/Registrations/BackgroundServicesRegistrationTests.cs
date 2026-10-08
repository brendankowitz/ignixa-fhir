using Ignixa.Api.Configuration;
using Ignixa.Api.Registrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public sealed class BackgroundServicesRegistrationTests
{
    [Fact]
    public void GivenReindexEnabledAndTransactionWatcherDisabled_WhenStartingOptionsValidation_ThenStartupFails()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reindex:Enabled"] = "true",
                ["TransactionWatcher:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddIgnixaBackgroundServices(configuration);
        using var provider = services.BuildServiceProvider();

        var error = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        error.Message.ShouldContain("TransactionWatcher:Enabled must be true when Reindex:Enabled is true.");
    }
}
