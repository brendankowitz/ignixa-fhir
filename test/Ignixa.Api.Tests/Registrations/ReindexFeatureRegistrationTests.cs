using Autofac;
using Ignixa.Api.Registrations;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public sealed class ReindexFeatureRegistrationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GivenReindexConfiguration_WhenRegisteringPackageFeatures_ThenCapabilityMatchesEnabled(
        bool enabled,
        bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Reindex:Enabled"] = enabled.ToString()
            })
            .Build();
        var builder = new ContainerBuilder();
        builder.RegisterApplicationServices(configuration);
        using var container = builder.Build();

        var registered = container.Resolve<IEnumerable<IPackageFeature>>()
            .Any(feature => feature.PackageId == "ignixa.reindex");

        registered.ShouldBe(expected);
    }
}
